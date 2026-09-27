using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
// Fusion replicates this scene object's rules and countdown to everyone in the room.
// Only the current master changes them. Connection, UI and voice belong to FusionNetworkManager.
public class FusionLobbyState : NetworkBehaviour
{
    // Negative one means we are waiting for enough players, with no active timer.
    public const float NoCountdown = -1f;
    // Storage limit for the shared starting positions; Max Players uses this same limit.
    public const int PlayerSlotCapacity = 256;
    public static FusionLobbyState Instance { get; private set; }
    [Networked] public TickTimer Countdown { get; set; }
    [Networked] public int RequiredPlayers { get; set; }
    [Networked] public float DelaySeconds { get; set; }
    [Networked] public float FullRoomDelaySeconds { get; set; }
    [Networked] public int GameSceneIndex { get; set; }
    [Networked] public float PlayerSpawnSpacing { get; set; }
    [Networked] public int SpawnPlayerCount { get; set; }
    [Networked, Capacity(PlayerSlotCapacity)] public NetworkArray<PlayerRef> SpawnPlayers => default;
    private bool _isLoadingGame;

    private void Awake()
    {
        // Give this object to the new master instead of destroying it when the old master leaves.
        var networkObject = GetComponent<NetworkObject>();
        networkObject.Flags |= NetworkObjectFlags.MasterClientObject;
        networkObject.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
    }

    public override void Spawned()
    {
        Instance = this;
        if (!HasStateAuthority) return;
        var settings = FusionNetworkManager.Instance;
        var session = Runner.GetComponent<FusionPlayerSpawner>();
        int capacity = Runner.SessionInfo.MaxPlayers;
        // Reuse the previous round's settings, or read the Inspector for a new room.
        if (session.HasLobbySettings)
        {
            RequiredPlayers = session.MinimumPlayers;
            DelaySeconds = session.WaitingDelay;
            FullRoomDelaySeconds = session.FullRoomDelay;
            GameSceneIndex = session.GameSceneBuildIndex;
            PlayerSpawnSpacing = session.PlayerSpawnSpacing;
        }
        else
        {
            RequiredPlayers = settings.MinimumPlayers;
            DelaySeconds = settings.WaitingDelay;
            FullRoomDelaySeconds = settings.FullRoomDelay;
            GameSceneIndex = settings.GameSceneBuildIndex;
            PlayerSpawnSpacing = settings.PlayerSpawnSpacing;
        }
        RequiredPlayers = Mathf.Clamp(RequiredPlayers, 2, capacity);
        DelaySeconds = Mathf.Max(0.1f, DelaySeconds);
        FullRoomDelaySeconds = Mathf.Clamp(FullRoomDelaySeconds, 0.1f, DelaySeconds);
        PlayerSpawnSpacing = Mathf.Max(0.1f, PlayerSpawnSpacing);
        Countdown = TickTimer.None;
        UpdateSpawnPlayers();
        RememberSettings();
        session.LobbyReady();
        Debug.Log($"[Fusion][Countdown] {FusionPlayerSpawner.PeerLabel(Runner)} Lobby ready: " +
            $"minimum={RequiredPlayers}, capacity={capacity}, waiting={DelaySeconds}s, full room={FullRoomDelaySeconds}s.", this);
    }

