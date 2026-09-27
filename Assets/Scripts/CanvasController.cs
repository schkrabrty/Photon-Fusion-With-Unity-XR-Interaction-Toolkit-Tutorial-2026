using UnityEngine;

public class CanvasController : MonoBehaviour
{
    public GameObject Camera;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        this.transform.LookAt(Camera.transform); // Turn the canvas to face the camera
        this.transform.Rotate(0, 180, 0); // Rotate the canvas by 180 degree on Y-axis to read the words properly
    }
}
