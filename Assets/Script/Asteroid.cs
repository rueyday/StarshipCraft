using UnityEngine;

public class Asteroid : MonoBehaviour
{
    public enum AsteroidSize { Large, Medium, Small }
    public AsteroidSize Size;

    Rigidbody rb; // null for belt rocks (they ride the planet's kinematic ring)

    void Awake() => rb = GetComponent<Rigidbody>();

    void FixedUpdate()
    {
        // Free-floating rocks feel the cloud-layer gravity too.
        if (rb != null)
            rb.AddForce(GravityField.Sample(transform.position), ForceMode.Acceleration);
    }

    // scored=true when a player bullet did the damage.
    public void Hit(bool scored)
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnAsteroidDestroyed(this, scored);
        Destroy(gameObject);
    }

    void OnCollisionEnter(Collision col)
    {
        var body = col.collider.attachedRigidbody;
        var ship = body != null ? body.GetComponent<Ship>() : null;
        if (ship == null) return;

        // Gentle nudges bounce harmlessly — you can drift through the belt
        // with care. A real ram smashes a block and shatters the rock.
        if (col.relativeVelocity.magnitude < 6f) return;

        ship.TakeHit(col.GetContact(0).point);
        FX.Explosion(col.GetContact(0).point, new Color(0.9f, 0.7f, 0.4f), 0.7f);
        Hit(false);
    }
}