    // Clients may receive settings after Spawned. Keep a copy ready for a future master change.
    public override void Render() { RememberSettings(); }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (hasState) RememberSettings();
    }

    private void RememberSettings()
    {
        // Cache the authoritative settings on every peer, including a future Shared master.
        if (RequiredPlayers < 2) return;
        var spawner = Runner.GetComponent<FusionPlayerSpawner>();
        spawner.RememberLobbySettings(RequiredPlayers, DelaySeconds, FullRoomDelaySeconds,
            GameSceneIndex, PlayerSpawnSpacing);
        for (int slot = 0; slot < SpawnPlayerCount; slot++)
        {
            if (SpawnPlayers[slot] == Runner.LocalPlayer)
            {
                spawner.RememberSpawnPosition(slot, SpawnPlayerCount);
                break;
            }
        }
    }

    private void UpdateSpawnPlayers()
    {
        // The master assigns one position per player while everyone is still in Lobby.
        // Cache these positions before unloading; leaving during Game must not move others.
        int count = 0;
        foreach (var player in Runner.ActivePlayers)
        {
            SpawnPlayers.Set(count, player);
            count++;
        }
        SpawnPlayerCount = count;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || _isLoadingGame) return;
        // Below minimum: wait. At minimum: start the long timer. At capacity: shorten it.
        UpdateSpawnPlayers();
        int count = SpawnPlayerCount;
        float remaining = NoCountdown;
        if (Countdown.IsRunning) remaining = RemainingCountdownSeconds();
        float nextRemaining = CalculateRemainingSeconds(count, RequiredPlayers, Runner.SessionInfo.MaxPlayers,
            DelaySeconds, FullRoomDelaySeconds, remaining);
        if (nextRemaining == NoCountdown)
        {
            if (Countdown.IsRunning)
                Debug.Log($"[Fusion][Countdown] {FusionPlayerSpawner.PeerLabel(Runner)} Cancelled: " +
                    $"{count} players, minimum {RequiredPlayers} required.", this);
            Countdown = TickTimer.None;
            return;
        }
        // Set a deadline only when starting or shortening; do not restart it every tick.
        if (remaining == NoCountdown || nextRemaining < remaining)
        {
            Countdown = TickTimer.CreateFromSeconds(Runner, nextRemaining);
            string action = "Shortened";
            if (remaining == NoCountdown) action = "Started";
            Debug.Log($"[Fusion][Countdown] {FusionPlayerSpawner.PeerLabel(Runner)} " +
                $"{action} countdown: {nextRemaining:0.##}s, " +
                $"players={count}/{Runner.SessionInfo.MaxPlayers}.", this);
        }
        if (!Countdown.Expired(Runner) || !Runner.IsSceneAuthority) return;
        StartGameScene(count);
    }

    private void StartGameScene(int playerCount)
    {
        // Close admission before the shared scene change. Players cannot join an ongoing game.
        _isLoadingGame = true;
        Runner.SessionInfo.IsOpen = false;
        Runner.SessionInfo.IsVisible = false;
        Debug.Log($"[Fusion][Scene] {FusionPlayerSpawner.PeerLabel(Runner)} Countdown finished. " +
            $"Loading Game (build index {GameSceneIndex}) for all {playerCount} players; room closed to joins.", this);
        Runner.LoadScene(SceneRef.FromIndex(GameSceneIndex), LoadSceneMode.Single).AddOnCompleted(op =>
        {
            if (op.Error == null) return;
            Debug.LogError($"[Fusion][Scene] Failed to load Game: {op.Error.Message}.");
            Debug.LogException(op.Error);
            if (!this) return;
            _isLoadingGame = false;
            Countdown = TickTimer.None;
            Runner.SessionInfo.IsOpen = true;
            Runner.SessionInfo.IsVisible = true;
        });
    }

    // Choose the remaining wait: below minimum cancels it; a full room only shortens it.
    public static float CalculateRemainingSeconds(int count, int minimum, int capacity,
        float waitingDelay, float fullRoomDelay, float remaining)
    {
        if (count < minimum) return NoCountdown;
        float seconds = remaining;
        if (remaining == NoCountdown) seconds = waitingDelay;
        if (count >= capacity) seconds = Mathf.Min(seconds, fullRoomDelay);
        return seconds;
    }

    public float RemainingCountdownSeconds()
    {
        // Fusion may return no value for an expired timer. Display zero in that case.
        var remaining = Countdown.RemainingTime(Runner);
        if (remaining.HasValue) return Mathf.Max(0f, remaining.Value);
        return 0f;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
}
