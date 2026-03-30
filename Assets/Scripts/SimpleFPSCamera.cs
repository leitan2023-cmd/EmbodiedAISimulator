using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class SimpleFPSCamera : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float verticalSpeed = 3f;
    public float lookSpeed = 2f;

    private float rotX;
    private float rotY;
    private CharacterController characterController;

    private bool activated;

    private void Start()
    {
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
        {
            characterController = gameObject.AddComponent<CharacterController>();
        }

        characterController.radius = 0.3f;
        characterController.height = 1.7f;
        characterController.center = new Vector3(0f, 0.85f, 0f);
        characterController.minMoveDistance = 0f;

        Vector3 angles = transform.rotation.eulerAngles;
        rotX = angles.x;
        rotY = angles.y;
    }

    private void Update()
    {
        // Right-click to activate free camera; Escape to deactivate.
        // This prevents input conflicts with EmbodiedAgentController (WASD/QE).
        if (Input.GetMouseButtonDown(1))
        {
            activated = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            activated = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!activated) return;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float upDown = 0f;
        if (Input.GetKey(KeyCode.E))
        {
            upDown += 1f;
        }
        if (Input.GetKey(KeyCode.Q))
        {
            upDown -= 1f;
        }

        Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 planarRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 move = (planarRight * h + planarForward * v) * moveSpeed * Time.deltaTime;
        move += Vector3.up * upDown * verticalSpeed * Time.deltaTime;
        characterController.Move(move);

        rotX -= Input.GetAxis("Mouse Y") * lookSpeed;
        rotY += Input.GetAxis("Mouse X") * lookSpeed;
        rotX = Mathf.Clamp(rotX, -80f, 80f);
        transform.rotation = Quaternion.Euler(rotX, rotY, 0f);
    }
}
