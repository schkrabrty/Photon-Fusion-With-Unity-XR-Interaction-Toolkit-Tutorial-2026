using UnityEngine;
using Fusion;
using Photon.Voice.Unity;
using Photon.Voice.Fusion;
using System.Collections;
using TMPro;
using UnityEngine.SceneManagement;
using PhotonAppSettings = Fusion.Photon.Realtime.PhotonAppSettings;

[RequireComponent(typeof(NetworkRunner))]
[RequireComponent(typeof(NetworkEvents))]
[RequireComponent(typeof(NetworkSceneManagerDefault))]
[RequireComponent(typeof(Recorder))]
[RequireComponent(typeof(FusionVoiceClient))]
[RequireComponent(typeof(FusionPlayerSpawner))]
[RequireComponent(typeof(VoiceLogger))]
public class FusionNetworkManager : MonoBehaviour
{
    public static FusionNetworkManager Instance;
    public string SessionName = "Room 1";
    [Range(2, 256)] public int MaxPlayers = 2;
    public int MinimumPlayers = 2;
    public float WaitingDelay = 30f, FullRoomDelay = 5f;
    public int GameSceneBuildIndex = 1;
    public NetworkObject PlayerPrefab;
    public float ReconnectDelay = 5f;
    public float PlayerSpawnSpacing = 0.7f;
    public GameObject WaitingRoomPanel;
    public TMP_Text StatusText, PlayerCountText, CountdownText;
    public bool MatchCameraHeight = true;
    public Transform LobbyCanvas;
    public Camera LobbyCamera;
    public float CanvasHeightOffset;

    private NetworkRunner _runner;
    private Recorder recorder;
    private FusionVoiceClient voice;
    private bool microphoneAllowed;
    private bool joinedRoom, reconnecting;
    private int lobbySceneIndex;

    private void Awake()
    {
        if (Instance && Instance != this)
        {
            // Keep the connection, but use the returning scene's new canvas.
            Instance.StatusText = StatusText;
            Instance.PlayerCountText = PlayerCountText;
            Instance.CountdownText = CountdownText;
            Instance.WaitingRoomPanel = WaitingRoomPanel;
            Instance.LobbyCanvas = LobbyCanvas;
            Instance.LobbyCamera = LobbyCamera;
            Instance.MatchCameraHeight = MatchCameraHeight;
            Instance.CanvasHeightOffset = CanvasHeightOffset;
            Instance.SetStatus("Waiting for others to join...");
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        lobbySceneIndex = gameObject.scene.buildIndex;
        DontDestroyOnLoad(gameObject);
        recorder = GetComponent<Recorder>();
        voice = GetComponent<FusionVoiceClient>();
        voice.AutoConnectAndJoin = !string.IsNullOrWhiteSpace(PhotonAppSettings.Global.AppSettings.AppIdVoice);
        recorder.RecordWhenJoined = false;
        recorder.RecordingEnabled = false;
    }

    private async void Start()
    {
        _runner = gameObject.GetComponent<NetworkRunner>();
        voice.PrimaryRecorder = recorder;
        GetComponent<FusionPlayerSpawner>().PlayerPrefab = PlayerPrefab;
        GetComponent<FusionPlayerSpawner>().GameSceneBuildIndex = GameSceneBuildIndex;
        GetComponent<FusionPlayerSpawner>().LobbySceneBuildIndex = lobbySceneIndex;
        if (voice.AutoConnectAndJoin) StartCoroutine(RequestMicrophonePermission());
        if (StatusText) StatusText.text = "Connecting...";

        if (!PlayerPrefab || GameSceneBuildIndex == lobbySceneIndex || GameSceneBuildIndex < 0 ||
            GameSceneBuildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            SetStatus("Assign the player prefab and a valid Game scene build index.");
            return;
        }
        var startGameArgs = new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = SessionName,
            PlayerCount = Mathf.Clamp(MaxPlayers, 2, 256),
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.GetComponent<NetworkSceneManagerDefault>()
        };

        try
        {
            var result = await _runner.StartGame(startGameArgs);
            if (!this) return;
            joinedRoom = result.Ok;
            if (result.Ok) SetStatus("Waiting for others to join...");
            else RetryConnection($"Could not join: {result.ShutdownReason}.");
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            if (this) RetryConnection("Connection failed.");
        }
    }

    private void Update()
    {
        bool inRoom = _runner && _runner.IsRunning;
        if (joinedRoom && !inRoom) RetryConnection("Connection lost.");
        recorder.RecordingEnabled = microphoneAllowed && inRoom && !reconnecting && voice.Client.InRoom;
        if (WaitingRoomPanel) WaitingRoomPanel.SetActive(true);
        int count = 0;
        if (inRoom) foreach (var player in _runner.ActivePlayers) count++;
        if (PlayerCountText) PlayerCountText.text = $"{count} / {(inRoom ? _runner.SessionInfo.MaxPlayers : MaxPlayers)}";
        var lobby = FusionLobbyState.Instance;
        if (CountdownText)
        {
            CountdownText.text = "Waiting...";
            if (inRoom && lobby && lobby.Object && lobby.Object.IsValid && lobby.Countdown.IsRunning)
                CountdownText.text = Mathf.CeilToInt(lobby.RemainingCountdownSeconds()).ToString();
        }
    }

    public void SetStatus(string message)
    {
        if (StatusText)
        {
            StatusText.text = message;
            if (_runner && _runner.IsRunning) StatusText.color = FusionNetworkPlayer.ColorForPlayer(_runner.LocalPlayer);
        }
        Debug.Log(message, this);
    }

    private void RetryConnection(string message)
    {
        joinedRoom = false;
        SetStatus(message + " Retrying automatically...");
        Invoke(nameof(RestartConnection), Mathf.Max(1f, ReconnectDelay));
    }

    private async void RestartConnection()
    {
        if (reconnecting) return;
        reconnecting = true;
        recorder.RecordingEnabled = false;
        voice.AutoConnectAndJoin = false;
        if (voice.Client.IsConnected) voice.Disconnect();
        try { await _runner.Shutdown(destroyGameObject: false); }
        catch (System.Exception exception) { Debug.LogException(exception); }
        if (!this) return;
        // A stopped runner cannot be reused. Reload the Lobby's original manager.
        Instance = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
        SceneManager.LoadScene(lobbySceneIndex);
    }

    private void LateUpdate()
    {
        if (!MatchCameraHeight || !LobbyCanvas || !LobbyCamera) return;
        var position = LobbyCanvas.position;
        position.y = LobbyCamera.transform.position.y + CanvasHeightOffset;
        LobbyCanvas.position = position;
    }

    private IEnumerator RequestMicrophonePermission()
    {
        if (recorder.SourceType != Recorder.InputSourceType.Microphone)
        {
            microphoneAllowed = true;
            yield break;
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
        if (!microphoneAllowed)
        {
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += permission => { if (this) microphoneAllowed = true; };
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
        }
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
        microphoneAllowed = true;
#endif
        yield break;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused || !recorder || recorder.SourceType != Recorder.InputSourceType.Microphone) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
