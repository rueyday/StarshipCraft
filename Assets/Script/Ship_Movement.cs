using UnityEngine;

public class Ship_Movement : MonoBehaviour
{
    Rigidbody rb;

    [Header("Thrust")]
    [SerializeField] float forwardThrust = 20f;
    [SerializeField] float strafeThrust = 12f;
    [SerializeField] float boostMultiplier = 2.5f;
    [SerializeField] float maxSpeed = 40f;

    [Header("Rotation")]
    [SerializeField] float pitchSensitivity = 80f;
    [SerializeField] float yawSensitivity = 60f;
    [SerializeField] float rollSpeed = 60f;

    [Header("Braking")]
    [SerializeField] float brakeForce = 15f;

    // Optional: reference to a child for center of mass offset
    public GameObject Location_child;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.drag = 0.5f;
        rb.angularDrag = 3f;

        // Set center of mass from thruster positions if Location_child is assigned
        if (Location_child != null)
        {
            Object_Handler handler = Object_Handler.Instance;
            if (handler != null)
            {
                Vector3 com = Vector3.zero;
                int count = 0;
                for (int i = 0; i < handler.index; i++)
                {
                    if (handler.Data_Mode[i] == 200) // Thruster
                    {
                        com.x += handler.Data_X[i];
                        com.y += handler.Data_Y[i];
                        com.z += handler.Data_Z[i];
                        count++;
                    }
                }
                if (count > 0)
                    Location_child.transform.localPosition = com / count;
            }
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void FixedUpdate()
    {
        bool boost = Input.GetKey(KeyCode.LeftShift);
        float thrustScale = boost ? boostMultiplier : 1f;

        float forward = Input.GetAxis("Vertical");
        float strafe  = Input.GetAxis("Horizontal");

        // Forward / backward thrust
        Vector3 forceDir = transform.forward * forward * forwardThrust * thrustScale;
        // Lateral strafe (A/D)
        forceDir += transform.right * strafe * strafeThrust * thrustScale;

        rb.AddForce(forceDir, ForceMode.Force);

        // Hard brake (Space)
        if (Input.GetKey(KeyCode.Space))
        {
            Vector3 brakeVel = rb.velocity;
            rb.AddForce(-brakeVel.normalized * brakeForce, ForceMode.Force);
        }

        // Cap speed
        if (rb.velocity.magnitude > maxSpeed)
            rb.velocity = rb.velocity.normalized * maxSpeed;

        // Mouse pitch and yaw
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        rb.AddTorque(transform.up    *  mouseX * yawSensitivity   * Time.fixedDeltaTime, ForceMode.VelocityChange);
        rb.AddTorque(transform.right * -mouseY * pitchSensitivity  * Time.fixedDeltaTime, ForceMode.VelocityChange);

        // Roll with Q / E
        float roll = 0f;
        if (Input.GetKey(KeyCode.Q)) roll =  1f;
        if (Input.GetKey(KeyCode.E)) roll = -1f;
        rb.AddTorque(transform.forward * roll * rollSpeed * Time.fixedDeltaTime, ForceMode.VelocityChange);
    }

    void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
