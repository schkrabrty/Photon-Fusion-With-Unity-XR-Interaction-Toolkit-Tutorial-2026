using System.Collections;
using System.Threading.Tasks;
using Fusion;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using PhotonAppSettings = Fusion.Photon.Realtime.PhotonAppSettings;

// Connects this client to a room and sets up the persistent controller, UI, and voice.
[RequireComponent(typeof(NetworkRunner), typeof(NetworkEvents), typeof(NetworkSceneManagerDefault))]
[RequireComponent(typeof(Recorder), typeof(FusionVoiceClient), typeof(VoiceLogger))]
public class FusionNetworkManager : MonoBehaviour
{
    // A local shortcut to the surviving manager; this static reference is not networked.
    public static FusionNetworkManager Instance;
    public string SessionName = "Room 1";
    [Range(2, 20)] public int MaxPlayers = 2;
    public NetworkObject PlayerSpawnerPrefab;
    [Min(1f)] public float ReconnectDelay = 5f;
    public TMP_Text StatusText, PlayerCountText, CountdownText;
    public Transform LobbyCanvas;
    public Camera LobbyCamera;
    [Min(0.1f)] public float LobbyCanvasDistance = 1f;
    [UnityEngine.Serialization.FormerlySerializedAs("LobbyCanvasHeight")]
    [Min(0f)] public float LobbyCanvasHeightAboveGround = 1f;
    public string Status { get; private set; } = "Connecting...";
    private NetworkRunner runner;
    private Recorder recorder;
    private FusionVoiceClient voice;
    private bool microphoneAllowed;
    private bool reconnecting;
    private bool lobbyCanvasPlaced;
    private int lobbySceneIndex;

    // Unity calls Awake before Start: prepare the local connection, UI, and voice components.
    private void Awake()
    {
        if (PlayerCountText) PlayerCountText.text = $"0 / {MaxPlayers}";
        if (CountdownText) CountdownText.text = "Waiting...";
        if (Instance && Instance != this)
        {
            // Returning to Lobby creates fresh UI. Keep the running manager,
            // but give it this scene's Inspector references before removing the duplicate.
            Instance.StatusText = StatusText;
            Instance.PlayerCountText = PlayerCountText;
            Instance.CountdownText = CountdownText;
            Instance.LobbyCanvas = LobbyCanvas;
            Instance.LobbyCamera = LobbyCamera;
            Instance.LobbyCanvasDistance = LobbyCanvasDistance;
            Instance.LobbyCanvasHeightAboveGround = LobbyCanvasHeightAboveGround;
            Instance.lobbyCanvasPlaced = false; // Place the newly loaded lobby panel once.
            Instance.SetStatus(Instance.Status);
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        lobbySceneIndex = gameObject.scene.buildIndex;
        // Keep this client's connection and voice components alive when Lobby becomes Game.
        DontDestroyOnLoad(gameObject);
        runner = GetComponent<NetworkRunner>();
        recorder = GetComponent<Recorder>();
        voice = GetComponent<FusionVoiceClient>();
        // Tell Photon Voice which microphone recorder supplies this client's outgoing audio.
        voice.PrimaryRecorder = recorder;
        // Voice is optional: no Voice App ID means no microphone request or voice connection.
        voice.AutoConnectAndJoin = !string.IsNullOrWhiteSpace(PhotonAppSettings.Global.AppSettings.AppIdVoice);
        // LateUpdate enables recording only when permission and both connections are ready.
        recorder.RecordWhenJoined = false;
        recorder.RecordingEnabled = false;
        if (voice.AutoConnectAndJoin) StartCoroutine(RequestMicrophonePermission());
    }

    // Subscribe to Fusion events, join the room, then let the master spawn the shared controller once.
    private async void Start()
    {
        SetStatus(Status);
        // NetworkEvents raises these callbacks when something happens in the room.
        // Each lambda (r => ...) is the small action to run when that event fires.
        var events = GetComponent<NetworkEvents>();
        events.OnConnectedToServer.AddListener(r => SetStatus("Connected. Joining room..."));
        events.PlayerJoined.AddListener((r, player) => SetStatus($"Player {player.PlayerId} joined. Waiting for others..."));
        events.PlayerLeft.AddListener((r, player) => SetStatus($"Player {player.PlayerId} left."));
        events.OnSceneLoadDone.AddListener(r =>
        {
            if (SceneManager.GetActiveScene().buildIndex == lobbySceneIndex)
                SetStatus("Waiting for others to join...");
        });
        events.OnConnectFailed.AddListener((r, address, reason) => Reconnect($"Connection failed: {reason}."));
        events.OnDisconnectedFromServer.AddListener((r, reason) => Reconnect($"Disconnected: {reason}."));
        events.OnShutdown.AddListener((r, reason) => Reconnect($"Runner stopped: {reason}."));
        if (!PlayerSpawnerPrefab)
        {
            SetStatus("Assign the Fusion Player Spawner and Lobby Controller prefab.");
            return;
        }
        // Shared mode lets each client control its own avatar. The room name selects the session;
        // PlayerCount sets its capacity, and the scene manager synchronizes scene loading.
        var startGameArgs = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = SessionName,
            PlayerCount = MaxPlayers,
            Scene = SceneRef.FromIndex(lobbySceneIndex),
            SceneManager = GetComponent<NetworkSceneManagerDefault>()
        };
        try
        {
            // Join/create the Fusion room. StartGame does not spawn a player avatar.
            var result = await runner.StartGame(startGameArgs);
            // After an await, this component may have been destroyed or a retry may have started.
            if (!this || reconnecting) return;
            if (result.Ok)
            {
                // Joining may finish before the initial scene is ready. Yield without freezing Unity.
                while (this && runner.IsSceneManagerBusy) await Task.Yield();
                if (!this || reconnecting) return;
                SetStatus("Waiting for others to join...");
                // Spawn ONE shared controller, not a player avatar. Fusion sends a copy to every client.
                // The flags keep it across scenes and transfer authority if the master leaves.
                // The controller separately spawns each client's PlayerPrefab.
                if (runner.IsSharedModeMasterClient && !FusionPlayerSpawnerAndLobbyController.Instance)
                    runner.Spawn(PlayerSpawnerPrefab, flags: NetworkSpawnFlags.DontDestroyOnLoad |
                        NetworkSpawnFlags.SharedModeStateAuthMasterClient);
            }
            else Reconnect($"Could not join: {result.ShutdownReason}.");
        }
        catch (System.Exception error) { if (this) Reconnect(error.Message); }
    }

