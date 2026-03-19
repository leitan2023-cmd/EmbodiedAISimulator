using UnityEngine;

public class SimpleFPSCamera : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float verticalSpeed = 3f;
    public float lookSpeed = 2f;

    private float rotX;
    private float rotY;

    private void Start()
    {
        Vector3 angles = transform.rotation.eulerAngles;
        rotX = angles.x;
        rotY = angles.y;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
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

        Vector3 move = (transform.right * h + transform.forward * v) * moveSpeed * Time.deltaTime;
        move += Vector3.up * upDown * verticalSpeed * Time.deltaTime;
        transform.position += move;

        rotX -= Input.GetAxis("Mouse Y") * lookSpeed;
        rotY += Input.GetAxis("Mouse X") * lookSpeed;
        rotX = Mathf.Clamp(rotX, -80f, 80f);
        transform.rotation = Quaternion.Euler(rotX, rotY, 0f);

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
