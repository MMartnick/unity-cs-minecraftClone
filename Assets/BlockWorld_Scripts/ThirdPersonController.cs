using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float jumpSpeed = 5f;
    public float gravity = 9f;

    [Header("Camera Settings")]
    public Transform overheadCamera;     // Assign your third-person camera in the Inspector
    public Vector3 cameraOffset = new Vector3(0f, 35f, -15f);

    [Header("Sprite Settings")]
    public Transform spriteTransform;    // Drag the child sprite here; it will billboard to the camera

    private CharacterController characterController;
    private float verticalVelocity = 0f;

    void Awake()
    {
        characterController = GetComponent<CharacterController>();

        // Optionally, you can do a runtime lookup for your overheadCamera
        // if it’s not assigned in the inspector:
        //
        // if (overheadCamera == null)
        // {
        //     overheadCamera = GameObject.Find("ThirdPersonCamera").transform;
        // }
    }

    void Update()
    {
        // ----- 1) Handle input & movement -----
        float horizontal = CrossPlatformInputManager.GetAxis("Horizontal"); // A/D or Left/Right
        float vertical = CrossPlatformInputManager.GetAxis("Vertical");   // W/S or Up/Down

        // Move in XZ plane only
        Vector3 move = new Vector3(horizontal, 0f, vertical) * moveSpeed;

        // Gravity & jumping
        if (characterController.isGrounded)
        {
            // Minor downward force so we stay “glued” to ground
            verticalVelocity = -0.5f;

            // Check jump
            if (CrossPlatformInputManager.GetButtonDown("Jump"))
            {
                verticalVelocity = jumpSpeed;
            }
        }
        else
        {
            verticalVelocity -= gravity * Time.deltaTime;
        }
        move.y = verticalVelocity;

        // Apply movement
        characterController.Move(move * Time.deltaTime);

        // ----- 2) Position the overhead (third-person) camera -----
        if (overheadCamera != null)
        {
            overheadCamera.position = transform.position + cameraOffset;
            // If you want the camera to always look down at the character, uncomment:
            // overheadCamera.LookAt(transform);
        }

        // ----- 3) Make the sprite always face the camera (billboard) -----
        if (spriteTransform != null && overheadCamera != null)
        {
            // Let the sprite “look at” the camera:
            spriteTransform.LookAt(overheadCamera.position);

            // If the sprite is flipped by default, rotate by 180 on Y:
            //spriteTransform.Rotate(0, 180, 0);
        }
    }
}
