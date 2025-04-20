using UnityEngine;

public class Billboard : MonoBehaviour
{
    public Camera ThirdPersonCamera;

    void LateUpdate()
    {
        // If you want the sprite to look directly at the camera:
        transform.LookAt(ThirdPersonCamera.transform);

        // If the sprite faces away from the camera by default, rotate 180 degrees:
        transform.Rotate(35, 0, 0);
    }
}
