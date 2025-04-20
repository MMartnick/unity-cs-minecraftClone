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
    public Transform overheadCamera;
    public Vector3 cameraOffset = new Vector3(0f, 35f, -15f);

    [Header("Sprite Settings")]
    public Transform spriteTransform;
    public Animator animator;
    public SpriteRenderer spriteRenderer;

    private CharacterController characterController;
    private float verticalVelocity = 0f;


    void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    void Update()
    {
        // 1) Read input
        float horizontal = CrossPlatformInputManager.GetAxis("Horizontal"); // A/D or Left/Right
        float vertical = CrossPlatformInputManager.GetAxis("Vertical");   // W/S or Up/Down

        // 2) Move in the XZ plane
        Vector3 move = new Vector3(horizontal, 0f, vertical) * moveSpeed;

        // Gravity & jumping
        if (characterController.isGrounded)
        {
            verticalVelocity = -0.5f;
            if (CrossPlatformInputManager.GetButtonDown("Jump"))
            {
                verticalVelocity = jumpSpeed;

                // Trigger jump animation if you want
                if (animator != null)
                {
                    animator.SetTrigger("Jump");
                }
            }
        }
        else
        {
            verticalVelocity -= gravity * Time.deltaTime;
        }

        // Apply Y velocity
        move.y = verticalVelocity;

        // Move the CharacterController
        characterController.Move(move * Time.deltaTime);

        // 3) Position overhead camera
        if (overheadCamera != null)
        {
            overheadCamera.position = transform.position + cameraOffset;
        }

        // 4) Billboard sprite to camera
        if (spriteTransform != null && overheadCamera != null)
        {
            spriteTransform.LookAt(overheadCamera.position);
        }

        // 5) Update Animator parameters
        if (animator != null)
        {
            // Pass raw movement axes to the animator
            animator.SetFloat("MoveX", horizontal);
            animator.SetFloat("MoveZ", vertical);

            // Check if we're moving or idle (based on speed)
            float speedValue = new Vector2(horizontal, vertical).magnitude;

            if(horizontal < 0)
            {
                spriteRenderer.flipX = true;
            }
            if(horizontal > 0)
            {
                spriteRenderer.flipX = false;
            }

            if(speedValue == 0.0f)
            {
                animator.SetBool("isIdle", true);
                animator.SetBool("isMoving", false);
            }
            else if(speedValue > 0.0f)
            {
                animator.SetBool("isMoving", true);
                animator.SetBool("isIdle", false);
            }

            
            

            // Also set IsGrounded
            animator.SetBool("IsGrounded", characterController.isGrounded);
        }
    }
}
