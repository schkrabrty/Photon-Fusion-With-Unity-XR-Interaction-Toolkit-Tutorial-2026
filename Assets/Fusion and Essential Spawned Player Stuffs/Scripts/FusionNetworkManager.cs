using System;
using System.Collections;
using System.Threading.Tasks;
using Fusion;
using Photon.Realtime;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using PhotonAppSettings = Fusion.Photon.Realtime.PhotonAppSettings;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// One persistent manager for the Fusion connection, lobby UI and Photon Voice.
// FusionLobbyState shares the countdown; FusionNetworkPlayer shares each avatar's poses/color.
[RequireComponent(typeof(NetworkRunner), typeof(NetworkEvents), typeof(NetworkSceneManagerDefault))]
[RequireComponent(typeof(FusionPlayerSpawner))]
[RequireComponent(typeof(FusionVoiceClient), typeof(Recorder), typeof(VoiceLogger))]
[DisallowMultipleComponent]
public class FusionNetworkManager : MonoBehaviour
{
    public static FusionNetworkManager Instance { get; private set; }
    [Header("Room")]
    public string SessionName = "Room 1";
    [Range(2, FusionLobbyState.PlayerSlotCapacity)] public int MaxPlayers = 2;
    [Min(2)] public int MinimumPlayers = 2;
    [Tooltip("Countdown started when Minimum Players have joined.")]
    [Min(0.1f)] public float WaitingDelay = 30f;
    [Tooltip("When the room fills, shorten the remaining countdown to this many seconds. Never extends it.")]
    [FormerlySerializedAs("StartDelay")]
    [Min(0.1f)] public float FullRoomDelay = 5f;
    public int GameSceneBuildIndex = 1;
    public NetworkObject PlayerPrefab;
    [Tooltip("Seconds before automatically retrying a failed connection or a closed/full room.")]
    [Min(1f)] public float ReconnectDelay = 5f;

    [Header("Game spawn positions")]
    [Tooltip("Starting distance in metres between players. Positions form a row centred on Game's XR Origin.")]
    [Min(0.1f)] public float PlayerSpawnSpacing = 0.7f;

    [Header("Existing lobby canvas")]
    public GameObject WaitingRoomPanel;
    public TMP_Text StatusText;
    public TMP_Text PlayerCountText;
    public TMP_Text CountdownText;

    [Header("Canvas height")]
    public bool MatchCameraHeight = true;
    public Transform LobbyCanvas;
    public Camera LobbyCamera;
    [Tooltip("World-space height above the camera. Zero keeps the canvas at eye height.")]
    public float CanvasHeightOffset = 0f;

    public enum LobbyPhase { Connecting, Ready, Joining, InRoom, Leaving, Failed }
    public LobbyPhase Phase { get; private set; } = LobbyPhase.Connecting;
    private NetworkRunner _runner;
    private int _lobbyIndex;
    private bool _cameraWarningLogged;
    private float _retryAt;
    public NetworkRunner Runner => _runner;

    // Voice uses the same manager throughout Lobby -> Game -> Lobby.
    private FusionVoiceClient _voice;
    private Recorder _recorder;
    private NetworkEvents _networkEvents;
    private bool _microphoneAllowed;
    private bool _voiceCallbacksBound;
    private bool _voiceStopping;

    #region Manager lifetime

