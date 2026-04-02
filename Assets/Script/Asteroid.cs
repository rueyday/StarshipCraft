using UnityEngine;

// Add this component to asteroid GameObjects (Space_Manager adds it automatically at runtime).
// Gives each asteroid random spin, drift, and size variation,
// and deals damage to the player ship on collision.
public class Asteroid : MonoBehaviour
{
    [SerializeField] float minScale = 0.4f;
    [SerializeField] float maxScale = 3f;
    [SerializeField] float minSpin  = 5f;
    [SerializeField] float maxSpin  = 40f;
    [SerializeField] float minDrift = 0.5f;
    [SerializeField] float maxDrift = 4f;
    [SerializeField] int   damage   = 20;

    Vector3 rotationAxis;
    float   rotationSpeed;
    Vector3 driftVelocity;

    void Start()
    {
        // Random size
        float scale = Random.Range(minScale, maxScale);
        transform.localScale = Vector3.one * scale;

        // Damage scales with size (bigger = more painful)
        damage = Mathf.RoundToInt(damage * scale);

        // Random rotation axis and speed
        rotationAxis  = Random.onUnitSphere;
        rotationSpeed = Random.Range(minSpin, maxSpin);

        // Random slow drift in a random direction
        driftVelocity = Random.onUnitSphere * Random.Range(minDrift, maxDrift);
    }

    void Update()
    {
        transform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime, Space.World);
        transform.position += driftVelocity * Time.deltaTime;
    }

    void OnCollisionEnter(Collision collision)
    {
        // Damage the player
        if (collision.gameObject.CompareTag("Player"))
        {
            if (Space_Manager.Instance != null)
                Space_Manager.Instance.TakeDamage(damage);

            // Bounce off — give a little impulse to the asteroid for feel
            Vector3 pushDir = (transform.position - collision.transform.position).normalized;
            driftVelocity = pushDir * Random.Range(2f, 6f);
        }
    }
}
