using UnityEngine;
using Fusion;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

[RequireComponent(typeof(NetworkObject), typeof(NetworkTransform))]
// One avatar per player. The owner reads XR tracking; Fusion shares the poses and color.
// The root NetworkTransform shares the spawn position; child NetworkTransforms share tracked poses.
// Each new Game scene binds a fresh XR rig. Voice stays on the persistent FusionNetworkManager.
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
    private XROrigin _boundOrigin;
    private bool _rigWarningLogged;
    public bool HasRigBindings => _boundOrigin && headRig && leftHandRig && rightHandRig &&
        _boundOrigin.gameObject.scene == gameObject.scene;
    public Animator leftHandAnimator, rightHandAnimator;
    public InputActionProperty LeftActivateAction, LeftGripAction, RightActivateAction, RightGripAction;

    [Networked, OnChangedRender(nameof(ApplyPlayerColor))]
    public Color PlayerColor { get; set; }
    // x/y = left trigger/grip; z/w = right trigger/grip.
    [Networked] private Vector4 HandPose { get; set; }
    private MaterialPropertyBlock _colorBlock;

    // Spread player IDs around the color wheel. The same ID keeps the same color in every scene.
    public static Color ColorForPlayer(PlayerRef player)
    {
        return Color.HSVToRGB(Mathf.Repeat((player.RawEncoded - 1) * 0.61803398875f, 1f), 0.75f, 0.95f);
    }

    private void ApplyPlayerColor()
    {
        if (!Neck)
        {
            Debug.LogWarning($"[Fusion][Color] {FusionPlayerSpawner.PeerLabel(Runner)} Neck is not assigned on '{name}'.", this);
            return;
        }
        if (_colorBlock == null) _colorBlock = new MaterialPropertyBlock();
        // Change this avatar's Neck color without changing the shared material asset.
        foreach (var renderer in Neck.GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetPropertyBlock(_colorBlock);
            _colorBlock.SetColor("_BaseColor", PlayerColor);
            _colorBlock.SetColor("_Color", PlayerColor);
            renderer.SetPropertyBlock(_colorBlock);
        }
        Debug.Log($"[Fusion][Color] {FusionPlayerSpawner.PeerLabel(Runner)} Applied " +
            $"#{ColorUtility.ToHtmlStringRGB(PlayerColor)} to Neck for player {Object.StateAuthority}, object={Object.Id}.", this);
    }

    public override void Render()
    {
        // Both local and remote hands animate from the values synchronized by Fusion.
        UpdateHandAnimation(leftHandAnimator, HandPose.x, HandPose.y);
        UpdateHandAnimation(rightHandAnimator, HandPose.z, HandPose.w);
    }

    public override void Spawned()
    {
        string avatarType = "Remote";
        if (HasStateAuthority) avatarType = "Local";
        Debug.Log($"[Fusion][Avatar] {FusionPlayerSpawner.PeerLabel(Runner)} " +
            $"{avatarType} avatar spawned: '{name}', " +
            $"owner={Object.StateAuthority}, object={Object.Id}, scene='{gameObject.scene.name}'.", this);
        NeckLocalPosition = Vector3.zero;
        if (Neck) NeckLocalPosition = Neck.transform.localPosition;
        if (HasStateAuthority)
        {
            PlayerColor = ColorForPlayer(Object.StateAuthority);
            Debug.Log($"[Fusion][Color] {FusionPlayerSpawner.PeerLabel(Runner)} Assigned networked color " +
                $"#{ColorUtility.ToHtmlStringRGB(PlayerColor)} to player {Object.StateAuthority}.", this);
            BindSceneRig();
            // Locally, show the XR Origin's hands and our Ground Contact only.
            // Renderer visibility is local to this client; remote players see the full avatar.
            DisableAllRenderers(head);
            DisableAllRenderers(leftHand);
            DisableAllRenderers(rightHand);
            if (Neck) DisableAllRenderers(Neck.transform);
        }
        ApplyPlayerColor();
    }

    private void BindSceneRig()
    {
        // Never retain transforms from the previous scene or bind a remote avatar to local input.
        if (!HasStateAuthority) return;
        _boundOrigin = FusionPlayerSpawner.FindSceneOrigin();
        headRig = null;
        leftHandRig = null;
        rightHandRig = null;
        if (_boundOrigin)
        {
            if (_boundOrigin.Camera) headRig = _boundOrigin.Camera.transform;
            foreach (var child in _boundOrigin.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Left Controller" || child.name == "LeftHand Controller" || child.name == "Left Hand") leftHandRig = child;
                if (child.name == "Right Controller" || child.name == "RightHand Controller" || child.name == "Right Hand") rightHandRig = child;
            }
        }
        if (HasRigBindings)
        {
            _rigWarningLogged = false;
            Debug.Log($"[Fusion][XR] {FusionPlayerSpawner.PeerLabel(Runner)} Bound head and both hands to XR Origin " +
                $"'{_boundOrigin.name}' in '{gameObject.scene.name}'.", this);
        }
        else if (!_rigWarningLogged)
        {
            _rigWarningLogged = true;
            Debug.LogWarning("[Fusion][XR] Waiting for this scene's XR camera and Left/Right Controller transforms. Binding will retry.", this);
        }
    }

    private void DisableAllRenderers(Transform root)
    {
        if (!root) return;
        // Include child renderers, even if a child is currently inactive.
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.enabled = false;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Only the owner reads this device's tracking. Child NetworkTransforms share these poses.
        if (!HasStateAuthority) return;
        if (!HasRigBindings) BindSceneRig();
        CopyTrackedPose(head, headRig);
        CopyTrackedPose(leftHand, leftHandRig);
        CopyTrackedPose(rightHand, rightHandRig);
        UpdateBodyPose();

        HandPose = new Vector4(ReadAction(LeftActivateAction), ReadAction(LeftGripAction),
            ReadAction(RightActivateAction), ReadAction(RightGripAction));
    }

    private static float ReadAction(InputActionProperty input)
    {
        if (input.action != null) return input.action.ReadValue<float>();
        return 0f;
    }

    private void UpdateHandAnimation(Animator animator, float trigger, float grip)
    {
        if (!animator) return;
        animator.SetFloat("Trigger", Mathf.Max(0f, trigger));
        animator.SetFloat("Grip", Mathf.Max(0f, grip));
    }

    private void CopyTrackedPose(Transform avatarPart, Transform trackedPart)
    {
        if (!avatarPart || !trackedPart) return;
        avatarPart.position = trackedPart.position;
        avatarPart.rotation = trackedPart.rotation;
    }

    private void UpdateBodyPose()
    {
        if (!headRig) return;
        // Neck and ground marker turn with the head, but stay upright when the player looks up/down.
        Quaternion bodyRotation = Quaternion.Euler(0f, headRig.eulerAngles.y, 0f);
        if (GroundContact)
        {
            Vector3 groundPosition = headRig.position;
            groundPosition.y = 0f;
            GroundContact.transform.SetPositionAndRotation(groundPosition, bodyRotation);
        }
        if (Neck)
            Neck.transform.SetPositionAndRotation(headRig.position + Vector3.up * NeckLocalPosition.y, bodyRotation);
    }
}
