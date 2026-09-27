using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(NetworkObject), typeof(XRSimpleInteractable))]
public class XRSimpleNetworkInteractable : NetworkBehaviour
{
    private XRSimpleInteractable _interactable;
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private TMP_Text txtInfo;
    [SerializeField] private ParticleSystem touchParticles;
    private Color _originalColor;

    private void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        if (!targetRenderer) targetRenderer = GetComponent<MeshRenderer>();
        if (!txtInfo) txtInfo = GetComponentInChildren<TMP_Text>(true);
        if (!touchParticles) touchParticles = GetComponentInChildren<ParticleSystem>(true);
        if (targetRenderer) _originalColor = targetRenderer.material.color;
        if (touchParticles)
        {
            var main = touchParticles.main;
            main.playOnAwake = false;
            SetLocalParticles(false);
        }
    }

    private void OnEnable()
    {
        _interactable.hoverEntered.AddListener(OnHoverEntered);
        _interactable.hoverExited.AddListener(OnHoverExited);
    }

    private void OnDisable()
    {
        _interactable.hoverEntered.RemoveListener(OnHoverEntered);
        _interactable.hoverExited.RemoveListener(OnHoverExited);
        SetLocalParticles(false);
    }

    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        SetLocalParticles(true);
        if (!Object || !Object.IsValid) return;
        var localPlayer = Runner.LocalPlayer;
        if (localPlayer == PlayerRef.None) return;

        // All -> All RPCs need no authority transfer. Read our real avatar color.
        var avatar = Runner.GetPlayerObject(localPlayer);
        if (avatar) RpcSetColor(avatar.GetComponent<FusionNetworkPlayer>().PlayerColor);
    }

    private void OnHoverExited(HoverExitEventArgs args)
    {
        // Keep the effect while another local hand or ray is still hovering.
        if (_interactable.isHovered) return;
        SetLocalParticles(false);
        if (!Object || !Object.IsValid) return;
        RpcSetColor(_originalColor);
    }

    // Only local interaction events control particles, never an RPC.
    private void SetLocalParticles(bool interacting)
    {
        if (!touchParticles) return;
        if (!interacting) touchParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        else if (!touchParticles.isPlaying) touchParticles.Play(true);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RpcSetColor(Color color)
    {
        if (targetRenderer) targetRenderer.material.color = color;
        if (txtInfo) txtInfo.text = color.Equals(_originalColor) ? "Tap the cube!" : "";
    }
}
