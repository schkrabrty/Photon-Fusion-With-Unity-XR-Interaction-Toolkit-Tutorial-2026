using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

// One shared network prefab survives scene changes and coordinates the lobby and player avatars.
[RequireComponent(typeof(NetworkObject))]
public class FusionPlayerSpawnerAndLobbyController : NetworkBehaviour, IStateAuthorityChanged
{
    // Access this client's copy after Spawned; Instance is null before the network object arrives.
    public static FusionPlayerSpawnerAndLobbyController Instance;
    public NetworkObject PlayerPrefab;
    public int LobbySceneBuildIndex = 0, GameSceneBuildIndex = 1;
    [Min(2)] public int MinimumPlayers = 2;
    [Min(0.1f)] public float WaitingDelay = 30f, FullRoomDelay = 5f;
    public float PlayerSpawnSpacing = 0.7f;
    // Fusion replicates this deadline to clients; compare it with Runner.SimulationTime, not local wall time.
    [Networked] public float CountdownEndTime { get; set; } // Zero means waiting.
    // Player IDs, not avatar objects: each array index is a numbered spawn position.
    // NetworkArray has fixed slots, so use Set(index, player), not List.Add(player).
    // PlayerRef.None marks an empty slot; Capacity(20) supports rooms up to 20 players.
    // When updated slots reach another client, it can try spawning its local avatar.
    [Networked, Capacity(20), OnChangedRender(nameof(SpawnPlayer))]
    public NetworkArray<PlayerRef> SpawnPlayers => default;
    private NetworkEvents events;
    private bool loadingScene;

    // Fusion calls this on every client when the shared coordinator becomes available.
    public override async void Spawned()
    {
        Instance = this;
        events = Runner.GetComponent<NetworkEvents>();
        events.PlayerJoined.AddListener(PlayersChanged);
        events.PlayerLeft.AddListener(PlayersChanged);
        events.OnSceneLoadStart.AddListener(SceneLoading);
        events.OnSceneLoadDone.AddListener(SceneLoaded);
        // The master's scene is ready when it spawns this controller, but a joining client's
        // scene may still be loading. Wait here too, so the first avatar attempt is not lost.
        while (this && Runner.IsSceneManagerBusy) await Task.Yield();
        if (!this || !Runner.IsRunning) return;
        // Include players whose join events happened before this controller arrived.
        PlayersChanged(Runner, Runner.LocalPlayer);
    }

    // Both join and leave events use this method. Rebuilding from ActivePlayers also
    // handles initial setup and a new master taking over; the event's player is not needed.
    private void PlayersChanged(NetworkRunner runner, PlayerRef player)
    {
        // Only the coordinator's state authority (the master) edits the shared slots.
        if (HasStateAuthority)
        {
            // First: is this slot's player still in the room? If not, empty the slot.
            // Leave everyone else's index unchanged so their spawn positions stay stable.
            for (int i = 0; i < SpawnPlayers.Length; i++)
                if (!runner.ActivePlayers.Contains(SpawnPlayers[i])) SpawnPlayers.Set(i, PlayerRef.None);
            // Second: does this active player already have a slot? If not, assign one.
            // These Contains calls ask different questions: who left, and who needs a slot?
            foreach (var joinedPlayer in runner.ActivePlayers)
                if (!SpawnPlayers.Contains(joinedPlayer))
                    SpawnPlayers.Set(SpawnPlayers.IndexOf(PlayerRef.None), joinedPlayer);
        }
        // With only one player left in Game, the scene authority returns the room to Lobby.
        if (runner.IsSceneAuthority && !loadingScene && !runner.IsSceneManagerBusy &&
            SceneManager.GetActiveScene().buildIndex == GameSceneBuildIndex && runner.ActivePlayers.Count() == 1)
        {
            loadingScene = true;
            runner.LoadScene(SceneRef.FromIndex(LobbySceneBuildIndex), LoadSceneMode.Single);
            return;
        }
        SpawnPlayer();
    }

    // If the master leaves, the new master also checks whether to return to Lobby.
    public void StateAuthorityChanged() => PlayersChanged(Runner, Runner.LocalPlayer);

    // Before leaving a scene, each client removes its own avatar; the shared controller stays alive.
    // The avatar follows this scene's XR rig; the next scene has a new rig to bind to.
    private void SceneLoading(NetworkRunner runner)
    {
        loadingScene = true; // Prevent spawning while the old scene is being replaced.
        var avatar = runner.GetPlayerObject(runner.LocalPlayer);
        // There may be no avatar yet during the initial scene load.
        // Despawn removes a network object for everyone; it does not disconnect the player.
        if (avatar) runner.Despawn(avatar);
    }

