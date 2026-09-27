using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using System.Collections.Generic;
using Photon.Voice.Unity;
using Photon.Voice.Fusion;

[RequireComponent(typeof(NetworkRunner))]
[RequireComponent(typeof(NetworkEvents))]
[RequireComponent(typeof(Recorder))]
[RequireComponent(typeof(VoiceLogger))]
[RequireComponent(typeof(FusionVoiceClient))]
public class FusionPlayerSpawner : SimulationBehaviour, IPlayerJoined, IPlayerLeft, ISceneLoadDone
{
    [Header("Prefab to Spawn")]
    public NetworkObject PlayerPrefab;
    public Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    [Header("Reference to the Camera Rig")]
    public GameObject CameraRig;
    public int GameSceneBuildIndex = 1;
    public int LobbySceneBuildIndex = 0;
    [HideInInspector] public int SpawnSlot = -1, SpawnPlayerCount;
    private bool gameSceneReady, returningToLobby;
    private XROrigin positionedOrigin;

    public void Start()
    {
        var fvc = GetComponent<FusionVoiceClient>();
        var rec = GetComponent<Recorder>();
        if (fvc && rec) fvc.PrimaryRecorder = rec;
    }

    public void SceneLoadDone(in SceneLoadDoneArgs args)
    {
        gameSceneReady = args.Scene.buildIndex == GameSceneBuildIndex;
        CameraRig = GameObject.Find("XR Origin (XR Rig)");
        if (!gameSceneReady) _spawnedPlayers.Clear();
    }

    private void Update()
    {
        // Wait until Game is ready, including Fusion's final scene-loading work.
        if (!Runner || !Runner.IsRunning || !gameSceneReady || Runner.IsSceneManagerBusy || returningToLobby) return;
        int count = 0;
        foreach (var player in Runner.ActivePlayers) count++;
        // Poll because master authority can transfer after PlayerLeft runs.
        if (count == 1 && Runner.IsSceneAuthority)
        {
            ReturnToLobby();
            return;
        }
        if (!Runner.GetPlayerObject(Runner.LocalPlayer)) PlayerJoined(Runner.LocalPlayer);
    }

    public void PlayerJoined(PlayerRef player)
    {
        if (!gameSceneReady) FusionNetworkManager.Instance.SetStatus($"Player {player.PlayerId} joined. Waiting for others...");
        if (!gameSceneReady || Runner.IsSceneManagerBusy || !CameraRig ||
            SceneManager.GetActiveScene().buildIndex != GameSceneBuildIndex) return;

        if (SpawnSlot < 0) return; // The lobby assigns our starting position.

        // Spawn only for yourself on your own machine.
        if (player != Runner.LocalPlayer)
            return;

        // Guard: never spawn for an invalid/None player
        if (player == PlayerRef.None)
        {
            Debug.LogWarning("Skipping spawn: PlayerRef.None");
            return;
        }

        // Guard: if something already set a player object, don't double-spawn
        if (Runner.GetPlayerObject(player) != null)
        {
            Debug.LogWarning($"Player object already exists for {player}, skipping spawn.");
            return;
        }

        PlaceRigAtSpawn();
        Vector3 spawnPos = CameraRig ? CameraRig.transform.position : Vector3.zero;
        Quaternion spawnRot = CameraRig ? CameraRig.transform.rotation : Quaternion.identity;

        // Give input authority to the joining player
        var obj = Runner.Spawn(PlayerPrefab, spawnPos, spawnRot, inputAuthority: player);

        // Register mapping so all peers can discover it
        Runner.SetPlayerObject(player, obj);

        _spawnedPlayers[player] = obj;

        Debug.Log($"Spawned local player for {player}. InputAuth={obj.InputAuthority}, StateAuth={obj.StateAuthority}");
    }

    private void PlaceRigAtSpawn()
    {
        var origin = CameraRig.GetComponent<XROrigin>();
        if (positionedOrigin == origin) return;
        var rig = origin.Origin.transform;
        float offset = (SpawnSlot - (SpawnPlayerCount - 1) * 0.5f) * FusionNetworkManager.Instance.PlayerSpawnSpacing;
        Vector3 position = rig.position + Vector3.ProjectOnPlane(rig.right, Vector3.up).normalized * offset;
        position.y = origin.Camera.transform.position.y;
        origin.MoveCameraToWorldLocation(position);
        positionedOrigin = origin;
    }

    private void ReturnToLobby()
    {
        returningToLobby = true;
        Runner.SessionInfo.IsOpen = false;
        Runner.SessionInfo.IsVisible = false;
        var avatar = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (avatar && avatar.HasStateAuthority) Runner.Despawn(avatar);
        try
        {
            Runner.LoadScene(SceneRef.FromIndex(LobbySceneBuildIndex), LoadSceneMode.Single).AddOnCompleted(op =>
            {
                if (op.Error != null) ReturnFailed(op.Error);
            });
        }
        catch (System.Exception exception) { ReturnFailed(exception); }
    }

    private void ReturnFailed(System.Exception exception)
    {
        Debug.LogException(exception);
        Invoke(nameof(RetryReturn), 2f);
    }

    private void RetryReturn() { returningToLobby = false; }

    public void LobbyReady()
    {
        returningToLobby = false;
        // The lobby calls this after its countdown object is ready.
        if (Runner.IsSceneAuthority)
        {
            Runner.SessionInfo.IsOpen = true;
            Runner.SessionInfo.IsVisible = true;
        }
    }

    public void PlayerLeft(PlayerRef player)
    {
        // Shared mode normally removes the departing player's object automatically.
        var obj = Runner.GetPlayerObject(player);
        if (obj && obj.HasStateAuthority) Runner.Despawn(obj);
        _spawnedPlayers.Remove(player);
        if (!gameSceneReady) FusionNetworkManager.Instance.SetStatus($"Player {player.PlayerId} left. Waiting for others...");
    }
}
