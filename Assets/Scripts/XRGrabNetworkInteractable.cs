using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

// Let the client grabbing this cube become the authority that publishes its movement to everyone.
// RequireComponent lists the components Unity should add with this script.
[RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable), typeof(XRGeneralGrabTransformer))]
public class XRGrabNetworkInteractable : NetworkBehaviour, IStateAuthorityChanged
{
    private XRGrabInteractable _grab;
    private Rigidbody body;

    // Cache the XR grab component once so interaction callbacks can use it.
    private void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        body = GetComponent<Rigidbody>();
    }

    // Listen for local XR selection (grab) and release while this component is enabled.
    private void OnEnable()
    {
        _grab.selectEntered.AddListener(OnSelectEntered);
        _grab.selectExited.AddListener(OnSelectExited);
    }

    // Unsubscribe when disabled so callbacks do not keep running or get registered twice on re-enable.
    private void OnDisable()
    {
        _grab.selectEntered.RemoveListener(OnSelectEntered);
        _grab.selectExited.RemoveListener(OnSelectExited);
    }

    // Called when a local hand/ray grabs the cube: ask Fusion for permission to publish its state.
    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        // XR interaction can start before Fusion has attached a valid network object and local player.
        if (!Object || !Object.IsValid || Runner.LocalPlayer == PlayerRef.None) return;
        // In Shared mode, request ownership so this client can synchronize the cube.
        if (HasStateAuthority) return;
        // A request is asynchronous: authority may arrive later through StateAuthorityChanged.
        // The cube's NetworkObject must allow state authority override for another client to take it.
        Object.RequestStateAuthority();
    }

    // If authority arrives after a quick release, make the cube dynamic so it can still fall.
    public void StateAuthorityChanged()
    {
        if (!HasStateAuthority || _grab.isSelected) return;
        body.isKinematic = false;
        body.WakeUp();
    }

    // After the last local hand lets go, keep authority to simulate falling/throwing and sync the result.
    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (_grab.isSelected || !Object || !Object.IsValid || !HasStateAuthority) return;
        // XRI may restore the kinematic state it recorded while we were still a remote proxy.
        // This cube should use gravity after release, so explicitly restore its dynamic body.
        body.isKinematic = false;
        body.WakeUp();
        // Allow State Authority Override lets the next grabber take over without releasing it here.
    }
}