    // After loading, prepare a fresh lobby if needed and spawn our avatar using the new scene's rig.
    private async void SceneLoaded(NetworkRunner runner)
    {
        // Fusion raises this event just before its scene manager finishes loading.
        // Yield lets that work finish without blocking Unity, before we use the new XR rig.
        while (this && runner.IsSceneManagerBusy) await Task.Yield();
        if (!this || !runner.IsRunning) return; // We may have disconnected while waiting.
        loadingScene = false;
        if (HasStateAuthority && SceneManager.GetActiveScene().buildIndex == LobbySceneBuildIndex)
        {
            // Returning to Lobby starts a fresh wait, frees the old slots, and allows joins.
            CountdownEndTime = 0;
            SpawnPlayers.Clear();
            runner.SessionInfo.IsOpen = runner.SessionInfo.IsVisible = true;
        }
        // Refresh the slots and create our avatar in the new scene.
        // Other clients may receive their slot later; OnChangedRender retries for them.
        PlayersChanged(runner, runner.LocalPlayer);
    }

    // Create at most one avatar for the local player once its assigned slot and scene are ready.
    private void SpawnPlayer()
    {
        // IndexOf returns -1 if the master's slot assignment has not reached this client yet.
        int slot = SpawnPlayers.IndexOf(Runner.LocalPlayer);
        // Wait for our slot and scene, and never create a second local avatar.
        if (slot < 0 || loadingScene || Runner.IsSceneManagerBusy || Runner.GetPlayerObject(Runner.LocalPlayer)) return;
        var rig = GameObject.Find("XR Origin (XR Rig)").GetComponent<XROrigin>();
        // Spread numbered slots across the rig's right direction, centered on its starting point.
        float offset = (slot - (Runner.SessionInfo.MaxPlayers - 1) * 0.5f) * PlayerSpawnSpacing;
        // Project onto the horizontal plane so the spacing never pushes players upward or downward.
        Vector3 position = rig.Origin.transform.position +
            Vector3.ProjectOnPlane(rig.Origin.transform.right, Vector3.up).normalized * offset;
        position.y = rig.Camera.transform.position.y;
        // Move the rig so the headset reaches this position, accounting for its offset inside the rig.
        rig.MoveCameraToWorldLocation(position);
        // This spawn creates THIS CLIENT'S visible player avatar. Fusion shows it to everyone.
        // The manager's separate Spawn call creates the persistent shared controller instead.
        var avatar = Runner.Spawn(PlayerPrefab, rig.transform.position, rig.transform.rotation);
        // Remember which avatar belongs to us, so we can find it and avoid spawning duplicates.
        Runner.SetPlayerObject(Runner.LocalPlayer, avatar);
        Debug.Log($"Spawned avatar for {Runner.LocalPlayer} in {SceneManager.GetActiveScene().name}, slot {slot}.", this);
    }

    // The master advances the shared lobby countdown on Fusion simulation ticks.
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || loadingScene || Runner.IsSceneManagerBusy ||
            SceneManager.GetActiveScene().buildIndex != LobbySceneBuildIndex) return;
        int count = Runner.ActivePlayers.Count();
        // Cancel the countdown if too few players remain; the next valid group gets a fresh wait.
        if (count < MinimumPlayers)
        {
            CountdownEndTime = 0;
            return;
        }
        // Store a shared finish time, then shorten the wait if the room fills up.
        float now = Runner.SimulationTime;
        if (CountdownEndTime == 0) CountdownEndTime = now + WaitingDelay;
        // Min can shorten an existing deadline, but never extend it on subsequent ticks.
        if (count == Runner.SessionInfo.MaxPlayers)
            CountdownEndTime = Mathf.Min(CountdownEndTime, now + FullRoomDelay);
        if (now < CountdownEndTime) return;
        loadingScene = true;
        // Stop new joins and move everyone together; the scene callbacks replace their avatars.
        Runner.SessionInfo.IsOpen = Runner.SessionInfo.IsVisible = false;
        Runner.LoadScene(SceneRef.FromIndex(GameSceneBuildIndex), LoadSceneMode.Single);
    }

    // Fusion calls Render for visual updates: display shared room data without modifying it.
    public override void Render()
    {
        // The scene's UI references stay on the manager; the spawner supplies the room data.
        var manager = FusionNetworkManager.Instance;
        if (!manager) return;
        if (manager.PlayerCountText)
            manager.PlayerCountText.text = $"{Runner.ActivePlayers.Count()} / {Runner.SessionInfo.MaxPlayers}";
        // Round remaining seconds up for the display and clamp at zero while the scene starts loading.
        if (manager.CountdownText)
            manager.CountdownText.text = CountdownEndTime > 0
                ? Mathf.CeilToInt(Mathf.Max(0, CountdownEndTime - Runner.SimulationTime)).ToString() : "Waiting...";
    }

    // Remove this coordinator's event subscriptions when Unity destroys it.
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (!events) return;
        // Stop the runner from calling methods on a coordinator that has been destroyed.
        events.PlayerJoined.RemoveListener(PlayersChanged);
        events.PlayerLeft.RemoveListener(PlayersChanged);
        events.OnSceneLoadStart.RemoveListener(SceneLoading);
        events.OnSceneLoadDone.RemoveListener(SceneLoaded);
    }
}
