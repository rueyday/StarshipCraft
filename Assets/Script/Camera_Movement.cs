using UnityEngine;

// Orbit camera for the Builder scene.
// Attach this to the camera or a controller object.
// Drag or arrow-key to orbit, scroll wheel to zoom.
public class Camera_Movement : MonoBehaviour
{
    [SerializeField] private Camera cam;

    [Header("Orbit")]
    [SerializeField] float orbitSpeed = 120f;       // degrees per second (arrow keys)
    [SerializeField] float mouseSensitivity = 3f;   // degrees per pixel (drag)

    [Header("Zoom")]
    [SerializeField] float distance = 10f;
    [SerializeField] float minDistance = 3f;
    [SerializeField] float maxDistance = 30f;
    [SerializeField] float zoomSpeed = 4f;

    float yaw   = 30f;   // horizontal angle
    float pitch = 20f;   // vertical angle

    void Start()
    {
        if (cam == null)
            cam = Camera.main;
    }

    void Update()
    {
        // ---- Arrow key orbit ----
        if (Input.GetKey(KeyCode.LeftArrow))  yaw   -= orbitSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.RightArrow)) yaw   += orbitSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.UpArrow))    pitch -= orbitSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.DownArrow))  pitch += orbitSpeed * Time.deltaTime;

        // ---- Mouse drag orbit (hold left mouse button) ----
        if (Input.GetMouseButton(0))
        {
            yaw   += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        }

        // Clamp vertical angle to avoid flipping
        pitch = Mathf.Clamp(pitch, -80f, 80f);

        // ---- Scroll wheel zoom ----
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        distance -= scroll * zoomSpeed;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        // ---- Apply orbit around origin (0,0,0) ----
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 position = rotation * new Vector3(0f, 0f, -distance);

        cam.transform.position = position;
        cam.transform.rotation = rotation;
    }
}
