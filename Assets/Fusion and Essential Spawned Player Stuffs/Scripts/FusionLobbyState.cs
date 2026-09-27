using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkObject))]
public class FusionLobbyState : NetworkBehaviour, IPlayerJoined, IPlayerLeft, IStateAuthorityChanged
{
    public static FusionLobbyState Instance;
    [Networked] public TickTimer Countdown { get; set; }
    [Networked, Capacity(20)] public NetworkArray<PlayerRef> SpawnPlayers => default;
    private FusionNetworkManager settings;
    private bool startingGame;

    private void Awake()
    {
        var obj = GetComponent<NetworkObject>();
        obj.Flags |= NetworkObjectFlags.MasterClientObject;
        obj.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
    }

    public override void Spawned()
    {
        Instance = this;
        settings = FusionNetworkManager.Instance;
        RefreshSlots(); // Include players who joined before this object spawned.
        if (HasStateAuthority) Runner.GetComponent<FusionPlayerSpawner>().LobbyReady();
    }

    public void PlayerJoined(PlayerRef player) => RefreshSlots();
    public void PlayerLeft(PlayerRef player) => RefreshSlots();

    public void StateAuthorityChanged() => RefreshSlots();

    private void RefreshSlots()
    {
        if (!Object || !Object.IsValid || !HasStateAuthority) return;
        // Reconcile only on membership/authority events. Survivors keep their slots.
        for (int slot = 0; slot < Runner.SessionInfo.MaxPlayers; slot++)
            if (!Runner.ActivePlayers.Contains(SpawnPlayers[slot])) SpawnPlayers.Set(slot, PlayerRef.None);
        foreach (var player in Runner.ActivePlayers)
        {
            if (SpawnPlayers.Contains(player)) continue;
            for (int slot = 0; slot < Runner.SessionInfo.MaxPlayers; slot++)
            {
                if (SpawnPlayers[slot] != PlayerRef.None) continue;
                SpawnPlayers.Set(slot, player);
                break;
            }
        }
    }

    public override void Render() => RememberSlot();
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (hasState) RememberSlot(); // Also capture a slot arriving on the final lobby tick.
    }

    private void RememberSlot()
    {
        var spawner = Runner.GetComponent<FusionPlayerSpawner>();
        for (int slot = 0; slot < Runner.SessionInfo.MaxPlayers; slot++)
        {
            if (SpawnPlayers[slot] != Runner.LocalPlayer) continue;
            spawner.SpawnSlot = slot;
            spawner.SpawnPlayerCount = Runner.SessionInfo.MaxPlayers;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || startingGame || Runner.IsSceneManagerBusy) return;
        int count = Runner.ActivePlayers.Count();
        if (count < Mathf.Clamp(settings.MinimumPlayers, 2, Runner.SessionInfo.MaxPlayers))
        {
            Countdown = TickTimer.None;
            return;
        }
        if (!Countdown.IsRunning)
            Countdown = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, settings.WaitingDelay));
        float fullRoomDelay = Mathf.Max(0.1f, settings.FullRoomDelay);
        if (count == Runner.SessionInfo.MaxPlayers && Countdown.RemainingTime(Runner) > fullRoomDelay)
            Countdown = TickTimer.CreateFromSeconds(Runner, fullRoomDelay);
        if (!Countdown.Expired(Runner) || !Runner.IsSceneAuthority) return;
        startingGame = true;
        Runner.SessionInfo.IsOpen = Runner.SessionInfo.IsVisible = false;
        Runner.LoadScene(SceneRef.FromIndex(settings.GameSceneBuildIndex), LoadSceneMode.Single).AddOnCompleted(op =>
        {
            if (op.Error != null) settings.Reconnect("Could not load Game.");
        });
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
}
