using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// The Shared master runs one countdown; Fusion sends it to everyone else.
[RequireComponent(typeof(NetworkObject))]
public class FusionLobbyState : NetworkBehaviour
{
    public static FusionLobbyState Instance;
    [Networked] public TickTimer Countdown { get; set; }
    [Networked] public int RequiredPlayers { get; set; }
    [Networked] public float DelaySeconds { get; set; }
    [Networked] public float FullRoomDelaySeconds { get; set; }
    [Networked] public int GameSceneIndex { get; set; }
    [Networked] public float PlayerSpawnSpacing { get; set; }
    [Networked] public int SpawnPlayerCount { get; set; }
    [Networked, Capacity(256)] public NetworkArray<PlayerRef> SpawnPlayers => default;
    private bool startingGame;

    private void Awake()
    {
        // Transfer this scene object if the master leaves the room.
        var networkObject = GetComponent<NetworkObject>();
        networkObject.Flags |= NetworkObjectFlags.MasterClientObject;
        networkObject.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
    }

    public override void Spawned()
    {
        Instance = this;
        if (!HasStateAuthority) return;
        var settings = FusionNetworkManager.Instance;
        RequiredPlayers = Mathf.Clamp(settings.MinimumPlayers, 2, Runner.SessionInfo.MaxPlayers);
        DelaySeconds = Mathf.Max(0.1f, settings.WaitingDelay);
        FullRoomDelaySeconds = Mathf.Clamp(settings.FullRoomDelay, 0.1f, DelaySeconds);
        GameSceneIndex = settings.GameSceneBuildIndex;
        PlayerSpawnSpacing = Mathf.Max(0.1f, settings.PlayerSpawnSpacing);
        Countdown = TickTimer.None;
        Runner.GetComponent<FusionPlayerSpawner>().LobbyReady();
    }

    public override void Render() { RememberLobby(); }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (hasState) RememberLobby();
    }

    private void RememberLobby()
    {
        if (RequiredPlayers < 2) return;
        var settings = FusionNetworkManager.Instance;
        settings.MinimumPlayers = RequiredPlayers;
        settings.WaitingDelay = DelaySeconds;
        settings.FullRoomDelay = FullRoomDelaySeconds;
        settings.GameSceneBuildIndex = GameSceneIndex;
        settings.PlayerSpawnSpacing = PlayerSpawnSpacing;
        var spawner = Runner.GetComponent<FusionPlayerSpawner>();
        spawner.GameSceneBuildIndex = GameSceneIndex;
        for (int slot = 0; slot < SpawnPlayerCount; slot++)
        {
            if (SpawnPlayers[slot] != Runner.LocalPlayer) continue;
            spawner.SpawnSlot = slot;
            spawner.SpawnPlayerCount = SpawnPlayerCount;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || startingGame) return;
        int count = 0;
        foreach (var player in Runner.ActivePlayers) SpawnPlayers.Set(count++, player);
        SpawnPlayerCount = count;
        RememberLobby();
        if (count < RequiredPlayers)
        {
            Countdown = TickTimer.None;
            return;
        }
        if (!Countdown.IsRunning)
            Countdown = TickTimer.CreateFromSeconds(Runner, DelaySeconds);
        if (count == Runner.SessionInfo.MaxPlayers && RemainingCountdownSeconds() > FullRoomDelaySeconds)
            Countdown = TickTimer.CreateFromSeconds(Runner, FullRoomDelaySeconds);
        if (Countdown.Expired(Runner) && Runner.IsSceneAuthority) StartGame();
    }

    private void StartGame()
    {
        startingGame = true;
        Runner.SessionInfo.IsOpen = false;
        Runner.SessionInfo.IsVisible = false;
        Runner.LoadScene(SceneRef.FromIndex(GameSceneIndex), LoadSceneMode.Single).AddOnCompleted(op =>
        {
            if (op.Error == null || !this) return;
            Debug.LogException(op.Error);
            startingGame = false;
            Countdown = TickTimer.None;
            Runner.SessionInfo.IsOpen = true;
            Runner.SessionInfo.IsVisible = true;
        });
    }

    public float RemainingCountdownSeconds()
    {
        return Mathf.Max(0f, Countdown.RemainingTime(Runner) ?? 0f);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
