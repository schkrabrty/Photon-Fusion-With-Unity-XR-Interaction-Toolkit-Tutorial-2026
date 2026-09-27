using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

// Shows the local controller or animated hand. FusionNetworkPlayer handles remote avatars.
public class HandPresence : MonoBehaviour
{
    public bool showController = false;
    public InputDeviceCharacteristics controllerCharacteristics;
    public List<GameObject> controllerPrefabs = new List<GameObject>();
    public GameObject handModelPrefab;

    [Tooltip("Find this controller's XRI Activate Value and Select Value actions automatically.")]
    public bool AutoAssignInputActions = true;
    [Tooltip("Filled automatically at runtime. Turn off Auto Assign Input Actions to use manual assignments.")]
    public InputActionProperty ActivateAction, SelectAction;

    private UnityEngine.XR.InputDevice targetDevice;
    private readonly List<UnityEngine.XR.InputDevice> devices = new List<UnityEngine.XR.InputDevice>();
    private GameObject spawnedController;
    private GameObject spawnedHandModel;
    private Animator handAnimator;
    private bool actionsAssigned;
    private bool inputWarningLogged;
    private float nextInitializeTime;

    private void Start()
    {
        TryInitialize();
    }

    private void TryInitialize()
    {
        // Retry missing controllers/actions once per second, including after a reconnect.
        nextInitializeTime = Time.unscaledTime + 1f;
        bool controllerDetected = false;
        if (!targetDevice.isValid)
        {
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(controllerCharacteristics, devices);
            if (devices.Count > 0)
            {
                targetDevice = devices[0];
                controllerDetected = true;
                CreateModels();
                Debug.Log($"[XR][Hand] Detected {targetDevice.name} ({targetDevice.characteristics}) on '{name}'.", this);
            }
        }

        if (!actionsAssigned || controllerDetected) actionsAssigned = TryAssignInputActions();
    }

    private bool TryAssignInputActions()
    {
        if (!AutoAssignInputActions)
            return ActivateAction.action != null && SelectAction.action != null;

        // Use the detected device's side. Before it connects, the Inspector's Left/Right
        // characteristics let us bind the actions without needing an active headset.
        var characteristics = controllerCharacteristics;
        if (targetDevice.isValid) characteristics = targetDevice.characteristics;
        bool isLeft = (characteristics & InputDeviceCharacteristics.Left) != 0;
        bool isRight = (characteristics & InputDeviceCharacteristics.Right) != 0;
        if (isLeft == isRight) return false; // A hand must identify exactly one side.

        string mapName = "XRI Right Interaction";
        if (isLeft) mapName = "XRI Left Interaction";
        var inputManager = GetComponentInParent<InputActionManager>(true);
        if (inputManager && inputManager.actionAssets != null)
        {
            foreach (var asset in inputManager.actionAssets)
            {
                if (!asset) continue;
                var activate = asset.FindAction(mapName + "/Activate Value");
                var select = asset.FindAction(mapName + "/Select Value");
                if (activate == null || select == null) continue;

                // Reuse the rig's actions. Its Input Action Manager controls enabling them.
                ActivateAction = new InputActionProperty(activate);
                SelectAction = new InputActionProperty(select);
                inputWarningLogged = false;
                Debug.Log($"[XR][Hand] '{name}' uses {mapName}/Activate Value and Select Value.", this);
                return true;
            }
        }

        if (!inputWarningLogged)
        {
            Debug.LogWarning($"[XR][Hand] Could not find {mapName} actions on the parent Input Action Manager. " +
                "Check its XRI input asset, or turn off Auto Assign Input Actions and assign actions manually.", this);
            inputWarningLogged = true;
        }
        return false;
    }

    private void CreateModels()
    {
        // Replace only the controller model on reconnect; keep one hand model.
        if (spawnedController) Destroy(spawnedController);
        spawnedController = null;
        if (controllerPrefabs != null)
        {
            foreach (var prefab in controllerPrefabs)
            {
                if (!prefab || prefab.name != targetDevice.name) continue;
                spawnedController = Instantiate(prefab, transform);
                break;
            }
        }
        if (!spawnedHandModel && handModelPrefab)
        {
            spawnedHandModel = Instantiate(handModelPrefab, transform);
            handAnimator = spawnedHandModel.GetComponentInChildren<Animator>();
        }
    }

    private void UpdateHandAnimation()
    {
        if (!handAnimator) return;
        // The Value actions provide gradual 0-1 finger movement rather than button states.
        handAnimator.SetFloat("Trigger", ReadAction(ActivateAction));
        handAnimator.SetFloat("Grip", ReadAction(SelectAction));
    }

    private static float ReadAction(InputActionProperty input)
    {
        if (input.action == null) return 0f;
        return Mathf.Clamp01(input.action.ReadValue<float>());
    }

    private void Update()
    {
        if ((!targetDevice.isValid || !actionsAssigned) && Time.unscaledTime >= nextInitializeTime)
            TryInitialize();

        bool connected = targetDevice.isValid;
        if (spawnedHandModel) spawnedHandModel.SetActive(connected && !showController);
        if (spawnedController) spawnedController.SetActive(connected && showController);
        if (connected && !showController) UpdateHandAnimation();
    }
}
