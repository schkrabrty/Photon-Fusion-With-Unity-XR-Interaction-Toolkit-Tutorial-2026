using UnityEngine;
using Fusion;
using UnityEngine.InputSystem;

// The network avatar follows its owner's local XR rig; other clients see the synchronized result.
[RequireComponent(typeof(NetworkObject))]
// Voice is managed by the persistent FusionVoiceClient in this project.
public class FusionNetworkPlayer : NetworkBehaviour
{
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;
    public GameObject GroundContact, Neck;
    private Vector3 NeckLocalPosition;

    private Transform headRig;
    private Transform leftHandRig;
    private Transform rightHandRig;
    public Animator leftHandAnimator, rightHandAnimator;
    public InputActionProperty LeftActivateAction, LeftGripAction, RightActivateAction, RightGripAction;

    // Fusion stores and replicates the color. When a received change is rendered, repaint this avatar.
    [Networked, OnChangedRender(nameof(ApplyPlayerColor))]
    public Color PlayerColor { get; set; }

    // Derive a repeatable color from the player ID, without needing a manually assigned color list.
    public static Color ColorForPlayer(PlayerRef player)
    {
        // The multiplier spreads successive IDs around the hue wheel; Repeat wraps hue into 0..1.
        // Saturation (0.75) and brightness (0.95) keep the resulting colors vivid.
        return Color.HSVToRGB(Mathf.Repeat((player.RawEncoded - 1) * 0.61803398875f, 1f), 0.75f, 0.95f);
    }

    // Apply the shared torso color locally, both after spawning and when PlayerColor changes.
    private void ApplyPlayerColor()
    {
        // A property block changes each renderer's appearance without editing the shared material asset.
        var color = new MaterialPropertyBlock();
        foreach (var renderer in Neck.GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetPropertyBlock(color);
            // Preserve other overrides, then set both common shader color property names.
            color.SetColor("_BaseColor", PlayerColor);
            color.SetColor("_Color", PlayerColor);
            renderer.SetPropertyBlock(color);
        }
    }

    // Fusion calls this when the avatar is ready: its owner connects it to local headset/controller objects.
    public override void Spawned()
    {
        // In this Shared-mode setup, the client that spawned the avatar owns its state.
        if (HasStateAuthority)
        {
            PlayerColor = ColorForPlayer(Object.StateAuthority);
            // These paths refer to the local scene's XR rig, so only our own avatar should use them.
            headRig = GameObject.Find("XR Origin (XR Rig)/Camera Offset/Main Camera").transform;
            leftHandRig = GameObject.Find("XR Origin (XR Rig)/Camera Offset/Left Controller").transform;
            rightHandRig = GameObject.Find("XR Origin (XR Rig)/Camera Offset/Right Controller").transform;
            NeckLocalPosition = Neck.transform.localPosition;

            // Our local rig already shows hands. Hide this avatar's duplicate body parts on OUR client;
            // the other clients keep their copies visible and can still see us.
            DisableAllRenderers(head);
            DisableAllRenderers(leftHand);
            DisableAllRenderers(rightHand);
            DisableAllRenderers(Neck.transform);
        }
        // Apply the initial value explicitly; a change callback alone does not initialize the appearance.
        ApplyPlayerColor();
    }

    // Hide visuals under a body-part root while keeping its transforms and network components active.
    private void DisableAllRenderers(Transform root)
    {
        // includeInactive:true makes sure we catch children that might be disabled
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.enabled = false;
        }
    }

    // On each Fusion tick, the owner copies tracking and hand input into the network avatar.
    // Its NetworkTransform/NetworkMecanimAnimator components synchronize those results to others.
    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
        {
            MapPoseToRig(head, headRig);
            MapPoseToRig(leftHand, leftHandRig);
            MapPoseToRig(rightHand, rightHandRig);

            // Input actions provide trigger/grip amounts; Animator parameters blend the hand poses.
            // Max prevents a negative value from being sent to these animation parameters.
            leftHandAnimator.SetFloat("Trigger", Mathf.Max(0, LeftActivateAction.action.ReadValue<float>()));
            leftHandAnimator.SetFloat("Grip", Mathf.Max(0, LeftGripAction.action.ReadValue<float>()));
            rightHandAnimator.SetFloat("Trigger", Mathf.Max(0, RightActivateAction.action.ReadValue<float>()));
            rightHandAnimator.SetFloat("Grip", Mathf.Max(0, RightGripAction.action.ReadValue<float>()));
        }
    }

    // Copy one tracked target's world pose to an avatar part. The head also positions the torso and floor marker.
    private void MapPoseToRig(Transform rig, Transform target)
    {
        rig.position = target.position;
        rig.rotation = target.rotation;

        if (target == headRig)
        {
            Vector3 GroundContactPostition = target.position;
            Vector3 HeadRotation = target.rotation.eulerAngles;
            // Keep only yaw (turning left/right), so looking up or tilting does not tip the torso or marker.
            HeadRotation.x = 0;
            HeadRotation.z = 0;
            // Express angles above 180 as equivalent negative angles, e.g. 270 becomes -90 degrees.
            if (HeadRotation.y > 180.0f)
                HeadRotation.y -= 360.0f;

            // This scene uses floor height y = 0; place the marker directly below the headset.
            GroundContactPostition.y = 0;
            GroundContact.transform.position = GroundContactPostition;
            GroundContact.transform.rotation = Quaternion.Euler(HeadRotation);

            Neck.transform.rotation = Quaternion.Euler(HeadRotation);
            // Follow headset height while retaining the torso's configured vertical offset.
            Vector3 offset = new Vector3(target.position.x, target.position.y + (NeckLocalPosition.y), target.position.z);
            Neck.transform.position = offset;
        }
    }
}
