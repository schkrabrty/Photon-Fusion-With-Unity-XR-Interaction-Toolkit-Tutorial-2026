using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

[RequireComponent(typeof(NetworkRunner))]
[RequireComponent(typeof(NetworkEvents))]
// Spawns our local avatar, logs session events, and returns the last player to Lobby.
// SimulationBehaviour.Runner uses the runner attached to this same GameObject.
// It keeps the existing connection when changing scenes; FusionNetworkManager owns voice.
public class FusionPlayerSpawner : SimulationBehaviour, IPlayerJoined, IPlayerLeft, ISceneLoadDone
{
    public NetworkObject PlayerPrefab;
    public int LobbySceneBuildIndex = 0;
    public int GameSceneBuildIndex = 1;
    [Tooltip("Automatically finds this scene's XR Origin when the component is added, and refreshes after scene changes.")]
    public GameObject CameraRig;
    // Keep the master's room rules while the Lobby's network object is unloaded.
    public bool HasLobbySettings { get; private set; }
    public int MinimumPlayers { get; private set; }
    public float WaitingDelay { get; private set; }
    public float FullRoomDelay { get; private set; }
    public float PlayerSpawnSpacing { get; private set; }
    private int _spawnSlot = -1;
    private int _spawnPlayerCount;
    private XROrigin _positionedOrigin;
    private NetworkEvents _networkEvents;
    private PlayerRef _lastMaster = PlayerRef.None;
    private bool _allPlayersSpawnedLogged;
    private bool _spawnErrorLogged;
    private int _loadedSceneIndex = -1;
    private bool _returningToLobby;
    private float _nextReturnAttempt;

