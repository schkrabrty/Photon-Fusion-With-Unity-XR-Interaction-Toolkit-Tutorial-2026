using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

// Like Shared Mode Basics: each client spawns only its own avatar.
[RequireComponent(typeof(NetworkRunner))]
public class FusionPlayerSpawner : SimulationBehaviour, IPlayerJoined, IPlayerLeft, ISceneLoadStart, ISceneLoadDone
{
    public NetworkObject PlayerPrefab;
    public GameObject CameraRig;
    public int GameSceneBuildIndex = 1, LobbySceneBuildIndex = 0;
    [HideInInspector] public int SpawnSlot = -1, SpawnPlayerCount;
    private bool gameSceneReady, returningToLobby;

    public void PlayerJoined(PlayerRef player)
    {
        if (player == Runner.LocalPlayer) SpawnLocalPlayer();
    }

    public void PlayerLeft(PlayerRef player)
    {
        // The avatar's Destroy When State Authority Leaves flag handles cleanup.
        TryReturnToLobby();
    }

    public void SceneLoadStart(SceneRef scene)
    {
        // Recreate the avatar against the next scene's XR rig.
        CameraRig = null;
        var avatar = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (avatar && avatar.HasStateAuthority) Runner.Despawn(avatar);
    }

    public void SceneLoadDone(in SceneLoadDoneArgs args)
    {
        gameSceneReady = args.Scene.buildIndex == GameSceneBuildIndex;
        CameraRig = GameObject.Find("XR Origin (XR Rig)");
        if (!gameSceneReady) SpawnSlot = -1; // Wait for the new lobby's slot assignment.
        SpawnLocalPlayer();
        TryReturnToLobby();
    }

    private void Update()
    {
        if (!Runner || !Runner.IsRunning || Runner.IsSceneManagerBusy) return;
        // Slots may arrive after PlayerJoined/SceneLoadDone. Master transfer may
        // finish after PlayerLeft. These two checks complete that deferred work.
        SpawnLocalPlayer();
        TryReturnToLobby();
    }

    private void SpawnLocalPlayer()
    {
        if (!Runner.IsRunning || Runner.IsSceneManagerBusy || !CameraRig || SpawnSlot < 0 ||
            Runner.LocalPlayer == PlayerRef.None || Runner.GetPlayerObject(Runner.LocalPlayer)) return;

        var origin = CameraRig.GetComponent<XROrigin>();
        float offset = (SpawnSlot - (SpawnPlayerCount - 1) * 0.5f) * FusionNetworkManager.Instance.PlayerSpawnSpacing;
        Vector3 position = origin.Origin.transform.position +
            Vector3.ProjectOnPlane(origin.Origin.transform.right, Vector3.up).normalized * offset;
        position.y = origin.Camera.transform.position.y;
        origin.MoveCameraToWorldLocation(position);

        var avatar = Runner.Spawn(PlayerPrefab, CameraRig.transform.position, CameraRig.transform.rotation);
        Runner.SetPlayerObject(Runner.LocalPlayer, avatar);
    }

    private void TryReturnToLobby()
    {
        if (!Runner.IsRunning || !gameSceneReady || returningToLobby ||
            Runner.IsSceneManagerBusy || !Runner.IsSceneAuthority) return;
        int count = 0;
        foreach (var player in Runner.ActivePlayers) count++;
        if (count != 1) return;

        returningToLobby = true;
        // Keep admission closed until the lobby's NetworkObject is ready.
        Runner.LoadScene(SceneRef.FromIndex(LobbySceneBuildIndex), LoadSceneMode.Single).AddOnCompleted(op =>
        {
            if (op.Error == null) return;
            Debug.LogException(op.Error);
            FusionNetworkManager.Instance.Reconnect("Could not load Lobby.");
        });
    }

    public void LobbyReady()
    {
        returningToLobby = false;
        FusionNetworkManager.Instance.SetStatus("Waiting for others to join...");
        if (!Runner.IsSceneAuthority) return;
        Runner.SessionInfo.IsOpen = true;
        Runner.SessionInfo.IsVisible = true;
    }
}
