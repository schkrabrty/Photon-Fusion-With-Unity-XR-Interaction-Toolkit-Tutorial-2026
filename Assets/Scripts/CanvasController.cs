using UnityEngine;

// Make this world-space UI face the camera assigned in the Inspector.
public class CanvasController : MonoBehaviour
{
    public GameObject Camera;

    // No startup setup is needed here; the camera reference is already assigned in the Inspector.
    void Start()
    {
        
    }

    // Reorient every Unity frame so the text remains readable as the viewer moves.
    void Update()
    {
        // LookAt points this transform's forward axis at the camera.
        this.transform.LookAt(Camera.transform);
        // This canvas's readable side faces the opposite way, so turn it half a rotation around local Y.
        this.transform.Rotate(0, 180, 0);
    }
}
