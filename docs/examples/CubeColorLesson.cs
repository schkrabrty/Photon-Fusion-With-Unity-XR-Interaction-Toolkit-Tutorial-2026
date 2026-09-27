using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Optional README exercise: copy into Assets/Scripts and add to Grabbable Cube.
[RequireComponent(typeof(NetworkObject), typeof(XRGrabInteractable), typeof(MeshRenderer))]
public class CubeColorLesson : NetworkBehaviour
{
    [Networked, OnChangedRender(nameof(ApplyColor))]
    public Color CubeColor { get; set; }

    [SerializeField] private ParticleSystem celebration;
    private XRGrabInteractable grab;
    private MeshRenderer cubeRenderer;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        cubeRenderer = GetComponent<MeshRenderer>();
    }

    private void OnEnable() { grab.activated.AddListener(OnActivated); }
    private void OnDisable() { grab.activated.RemoveListener(OnActivated); }

    public override void Spawned()
    {
        if (HasStateAuthority) CubeColor = cubeRenderer.sharedMaterial.color;
        ApplyColor(); // Also apply the current state when this object first appears.
    }

    private void OnActivated(ActivateEventArgs args)
    {
        if (!Object || !Object.IsValid || !grab.isSelected) return;
        RpcRequestColor(Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RpcRequestColor(Color requestedColor, RpcInfo info = default)
    {
        // In this exercise, only the player who owns the held cube may recolor it.
        if (info.Source != Object.StateAuthority) return;
        CubeColor = requestedColor; // Persistent replicated state.
        RpcCelebrate();            // A one-time effect for current peers.
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RpcCelebrate()
    {
        if (celebration) celebration.Play();
    }

    private void ApplyColor()
    {
        cubeRenderer.material.color = CubeColor;
    }
}
