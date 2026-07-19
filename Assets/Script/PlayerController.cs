using UnityEngine;

// Reads input and feeds it to the Ship. W/S throttle, mouse pitch/yaw,
// Q/E roll, Shift boost, X brake, Space/LMB fire.
public class PlayerController : MonoBehaviour
{
    [SerializeField] float mouseSens = 0.55f;

    Ship ship;

    void Awake() => ship = GetComponent<Ship>();

    void Update()
    {
        // Map view and free cam borrow WASD/mouse — the ship just drifts.
        if (GameManager.Instance != null && GameManager.Instance.ShipInputSuspended)
        {
            ship.ThrustInput = 0f;
            ship.TorqueInput = Vector3.zero;
            ship.Boost = ship.Brake = ship.Turbo = false;
            return;
        }

        ship.ThrustInput = Input.GetAxis("Vertical");
        ship.Boost = Input.GetKey(KeyCode.LeftShift);
        ship.Brake = Input.GetKey(KeyCode.X);
        ship.Turbo = Input.GetKey(KeyCode.T);

        float pitch = Mathf.Clamp(-Input.GetAxis("Mouse Y") * mouseSens, -1f, 1f);
        float yaw   = Mathf.Clamp(Input.GetAxis("Mouse X") * mouseSens
                                  + Input.GetAxis("Horizontal") * 0.8f, -1f, 1f); // A/D turn too
        float roll  = Input.GetKey(KeyCode.Q) ? 1f : Input.GetKey(KeyCode.E) ? -1f : 0f;
        ship.TorqueInput = new Vector3(pitch, yaw, roll);

        if (Input.GetKeyDown(KeyCode.G)) ship.SetAnchored(!ship.Anchored);
        if (Input.GetKeyDown(KeyCode.F)) ship.SetArmorMode(!ship.ArmorMode);

        if (Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0))
            ship.TryFire();
    }
}
