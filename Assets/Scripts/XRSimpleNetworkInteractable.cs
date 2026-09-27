using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Hover feedback: particles are local, while an RPC tells connected clients to change the cube color.
[RequireComponent(typeof(NetworkObject), typeof(XRSimpleInteractable))]
public class XRSimpleNetworkInteractable : NetworkBehaviour
{
    private XRSimpleInteractable _interactable;
    // SerializeField exposes these private references in the Inspector for scene setup.
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private TMP_Text txtInfo;
    [SerializeField] private ParticleSystem touchParticles;
    private Color _originalColor;

    // Find feedback components, remember the starting material color, and begin with particles stopped.
    private void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        // Prefer Inspector assignments; otherwise search this object or its children (including inactive ones).
        if (!targetRenderer) targetRenderer = GetComponent<MeshRenderer>();
        if (!txtInfo) txtInfo = GetComponentInChildren<TMP_Text>(true);
        if (!touchParticles) touchParticles = GetComponentInChildren<ParticleSystem>(true);
        // Save the actual material color so hover exit restores the scene's chosen appearance.
        if (targetRenderer) _originalColor = targetRenderer.material.color;
        if (touchParticles)
        {
            // Particle settings live in modules; disable automatic playback and clear any existing particles.
            var main = touchParticles.main;
            main.playOnAwake = false;
            touchParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    // Listen for local hover events while this component is enabled.
    private void OnEnable()
    {
        _interactable.hoverEntered.AddListener(OnHoverEntered);
        _interactable.hoverExited.AddListener(OnHoverExited);
    }

    // Remove the listeners and clear particles when this object becomes inactive.
    private void OnDisable()
    {
        _interactable.hoverEntered.RemoveListener(OnHoverEntered);
        _interactable.hoverExited.RemoveListener(OnHoverExited);
        if (touchParticles) touchParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // When a local hand/ray hovers, play local particles and broadcast that player's avatar color.
    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (touchParticles && !touchParticles.isPlaying) touchParticles.Play(true);
        // Local feedback can run before joining, but an RPC requires a valid Fusion object.
        if (!Object || !Object.IsValid) return;
        var localPlayer = Runner.LocalPlayer;
        if (localPlayer == PlayerRef.None) return;

        // SetPlayerObject in the spawner registered this avatar; read its shared PlayerColor.
        // All -> All RPCs let any connected player send this interaction without taking authority.
        var avatar = Runner.GetPlayerObject(localPlayer);
        if (avatar) RpcSetColor(avatar.GetComponent<FusionNetworkPlayer>().PlayerColor);
    }

    // After the last local hover ends, stop local particles and broadcast the starting color.
    private void OnHoverExited(HoverExitEventArgs args)
    {
        // Keep the effect while another local hand or ray is still hovering.
        if (_interactable.isHovered) return;
        if (touchParticles) touchParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (!Object || !Object.IsValid) return;
        RpcSetColor(_originalColor);
    }

    // RPC = remote procedure call: any player may send it, and all current clients execute it.
    // This is an interaction message, not stored networked state; late joiners do not replay past colors.
    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RpcSetColor(Color color)
    {
        // Each recipient updates its own renderer and hides the prompt while showing a player color.
        if (targetRenderer) targetRenderer.material.color = color;
        if (txtInfo) txtInfo.text = color.Equals(_originalColor) ? "Tap the cube!" : "";
    }
}
