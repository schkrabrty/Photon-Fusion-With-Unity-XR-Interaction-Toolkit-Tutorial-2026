using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

// Local XR visuals: show a controller model or animate a hand. This script does not send network data.
public class HandPresence : MonoBehaviour
{
    public bool showController = false;
    // Inspector flags select which tracked device to use, for example the left or right controller.
    public InputDeviceCharacteristics controllerCharacteristics;
    public List<GameObject> controllerPrefabs;
    public GameObject handModelPrefab;
    public InputActionProperty ActivateAction, SelectAction;
    
    private UnityEngine.XR.InputDevice targetDevice;
    private GameObject spawnedController;
    private GameObject spawnedHandModel;
    private Animator handAnimator;

    // Try to find the controller once at startup; Update retries if XR hardware is not ready yet.
    void Start()
    {
        TryInitialize();
    }

    // Find a matching XR device, then create its controller model and an animated hand as children.
    void TryInitialize()
    {
        List<UnityEngine.XR.InputDevice> devices = new List<UnityEngine.XR.InputDevice>();

        // This fills the list with connected devices matching the Inspector flags.
        InputDevices.GetDevicesWithCharacteristics(controllerCharacteristics, devices);

        // Log detected device names to help match entries in controllerPrefabs.
        foreach (var item in devices)
        {
            Debug.Log(item.name + item.characteristics);
        }

        if (devices.Count > 0)
        {
            targetDevice = devices[0];
            // Use the first matching device and find a model whose prefab name equals its device name.
            GameObject prefab = controllerPrefabs.Find(controller => controller.name == targetDevice.name);
            if (prefab)
            {
                spawnedController = Instantiate(prefab, transform);
            }
            else
            {
                Debug.Log("Did not find corresponding controller model");
            }

            // The hand is created even if no matching controller model is available.
            spawnedHandModel = Instantiate(handModelPrefab, transform);
            handAnimator = spawnedHandModel.GetComponent<Animator>();
        }
    }

    // Read trigger/grip input and send it to the local hand Animator's matching float parameters.
    void UpdateHandAnimation()
    {
        // These Inspector-assigned actions normally produce values from 0 (released) to 1 (fully pressed).
        float triggerValue = ActivateAction.action.ReadValue<float>();
        float gripValue = SelectAction.action.ReadValue<float>();

        // Reset released inputs to zero so the hand does not stay in its previous pose.
        if (triggerValue > 0)
        {
            handAnimator.SetFloat("Trigger", triggerValue);
        }
        else
        {
            handAnimator.SetFloat("Trigger", 0);
        }

        if (gripValue > 0)
        {
            handAnimator.SetFloat("Grip", gripValue);
        }
        else
        {
            handAnimator.SetFloat("Grip", 0);
        }
    }

    // Each Unity frame, retry device setup if needed; otherwise display the selected visual style.
    void Update()
    {
        if(!targetDevice.isValid)
        {
            TryInitialize();
        }
        else
        {
            // Only one visual should be visible. Animate the hand only while hand mode is selected.
            if (showController)
            {
                if(spawnedHandModel)
                    spawnedHandModel.SetActive(false);
                if(spawnedController)
                    spawnedController.SetActive(true);
            }
            else
            {
                if (spawnedHandModel)
                    spawnedHandModel.SetActive(true);
                if (spawnedController)
                    spawnedController.SetActive(false);
                UpdateHandAnimation();
            }
        }
    }
}
