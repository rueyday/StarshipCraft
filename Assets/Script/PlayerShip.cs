using UnityEngine;

public class PlayerShip : MonoBehaviour
{
    [SerializeField] float forwardThrust = 25f;
    [SerializeField] float strafeThrust  = 15f;
    [SerializeField] float boostMult     = 2.5f;
    [SerializeField] float maxSpeed      = 50f;
    [SerializeField] float brakeForce    = 20f;
    [SerializeField] float pitchSens     = 80f;
    [SerializeField] float yawSens       = 60f;
    [SerializeField] float rollSpeed     = 60f;
    [SerializeField] float bulletSpeed   = 80f;
    [SerializeField] float fireRate      = 0.18f;

    Rigidbody  rb;
    Renderer   rend;
    float      nextFireTime;
    bool       invincible;
    float      invincibleTimer;
    float      flashTick;

    void Awake()
    {
        rb   = GetComponent<Rigidbody>();
        rend = GetComponent<Renderer>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void Update()
    {
        if (invincible) TickInvincible();
        HandleShooting();
    }

    void FixedUpdate()
    {
        bool boost = Input.GetKey(KeyCode.LeftShift);
        float sc   = boost ? boostMult : 1f;

        Vector3 force = transform.forward * Input.GetAxis("Vertical")   * forwardThrust * sc
                      + transform.right   * Input.GetAxis("Horizontal") * strafeThrust  * sc;
        rb.AddForce(force, ForceMode.Force);

        if (Input.GetKey(KeyCode.X))
            rb.AddForce(-rb.velocity.normalized * brakeForce, ForceMode.Force);

        if (rb.velocity.magnitude > maxSpeed)
            rb.velocity = rb.velocity.normalized * maxSpeed;

        rb.AddTorque(transform.up    *  Input.GetAxis("Mouse X") * yawSens   * Time.fixedDeltaTime, ForceMode.VelocityChange);
        rb.AddTorque(transform.right * -Input.GetAxis("Mouse Y") * pitchSens * Time.fixedDeltaTime, ForceMode.VelocityChange);

        float roll = Input.GetKey(KeyCode.Q) ? 1f : Input.GetKey(KeyCode.E) ? -1f : 0f;
        rb.AddTorque(transform.forward * roll * rollSpeed * Time.fixedDeltaTime, ForceMode.VelocityChange);
    }

    void HandleShooting()
    {
        if ((Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0)) && Time.time >= nextFireTime)
        {
            Fire();
            nextFireTime = Time.time + fireRate;
        }
    }

    void Fire()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Bullet";
        go.transform.position   = transform.position + transform.forward * 2.1f;
        go.transform.localScale = Vector3.one * 0.25f;

        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(1f, 0.9f, 0.2f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(1f, 0.7f, 0f) * 3f);
        go.GetComponent<Renderer>().material = mat;

        // Replace the primitive's collider with a smaller one
        Destroy(go.GetComponent<SphereCollider>());
        var col = go.AddComponent<SphereCollider>();
        col.radius = 0.15f;

        var bRb = go.AddComponent<Rigidbody>();
        bRb.useGravity             = false;
        bRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        bRb.velocity               = transform.forward * bulletSpeed + rb.velocity;

        go.AddComponent<Bullet>();
    }

    public void BeginInvincible(float duration)
    {
        invincible      = true;
        invincibleTimer = duration;
        if (TryGetComponent<Collider>(out var c)) c.enabled = false;
    }

    void TickInvincible()
    {
        invincibleTimer -= Time.deltaTime;
        flashTick       += Time.deltaTime;
        if (rend != null) rend.enabled = Mathf.Sin(flashTick * 18f) > 0f;

        if (invincibleTimer <= 0f)
        {
            invincible = false;
            if (rend != null) rend.enabled = true;
            if (TryGetComponent<Collider>(out var c)) c.enabled = true;
        }
    }

    void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }
}