    // Keep the latest connection message, show it when the lobby UI exists, and log it for debugging.
    public void SetStatus(string message)
    {
        Status = message;
        if (StatusText) StatusText.text = message;
        Debug.Log(message, this);
    }

    // Refresh voice each frame, and place each new lobby panel once after the camera updates.
    private void LateUpdate()
    {
        if (!runner) return;
        // Recording begins only after permission and both the game and voice connections are ready.
        recorder.RecordingEnabled = !reconnecting && microphoneAllowed && runner.IsRunning && voice.Client.InRoom;
        // Game has no lobby panel. Unity object checks also detect references destroyed by a scene change.
        if (!LobbyCanvas || !LobbyCamera || lobbyCanvasPlaced) return;
        // Use only the headset's left/right heading; looking up or down must not move the panel vertically.
        // Place it once, then let CanvasController turn it toward the camera without moving it.
        var cameraTransform = LobbyCamera.transform;
        var heading = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);
        var position = cameraTransform.position + heading * Vector3.forward * LobbyCanvasDistance;
        position.y = LobbyCanvasHeightAboveGround; // Height above this scene's floor at world y = 0.
        LobbyCanvas.SetPositionAndRotation(position, heading);
        lobbyCanvasPlaced = true;
    }

    // Recover from a connection failure by stopping this runner and reloading Lobby for a fresh attempt.
    public async void Reconnect(string message)
    {
        if (reconnecting) return; // Several callbacks can report the same failure.
        reconnecting = true;
        SetStatus(message + " Retrying automatically...");
        recorder.RecordingEnabled = false;
        voice.AutoConnectAndJoin = false;
        if (voice.Client.IsConnected) voice.Disconnect();
        // Task.Delay uses milliseconds and lets Unity keep running during the retry delay.
        await Task.Delay(Mathf.CeilToInt(ReconnectDelay * 1000));
        if (!this) return;
        // Finish network cleanup first; we explicitly destroy the manager GameObject below.
        try { await runner.Shutdown(destroyGameObject: false); }
        catch (System.Exception error) { Debug.LogException(error); }
        if (!this) return;
        // Fusion runners are single-use. Reload Lobby to create a fresh one.
        Instance = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
        SceneManager.LoadScene(lobbySceneIndex);
    }

    // Unity calls this when the component becomes inactive: stop recording and disconnect voice.
    private void OnDisable()
    {
        if (!recorder || !voice) return;
        recorder.RecordingEnabled = false;
        voice.AutoConnectAndJoin = false;
        if (voice.Client.IsConnected) voice.Disconnect();
    }

    // Ask the operating system for microphone access. This coroutine can wait for the user
    // without blocking the game; #if selects the code compiled for the current platform.
    private IEnumerator RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
        if (!microphoneAllowed)
        {
            // Android reports approval later through this callback. Denial leaves recording disabled.
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += permission => { if (this) microphoneAllowed = true; };
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
        }
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // On Apple platforms, wait for the permission request to finish, then read the result.
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
        // Other platforms use their normal microphone access; this script makes no permission request.
        microphoneAllowed = true;
#endif
        yield break;
    }

    // Recheck permission when the user returns, for example after changing it in device settings.
    private void OnApplicationFocus(bool focused)
    {
        if (!focused || !recorder) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
    }

    // Clear the shortcut only if THIS manager owns it; a discarded duplicate must not clear it.
    private void OnDestroy() { if (Instance == this) Instance = null; }
}
