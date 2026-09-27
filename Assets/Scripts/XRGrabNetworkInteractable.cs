using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

[RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable), typeof(XRGeneralGrabTransformer))]
public class XRGrabNetworkInteractable : NetworkBehaviour, IStateAuthorityChanged
{
    private XRGrabInteractable _grab;
    private bool awaitingAuthority;

    private void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
    }

    private void OnEnable()
    {
        _grab.selectEntered.AddListener(OnSelectEntered);
        _grab.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        _grab.selectEntered.RemoveListener(OnSelectEntered);
        _grab.selectExited.RemoveListener(OnSelectExited);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!Object || !Object.IsValid || Runner.LocalPlayer == PlayerRef.None) return;
        // In Shared mode, request ownership so this client can synchronize the cube.
        if (HasStateAuthority) return;
        awaitingAuthority = true;
        Object.RequestStateAuthority();
    }

    public void StateAuthorityChanged()
    {
        // A quick release may happen before the request is granted.
        if (!HasStateAuthority || !awaitingAuthority) return;
        awaitingAuthority = false;
        if (!_grab.isSelected) Object.ReleaseStateAuthority();
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (_grab.isSelected || !Object || !Object.IsValid) return;
        // Relinquish ownership when the last local hand releases the cube.
        if (Object.HasStateAuthority) Object.ReleaseStateAuthority();
    }
}
