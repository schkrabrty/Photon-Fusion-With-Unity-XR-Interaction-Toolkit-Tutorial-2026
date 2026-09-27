using UnityEngine;
using Fusion;
using UnityEngine.InputSystem;

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

    [Networked, OnChangedRender(nameof(ApplyPlayerColor))]
    public Color PlayerColor { get; set; }

    public static Color ColorForPlayer(PlayerRef player)
    {
        return Color.HSVToRGB(Mathf.Repeat((player.RawEncoded - 1) * 0.61803398875f, 1f), 0.75f, 0.95f);
    }

    private void ApplyPlayerColor()
    {
        var color = new MaterialPropertyBlock();
        foreach (var renderer in Neck.GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetPropertyBlock(color);
            color.SetColor("_BaseColor", PlayerColor);
            color.SetColor("_Color", PlayerColor);
            renderer.SetPropertyBlock(color);
        }
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            PlayerColor = ColorForPlayer(Object.StateAuthority);
            // Map XR rig references
            headRig = GameObject.Find("XR Origin (XR Rig)/Camera Offset/Main Camera").transform;
            leftHandRig = GameObject.Find("XR Origin (XR Rig)/Camera Offset/Left Controller").transform;
            rightHandRig = GameObject.Find("XR Origin (XR Rig)/Camera Offset/Right Controller").transform;
            NeckLocalPosition = Neck.transform.localPosition;

            // --- Hide all renderers on head, hands, and neck for the local player ---
            DisableAllRenderers(head);
            DisableAllRenderers(leftHand);
            DisableAllRenderers(rightHand);
            DisableAllRenderers(Neck.transform);
        }
        ApplyPlayerColor();
    }

    private void DisableAllRenderers(Transform root)
    {
        // includeInactive:true makes sure we catch children that might be disabled
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            // Debug.Log($"Disabling renderer: {r.gameObject.name}");
            r.enabled = false;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
        {
            MapPoseToRig(head, headRig);
            MapPoseToRig(leftHand, leftHandRig);
            MapPoseToRig(rightHand, rightHandRig);

            UpdateHandAnimation(leftHand, 1);
            UpdateHandAnimation(rightHand, 2);
        }
    }

    private void UpdateHandAnimation(Transform Hand, int value)
    {
        float triggervalue = 0;
        float gripValue = 0;

        if (Hand == leftHand)
        {
            triggervalue = LeftActivateAction.action.ReadValue<float>();
            gripValue = LeftGripAction.action.ReadValue<float>();
        }
        else
        {
            triggervalue = RightActivateAction.action.ReadValue<float>();
            gripValue = RightGripAction.action.ReadValue<float>();
        }

        TriggerAndGripSetter(triggervalue, gripValue, value);
    }

    private void TriggerAndGripSetter(float triggervalue, float gripValue, int value)
    {
        Animator handAnimator = null;

        if (value == 1)
            handAnimator = leftHandAnimator;
        else if (value == 2)
            handAnimator = rightHandAnimator;

        if (triggervalue > 0)
            handAnimator.SetFloat("Trigger", triggervalue);
        else
            handAnimator.SetFloat("Trigger", 0);

        if (gripValue > 0)
            handAnimator.SetFloat("Grip", gripValue);
        else
            handAnimator.SetFloat("Grip", 0);
    }

    private void MapPoseToRig(Transform rig, Transform target)
    {
        rig.position = target.position;
        rig.rotation = target.rotation;

        if (target == headRig)
        {
            Vector3 GroundContactPostition = target.position;
            Vector3 HeadRotation = target.rotation.eulerAngles;
            HeadRotation.x = 0;
            HeadRotation.z = 0;
            if (HeadRotation.y > 180.0f)
                HeadRotation.y -= 360.0f;

            GroundContactPostition.y = 0;
            GroundContact.transform.position = GroundContactPostition;
            GroundContact.transform.rotation = Quaternion.Euler(HeadRotation);

            Neck.transform.rotation = Quaternion.Euler(HeadRotation);
            Vector3 offset = new Vector3(target.position.x, target.position.y + (NeckLocalPosition.y), target.position.z);
            Neck.transform.position = offset;
        }
    }
}