    [ContextMenu("Find XR Origin in Scene")]
    private void Reset()
    {
        // Unity calls Reset when this component is first attached in the Editor.
        // Find the component, so renaming or disabling the XR rig does not hide it.
        var scene = gameObject.scene;
        if (Application.isPlaying) scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // Make the menu assignment saveable and undoable in the scene.
        if (!Application.isPlaying) UnityEditor.Undo.RecordObject(this, "Assign XR Origin");
#endif
        CameraRig = null;
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects())
        {
            var origin = root.GetComponentInChildren<XROrigin>(true);
            if (!origin) continue;
            CameraRig = origin.gameObject;
            return;
        }
    }

    public void RememberLobbySettings(int minimumPlayers, float waitingDelay, float fullRoomDelay,
        int gameSceneIndex, float playerSpawnSpacing)
    {
        MinimumPlayers = minimumPlayers;
        WaitingDelay = waitingDelay;
        FullRoomDelay = fullRoomDelay;
        GameSceneBuildIndex = gameSceneIndex;
        PlayerSpawnSpacing = playerSpawnSpacing;
        HasLobbySettings = true;
    }

    public void RememberSpawnPosition(int slot, int playerCount)
    {
        _spawnSlot = slot;
        _spawnPlayerCount = playerCount;
    }

    public void LobbyReady()
    {
        _returningToLobby = false;
        if (!Runner.IsSceneAuthority) return;
        // Called after the new lobby NetworkObject is spawned and its countdown is reset.
        Runner.SessionInfo.IsOpen = true;
        Runner.SessionInfo.IsVisible = true;
        Debug.Log($"[Fusion][Room] {PeerLabel(Runner)} Lobby ready; '{Runner.SessionInfo.Name}' is open for partners.", this);
    }

    public static string PeerLabel(NetworkRunner runner)
    {
        if (!runner) return "[No runner]";
        if (!runner.IsRunning)
        {
            if (runner.IsCloudReady) return "[Cloud connected; not in room]";
            return "[Connecting/stopped]";
        }
        string role = "Shared client";
        if (runner.IsSharedModeMasterClient) role = "Shared master";
        return $"[Local={runner.LocalPlayer}, {role}]";
    }

    private void Awake()
    {
        // NetworkEvents is already attached and serialized in the Lobby scene.
        _networkEvents = GetComponent<NetworkEvents>();
    }

    private void OnEnable()
    {
        // Bind once when enabled. This component persists with the runner between scenes.
        _networkEvents.OnConnectedToServer.AddListener(OnConnectedToServer);
        _networkEvents.OnConnectFailed.AddListener(OnConnectFailed);
        _networkEvents.OnDisconnectedFromServer.AddListener(OnDisconnectedFromServer);
        _networkEvents.OnShutdown.AddListener(OnShutdown);
        _networkEvents.OnSceneLoadStart.AddListener(OnSceneLoadStart);
    }

    private void OnDisable()
    {
        if (!_networkEvents) return;
        _networkEvents.OnConnectedToServer.RemoveListener(OnConnectedToServer);
        _networkEvents.OnConnectFailed.RemoveListener(OnConnectFailed);
        _networkEvents.OnDisconnectedFromServer.RemoveListener(OnDisconnectedFromServer);
        _networkEvents.OnShutdown.RemoveListener(OnShutdown);
        _networkEvents.OnSceneLoadStart.RemoveListener(OnSceneLoadStart);
    }

    private void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log($"[Fusion][Connect] {PeerLabel(runner)} Connected to the server.", this);
    }

    private void OnConnectFailed(NetworkRunner runner, NetAddress address, NetConnectFailedReason reason)
    {
        Debug.LogError($"[Fusion][Connect] {PeerLabel(runner)} Connection failed: {reason}.", this);
    }

    private void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[Fusion][Connect] {PeerLabel(runner)} Disconnected: {reason}.", this);
    }

    private void OnShutdown(NetworkRunner runner, ShutdownReason reason)
    {
        Debug.Log($"[Fusion][Runner] {PeerLabel(runner)} Shutdown: {reason}.", this);
    }

    private void OnSceneLoadStart(NetworkRunner runner)
    {
        _loadedSceneIndex = -1;
        _allPlayersSpawnedLogged = false;
        Debug.Log($"[Fusion][Scene] {PeerLabel(runner)} Local scene loading started.", this);
    }

    public void PlayerJoined(PlayerRef player)
    {
        // Fusion calls this for each player. Only this device spawns its own avatar.
        _allPlayersSpawnedLogged = false;
        string playerType = "remote";
        if (player == Runner.LocalPlayer) playerType = "local";
        Debug.Log($"[Fusion][Room] {PeerLabel(Runner)} Player joined: {player} " +
            $"({playerType}). Master={Runner.GetMasterClient()}.", this);
        if (player == Runner.LocalPlayer)
            ShowLobbyMessage($"Joined {Runner.SessionInfo.Name} as Player {player.PlayerId}.");
        else
            ShowLobbyMessage($"Player {player.PlayerId} has joined the room.");
        if (player == Runner.LocalPlayer) TrySpawn();
    }

    public void PlayerLeft(PlayerRef player)
    {
        _allPlayersSpawnedLogged = false;
        Debug.Log($"[Fusion][Room] {PeerLabel(Runner)} Player left: {player}.", this);
        ShowLobbyMessage($"Player {player.PlayerId} has left the room. Waiting for others to join...");
    }

    private void ShowLobbyMessage(string message)
    {
        if (SceneManager.GetActiveScene().buildIndex != LobbySceneBuildIndex) return;
        var manager = FusionNetworkManager.Instance;
        if (manager && manager.Runner == Runner) manager.SetStatus(message);
    }

    public void SceneLoadDone(in SceneLoadDoneArgs args)
    {
        // Scene objects cannot be reused after unloading, so find the new XR Origin.
        _loadedSceneIndex = args.Scene.buildIndex;
        CameraRig = null; // The XR rig belongs to the newly loaded scene.
        var origin = FindSceneOrigin();
        if (origin)
        {
            CameraRig = origin.gameObject;
            Debug.Log($"[Fusion][XR] Using XR Origin '{origin.name}' in '{args.Scene.name}'.", origin);
        }
        Debug.Log($"[Fusion][Scene] {PeerLabel(Runner)} Local scene load completed: '{args.Scene.name}' " +
            $"(build index {args.Scene.buildIndex}).", this);
        if (args.SceneRef == SceneRef.FromIndex(GameSceneBuildIndex)) TrySpawn();
    }

    public static XROrigin FindSceneOrigin()
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var origin in FindObjectsByType<XROrigin>(FindObjectsSortMode.None))
        {
            if (origin.gameObject.scene == scene) return origin;
        }
        return null;
    }

    private void TrySpawn()
    {
        // Wait for Game and its XR rig. SetPlayerObject prevents duplicate avatars.
        if (!Runner.IsRunning || Runner.IsSceneManagerBusy || _returningToLobby ||
            _loadedSceneIndex != GameSceneBuildIndex ||
            SceneManager.GetActiveScene().buildIndex != GameSceneBuildIndex ||
            Runner.LocalPlayer == PlayerRef.None || Runner.GetPlayerObject(Runner.LocalPlayer)) return;
        if (!PlayerPrefab)
        {
            if (!_spawnErrorLogged) Debug.LogError("[Fusion][Spawn] Assign Network Player to the lobby manager.", this);
            _spawnErrorLogged = true;
            return;
        }
        var origin = FindSceneOrigin();
        if (!origin || !origin.Camera) return; // Update retries while the new scene's XR rig initializes.
        if (_spawnSlot < 0) return; // Wait for our assigned position from the shared lobby state.
        CameraRig = origin.gameObject;
        PlaceRigAtSpawn(origin);
        var obj = Runner.Spawn(PlayerPrefab, origin.transform.position, origin.transform.rotation, Runner.LocalPlayer);
        Runner.SetPlayerObject(Runner.LocalPlayer, obj);
        Debug.Log($"[Fusion][Spawn] {PeerLabel(Runner)} Spawned and registered local avatar '{obj.name}', " +
            $"object={obj.Id}, state authority={obj.StateAuthority}, scene='{obj.gameObject.scene.name}'.", obj);
    }

    private void PlaceRigAtSpawn(XROrigin origin)
    {
        // Move once per scene's rig, even if spawning needs another attempt.
        if (_positionedOrigin == origin) return;
        var rig = origin.Origin.transform;
        Vector3 right = Vector3.ProjectOnPlane(rig.right, Vector3.up).normalized;
        float offset = (_spawnSlot - (_spawnPlayerCount - 1) * 0.5f) * PlayerSpawnSpacing;
        Vector3 position = rig.position + right * offset;
        position.y = origin.Camera.transform.position.y;

        // Move the whole XR rig so camera, hands and avatar agree. Preserve headset height.
        // Target the camera's X/Z to account for where the person stands in their play area.
        origin.MoveCameraToWorldLocation(position);
        _positionedOrigin = origin;
        Debug.Log($"[Fusion][Spawn] {PeerLabel(Runner)} Starting position {_spawnSlot + 1}/{_spawnPlayerCount}, " +
            $"spacing={PlayerSpawnSpacing:0.##}m, camera position={position}.", this);
    }

    // Also covers scene callbacks that arrive before the scene manager clears IsBusy.
    private void Update()
    {
        if (!Runner || !Runner.IsRunning) return;
        var master = Runner.GetMasterClient();
        if (master.IsRealPlayer && master != _lastMaster)
        {
            _lastMaster = master;
            Debug.Log($"[Fusion][Master] {PeerLabel(Runner)} Current Shared master: {master}.", this);
        }
        // Poll as well as handling PlayerLeft: the departing peer may have been the master,
        // so the new scene authority can be assigned after the departure callback.
        if (TryReturnToLobby()) return;
        TrySpawn();
        if (_allPlayersSpawnedLogged || Runner.IsSceneManagerBusy ||
            SceneManager.GetActiveScene().buildIndex != GameSceneBuildIndex) return;
        int count = 0;
        foreach (var player in Runner.ActivePlayers)
        {
            // Each player only registers their avatar after loading the Game scene.
            var avatar = Runner.GetPlayerObject(player);
            if (!avatar || avatar.gameObject.scene.buildIndex != GameSceneBuildIndex) return;
            count++;
        }
        if (count == 0) return;
        _allPlayersSpawnedLogged = true;
        Debug.Log($"[Fusion][Scene] {PeerLabel(Runner)} All {count} currently connected players have spawned in Game. " +
            "Their registered avatars are visible to this peer.", this);
    }

    public static bool ShouldReturnToLobby(int playerCount, bool gameReady, bool isSceneAuthority, bool returning)
    {
        return playerCount == 1 && gameReady && isSceneAuthority && !returning;
    }

    private bool TryReturnToLobby()
    {
        int count = 0;
        foreach (var player in Runner.ActivePlayers) count++;
        bool gameReady = _loadedSceneIndex == GameSceneBuildIndex && !Runner.IsSceneManagerBusy &&
            SceneManager.GetActiveScene().buildIndex == GameSceneBuildIndex;
        if (!ShouldReturnToLobby(count, gameReady, Runner.IsSceneAuthority, _returningToLobby) ||
            Time.unscaledTime < _nextReturnAttempt) return _returningToLobby;

        _returningToLobby = true;
        // Keep the room closed until the lobby state has been registered again.
        Runner.SessionInfo.IsOpen = false;
        Runner.SessionInfo.IsVisible = false;
        var avatar = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (avatar && avatar.HasStateAuthority) Runner.Despawn(avatar);
        Debug.Log($"[Fusion][Room] {PeerLabel(Runner)} One player remains. Returning to Lobby in the same room.", this);
        try
        {
            Runner.LoadScene(SceneRef.FromIndex(LobbySceneBuildIndex), LoadSceneMode.Single).AddOnCompleted(op =>
            {
                if (op.Error != null) ReturnFailed(op.Error);
            });
        }
        catch (System.Exception exception) { ReturnFailed(exception); }
        return true;
    }

    private void ReturnFailed(System.Exception exception)
    {
        Debug.LogError($"[Fusion][Scene] Return to Lobby failed: {exception.Message}. Retrying in two seconds.", this);
        _returningToLobby = false;
        _loadedSceneIndex = SceneManager.GetActiveScene().buildIndex;
        _nextReturnAttempt = Time.unscaledTime + 2f;
    }
}
