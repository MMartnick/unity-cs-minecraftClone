using UnityEngine;

public class PlayerCollisionHandler : MonoBehaviour
{
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // If the object we hit has a "TeleportOnCollision" component or some known tag
        if (hit.collider.CompareTag("Teleporter"))
        {
            // Teleport ourselves
            transform.position = new Vector3(25f, 250f, 25f);
        }
    }
}