    private void Awake()
    {
        // Returning to Lobby creates another scene copy. Keep our connected manager
        // and take only the fresh UI references from that copy.
        if (Instance && Instance != this)
        {
            Instance.BindLobbyCanvas(this);
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _lobbyIndex = gameObject.scene.buildIndex;
        _runner = GetComponent<NetworkRunner>();
        _networkEvents = GetComponent<NetworkEvents>();
        var spawner = GetComponent<FusionPlayerSpawner>();
        spawner.PlayerPrefab = PlayerPrefab;
        spawner.LobbySceneBuildIndex = _lobbyIndex;
        spawner.GameSceneBuildIndex = GameSceneBuildIndex;
        ConfigureVoice();
        DontDestroyOnLoad(gameObject);
        FindLobbyCanvas();
    }

    private async void Start()
    {
        if (Instance != this) return;
        // Voice follows the Fusion room. Requesting permission does not hold up joining.
        StartVoice();
        await ConnectToServerAsync();
    }

    private void Update()
    {
        // Recovery is automatic; there are no Join, Cancel or Retry buttons.
        if (Phase == LobbyPhase.Ready && (!_runner || !_runner.IsCloudReady))
            SetPhase(LobbyPhase.Failed, "Connection lost. Retrying automatically...");
        if (Phase == LobbyPhase.Failed && Time.unscaledTime >= _retryAt)
        {
            RestartConnection();
            return;
        }
        if (Phase == LobbyPhase.InRoom && (!_runner || !_runner.IsRunning))
        {
            RestartConnection();
            return;
        }
        RefreshUI();
    }

    private void OnDestroy()
    {
        if (_voiceCallbacksBound)
        {
            if (_voice)
            {
                _voice.Client.StateChanged -= OnVoiceStateChanged;
                _voice.SpeakerLinked -= OnSpeakerLinked;
            }
            if (_networkEvents) _networkEvents.OnShutdown.RemoveListener(OnVoiceRunnerShutdown);
        }
        if (Instance == this) Instance = null;
    }

    #endregion

    #region Connect, join and reconnect

    private async Task ConnectToServerAsync()
    {
        SetPhase(LobbyPhase.Connecting, "Connecting to the server...");
        try
        {
            Debug.Log("[Fusion][Runner] Using the attached NetworkRunner on DontDestroyOnLoad > Fusion Network Manager.", _runner);
            var result = await _runner.JoinSessionLobby(SessionLobby.Shared);
            if (!this) return;
            if (!result.Ok)
            {
                await FailAsync($"Connection failed: {result.ShutdownReason}.");
                return;
            }
            SetPhase(LobbyPhase.Ready, "Connected to the server.");
            Debug.Log("[Fusion][Connect] Shared lobby connection established. Joining automatically.", _runner);
            await JoinRoomAsync();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (this) await FailAsync("Server connection failed.");
        }
    }

    private async Task JoinRoomAsync()
    {
        if (Phase != LobbyPhase.Ready || !_runner || !_runner.IsCloudReady)
        {
            Debug.LogWarning("[Fusion][Room] Join ignored: wait until the server connection is ready.", this);
            return;
        }
        if (!PlayerPrefab || GameSceneBuildIndex == _lobbyIndex ||
            GameSceneBuildIndex < 0 || GameSceneBuildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            SetStatus("Assign the player prefab and a valid Game scene build index.");
            return;
        }
        SetPhase(LobbyPhase.Joining, $"Joining {SessionName}...");
        var runner = _runner;
        try
        {
            Debug.Log($"[Fusion][Room] Automatically joining Shared session '{SessionName}', capacity={MaxPlayers}.", this);
            // Shared mode lets another player become master if the current master leaves.
            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = SessionName,
                PlayerCount = Mathf.Clamp(MaxPlayers, 2, FusionLobbyState.PlayerSlotCapacity),
                Scene = SceneRef.FromIndex(_lobbyIndex),
                SceneManager = runner.GetComponent<NetworkSceneManagerDefault>()
            });
            if (!this || Phase == LobbyPhase.Leaving) return;
            if (!result.Ok)
            {
                string message = $"Could not join: {result.ShutdownReason}.";
                if (result.ShutdownReason == ShutdownReason.GameClosed)
                    message = "Game in progress. Waiting for the room to reopen.";
                else if (result.ShutdownReason == ShutdownReason.GameIsFull)
                    message = "Room is full. Waiting for a space.";
                await FailAsync(message);
                return;
            }
            SetPhase(LobbyPhase.InRoom, $"Joined {runner.SessionInfo.Name} as Player {runner.LocalPlayer.PlayerId}.");
            Debug.Log($"[Fusion][Room] {FusionPlayerSpawner.PeerLabel(runner)} Joined '{runner.SessionInfo.Name}' " +
                $"in region '{runner.SessionInfo.Region}'. Master={runner.GetMasterClient()}, capacity={runner.SessionInfo.MaxPlayers}.", runner);
            if (StatusText) StatusText.color = FusionNetworkPlayer.ColorForPlayer(runner.LocalPlayer);
            Debug.Log($"[Fusion][Color] {FusionPlayerSpawner.PeerLabel(runner)} Lobby color assigned: " +
                $"#{ColorUtility.ToHtmlStringRGB(FusionNetworkPlayer.ColorForPlayer(runner.LocalPlayer))}.", runner);
        }
        catch (Exception exception)
        {
            if (this && Phase != LobbyPhase.Leaving)
            {
                Debug.LogException(exception);
                await FailAsync("Could not join the room.");
            }
        }
    }

    private async void RestartConnection()
    {
        // This is for a failed/lost connection. Normal Game -> Lobby return keeps the runner.
        SetPhase(LobbyPhase.Leaving, "Reconnecting...");
        await ShutdownRunnerAsync();
        if (this) ReloadLobbyWithNewManager();
    }

    private async Task FailAsync(string message)
    {
        Debug.LogWarning($"[Fusion][Connect] {message}", this);
        SetPhase(LobbyPhase.Leaving, message);
        await ShutdownRunnerAsync();
        if (this) SetPhase(LobbyPhase.Failed, message + " Retrying automatically...");
    }

    private async Task ShutdownRunnerAsync()
    {
        var runner = _runner;
        _runner = null;
        if (!runner) return;
        // Keep the manager alive to display failure/retry status until the scene reloads.
        try { await runner.Shutdown(destroyGameObject: false); }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    private void ReloadLobbyWithNewManager()
    {
        // A stopped runner cannot be reused. The reloaded scene supplies a fresh manager and runner.
        if (Instance == this) Instance = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
        SceneManager.LoadScene(_lobbyIndex);
    }

    #endregion

    #region Lobby canvas and messages

    private void BindLobbyCanvas(FusionNetworkManager sceneManager)
    {
        // Old canvas/camera objects were destroyed when Lobby unloaded.
        WaitingRoomPanel = sceneManager.WaitingRoomPanel;
        StatusText = sceneManager.StatusText;
        PlayerCountText = sceneManager.PlayerCountText;
        CountdownText = sceneManager.CountdownText;
        LobbyCanvas = sceneManager.LobbyCanvas;
        LobbyCamera = sceneManager.LobbyCamera;
        MatchCameraHeight = sceneManager.MatchCameraHeight;
        CanvasHeightOffset = sceneManager.CanvasHeightOffset;
        _cameraWarningLogged = false;
        FindLobbyCanvas();
        if (_runner && _runner.IsRunning && _runner.IsInSession)
        {
            var spawner = GetComponent<FusionPlayerSpawner>();
            SessionName = _runner.SessionInfo.Name;
            MaxPlayers = _runner.SessionInfo.MaxPlayers;
            if (spawner.HasLobbySettings)
            {
                MinimumPlayers = spawner.MinimumPlayers;
                WaitingDelay = spawner.WaitingDelay;
                FullRoomDelay = spawner.FullRoomDelay;
                GameSceneBuildIndex = spawner.GameSceneBuildIndex;
                PlayerSpawnSpacing = spawner.PlayerSpawnSpacing;
            }
            SetPhase(LobbyPhase.InRoom, "Waiting for others to join...");
            if (StatusText) StatusText.color = FusionNetworkPlayer.ColorForPlayer(_runner.LocalPlayer);
            Debug.Log($"[Fusion][Lobby] {FusionPlayerSpawner.PeerLabel(_runner)} Reusing the existing manager, runner and room '{SessionName}'.", this);
        }
    }

    private void FindLobbyCanvas()
    {
        if (!LobbyCanvas)
        {
            Canvas canvas;
            if (WaitingRoomPanel)
                canvas = WaitingRoomPanel.GetComponentInParent<Canvas>();
            else
                canvas = GetComponent<Canvas>();
            if (canvas) LobbyCanvas = canvas.transform;
        }
    }

    private void LateUpdate()
    {
        // Follow the current camera's height after XR tracking has updated it.
        if (!MatchCameraHeight || SceneManager.GetActiveScene().buildIndex != _lobbyIndex) return;
        if (!LobbyCamera)
        {
            var origin = FusionPlayerSpawner.FindSceneOrigin();
            if (origin && origin.Camera)
                LobbyCamera = origin.Camera;
            else
                LobbyCamera = Camera.main;
        }
        if (!LobbyCanvas || !LobbyCamera)
        {
            if (!_cameraWarningLogged)
                Debug.LogWarning("[Fusion][Canvas] Assign Lobby Canvas and Lobby Camera to follow camera height.", this);
            _cameraWarningLogged = true;
            return;
        }
        _cameraWarningLogged = false;
        var position = LobbyCanvas.position;
        position.y = LobbyCamera.transform.position.y + CanvasHeightOffset;
        LobbyCanvas.position = position;
    }

    private void SetPhase(LobbyPhase phase, string message)
    {
        Phase = phase;
        if (phase == LobbyPhase.Failed) _retryAt = Time.unscaledTime + Mathf.Max(1f, ReconnectDelay);
        SetStatus(message);
        RefreshUI();
    }

    private void RefreshUI()
    {
        // Display Fusion's current player list and the shared countdown, never a local timer.
        if (WaitingRoomPanel) WaitingRoomPanel.SetActive(true);
        bool inRoom = _runner && _runner.IsRunning && _runner.IsInSession;
        int count = 0;
        if (inRoom) foreach (var player in _runner.ActivePlayers) count++;
        if (PlayerCountText)
        {
            PlayerCountText.gameObject.SetActive(true);
            int capacity = Mathf.Max(2, MaxPlayers);
            if (inRoom) capacity = _runner.SessionInfo.MaxPlayers;
            PlayerCountText.text = $"{count} / {capacity}";
        }
        var state = FusionLobbyState.Instance;
        if (CountdownText)
        {
            CountdownText.gameObject.SetActive(true);
            CountdownText.text = "Waiting...";
            if (inRoom && state && state.Object && state.Object.IsValid && state.Countdown.IsRunning)
                CountdownText.text = Mathf.CeilToInt(state.RemainingCountdownSeconds()).ToString();
        }
    }

    public void SetStatus(string message)
    {
        if (StatusText) StatusText.text = message;
        Debug.Log($"[Fusion][Lobby] {message}", this);
    }

    #endregion

    #region Voice and microphone

    private void ConfigureVoice()
    {
        _voice = GetComponent<FusionVoiceClient>();
        _recorder = GetComponent<Recorder>();

        // Permission and room membership must BOTH be ready before opening the microphone.
        // Record When Joined alone could start capture before permission is granted.
        _recorder.RecordWhenJoined = false;
        _recorder.RecordingEnabled = false;
        _voice.PrimaryRecorder = _recorder;
        _voice.AutoConnectAndJoin = !string.IsNullOrWhiteSpace(PhotonAppSettings.Global.AppSettings.AppIdVoice);
    }

    private void StartVoice()
    {
        if (!_voice.AutoConnectAndJoin)
        {
            Debug.LogWarning("[Voice] App Id Voice is empty in Photon App Settings. Gameplay can continue without voice.", this);
            return;
        }

        // Start runs after every component's Awake, so the Voice client now exists.
        _voice.Client.StateChanged += OnVoiceStateChanged;
        _voice.SpeakerLinked += OnSpeakerLinked;
        _networkEvents.OnShutdown.AddListener(OnVoiceRunnerShutdown);
        _voiceCallbacksBound = true;
        Debug.Log("[Voice] Ready. Voice will follow the Fusion room and survive scene/master changes.", this);

        StartCoroutine(RequestMicrophonePermission());
    }

    private IEnumerator RequestMicrophonePermission()
    {
        // Audio clips and generated audio do not need microphone permission.
        if (_recorder.SourceType != Recorder.InputSourceType.Microphone)
        {
            SetMicrophonePermission(true);
            yield break;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            SetMicrophonePermission(true);
        }
        else
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += OnMicrophoneGranted;
            callbacks.PermissionDenied += OnMicrophoneDenied;
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
        }
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        SetMicrophonePermission(Application.HasUserAuthorization(UserAuthorization.Microphone));
#else
        // Desktop microphone access is controlled by the operating system's privacy settings.
        SetMicrophonePermission(true);
#endif
        yield break;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void OnMicrophoneGranted(string permission) { SetMicrophonePermission(true); }
    private void OnMicrophoneDenied(string permission) { SetMicrophonePermission(false); }
#endif

    private void SetMicrophonePermission(bool allowed)
    {
        if (!this || _voiceStopping) return;
        _microphoneAllowed = allowed;
        if (allowed)
            Debug.Log("[Voice] Audio input permitted. Recording starts when the Voice room is joined.", this);
        else
            Debug.LogWarning("[Voice] Microphone permission denied. You can still hear others. Enable microphone access in device settings to speak.", this);
        UpdateVoiceRecording();
    }

    private void OnApplicationFocus(bool focused)
    {
        // Recheck when returning from device settings after granting or revoking access.
        if (!focused || !_voiceCallbacksBound || _voiceStopping || _recorder.SourceType != Recorder.InputSourceType.Microphone) return;
        bool allowed = _microphoneAllowed;
#if UNITY_ANDROID && !UNITY_EDITOR
        allowed = Permission.HasUserAuthorizedPermission(Permission.Microphone);
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        allowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
        if (allowed != _microphoneAllowed) SetMicrophonePermission(allowed);
    }

    private void OnVoiceStateChanged(ClientState previous, ClientState current)
    {
        Debug.Log($"[Voice] Connection: {previous} -> {current}.", this);
        if (current == ClientState.Joined)
            Debug.Log($"[Voice] Joined '{_voice.Client.CurrentRoom.Name}' in region '{_voice.Client.CurrentRegion}'.", this);
        if (current == ClientState.Disconnected)
            Debug.Log($"[Voice] Disconnected: {_voice.Client.DisconnectedCause}.", this);
        UpdateVoiceRecording();
    }

    private void UpdateVoiceRecording()
    {
        bool shouldRecord = _microphoneAllowed && _voice.Client.InRoom && !_voiceStopping;
        if (_recorder.RecordingEnabled == shouldRecord) return;
        _recorder.RecordingEnabled = shouldRecord;
        if (shouldRecord)
            Debug.Log("[Voice] Recording enabled. Speech is sent to everyone in this room.", this);
        else
            Debug.Log("[Voice] Recording stopped.", this);
    }

    private void OnSpeakerLinked(Speaker speaker)
    {
        // Voice actor numbers belong to Voice, and can differ from Fusion player IDs.
        Debug.Log($"[Voice] Remote audio linked for Voice actor {speaker.RemoteVoice.PlayerId}.", speaker);
    }

    private void OnVoiceRunnerShutdown(NetworkRunner runner, ShutdownReason reason)
    {
        _voiceStopping = true;
        _recorder.RecordingEnabled = false;
        _voice.AutoConnectAndJoin = false;
        if (_voice.Client.IsConnected) _voice.Disconnect();
        Debug.Log($"[Voice] Stopped with Fusion runner: {reason}.", this);
    }

    #endregion
}
