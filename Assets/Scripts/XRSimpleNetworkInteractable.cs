using System.Collections.Generic;
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
    private static readonly Dictionary<PlayerRef, Color> _playerColors = new();

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

        if (!_playerColors.ContainsKey(localPlayer))
            _playerColors[localPlayer] = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f);

        if (!Object.HasStateAuthority) Object.RequestStateAuthority();
        RpcSetColor(_playerColors[localPlayer]);
    }

    private void OnHoverExited(HoverExitEventArgs args)
    {
        // Keep the effect while another local hand or ray is still hovering.
        if (_interactable.isHovered) return;
        SetLocalParticles(false);
        if (!Object || !Object.IsValid || !Object.HasStateAuthority) return;
        RpcSetColor(_originalColor);
        Object.ReleaseStateAuthority();
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
