using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkRunner), typeof(NetworkEvents), typeof(NetworkSceneManagerDefault))]
[RequireComponent(typeof(FusionPlayerSpawner), typeof(FusionVoiceSetup))]
public class FusionNetworkManager : MonoBehaviour
{
    public static FusionNetworkManager Instance;
    public string SessionName = "Room 1";
    [Range(2, 20)] public int MaxPlayers = 2;
    public int MinimumPlayers = 2;
    public float WaitingDelay = 30f, FullRoomDelay = 5f;
    public int GameSceneBuildIndex = 1;
    public NetworkObject PlayerPrefab;
    public float ReconnectDelay = 5f, PlayerSpawnSpacing = 0.7f;
    public string Status { get; private set; } = "Connecting...";
    private NetworkRunner runner;
    private bool reconnecting;
    private int lobbySceneIndex;

    private void Awake()
    {
        if (Instance && Instance != this)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        lobbySceneIndex = gameObject.scene.buildIndex;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        runner = GetComponent<NetworkRunner>();
        // Direct Fusion events: no connection polling or manual callback forwarding.
        var events = GetComponent<NetworkEvents>();
        events.OnConnectedToServer.AddListener(r => SetStatus("Connected. Joining room..."));
        events.PlayerJoined.AddListener((r, player) => SetStatus($"Player {player.PlayerId} joined. Waiting for others..."));
        events.PlayerLeft.AddListener((r, player) => SetStatus($"Player {player.PlayerId} left."));
        events.OnConnectFailed.AddListener((r, address, reason) => Reconnect($"Connection failed: {reason}."));
        events.OnDisconnectedFromServer.AddListener((r, reason) => Reconnect($"Disconnected: {reason}."));
        events.OnShutdown.AddListener((r, reason) => Reconnect($"Runner stopped: {reason}."));
        var spawner = GetComponent<FusionPlayerSpawner>();
        spawner.PlayerPrefab = PlayerPrefab;
        spawner.GameSceneBuildIndex = GameSceneBuildIndex;
        spawner.LobbySceneBuildIndex = lobbySceneIndex;
        if (!PlayerPrefab || GameSceneBuildIndex == lobbySceneIndex || GameSceneBuildIndex < 0 ||
            GameSceneBuildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            SetStatus("Assign the player prefab and a valid Game scene build index.");
            return;
        }
        var startGameArgs = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = SessionName,
            PlayerCount = Mathf.Clamp(MaxPlayers, 2, 20),
            Scene = SceneRef.FromIndex(lobbySceneIndex),
            SceneManager = GetComponent<NetworkSceneManagerDefault>()
        };
        try
        {
            var result = await runner.StartGame(startGameArgs);
            if (!this || reconnecting) return;
            if (result.Ok) SetStatus("Waiting for others to join...");
            else Reconnect($"Could not join: {result.ShutdownReason}.");
        }
        catch (System.Exception error) { if (this) Reconnect(error.Message); }
    }

    public void SetStatus(string message) { Status = message; Debug.Log(message, this); }

    public async void Reconnect(string message)
    {
        if (reconnecting) return; // Several callbacks can report the same failure.
        reconnecting = true;
        SetStatus(message + " Retrying automatically...");
        GetComponent<FusionVoiceSetup>().enabled = false;
        await Task.Delay(Mathf.CeilToInt(Mathf.Max(1f, ReconnectDelay) * 1000));
        if (!this) return;
        try { await runner.Shutdown(destroyGameObject: false); }
        catch (System.Exception error) { Debug.LogException(error); }
        if (!this) return;
        // Fusion runners are single-use. Reload Lobby to create a fresh one.
        Instance = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
        SceneManager.LoadScene(lobbySceneIndex);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
}
