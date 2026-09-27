using System.Linq;
using Fusion;
using TMPro;
using UnityEngine;

// This component belongs to the scene's canvas, not the persistent runner.
public class FusionLobbyUI : MonoBehaviour
{
    public TMP_Text StatusText, PlayerCountText, CountdownText;
    public bool MatchCameraHeight = true;
    public Transform LobbyCanvas;
    public Camera LobbyCamera;
    public float CanvasHeightOffset;

    private void LateUpdate()
    {
        var manager = FusionNetworkManager.Instance;
        if (!manager) return;
        var runner = manager.GetComponent<NetworkRunner>();
        bool inRoom = runner.IsRunning;
        if (StatusText)
        {
            StatusText.text = manager.Status;
            if (inRoom) StatusText.color = FusionNetworkPlayer.ColorForPlayer(runner.LocalPlayer);
        }
        if (PlayerCountText)
            PlayerCountText.text = $"{(inRoom ? runner.ActivePlayers.Count() : 0)} / {(inRoom ? runner.SessionInfo.MaxPlayers : manager.MaxPlayers)}";
        var lobby = FusionLobbyState.Instance;
        if (CountdownText)
            CountdownText.text = inRoom && lobby && lobby.Object && lobby.Object.IsValid && lobby.Countdown.IsRunning
                ? Mathf.CeilToInt(lobby.Countdown.RemainingTime(runner) ?? 0).ToString() : "Waiting...";
        if (!MatchCameraHeight || !LobbyCanvas || !LobbyCamera) return;
        var position = LobbyCanvas.position;
        position.y = LobbyCamera.transform.position.y + CanvasHeightOffset;
        LobbyCanvas.position = position;
    }
}
