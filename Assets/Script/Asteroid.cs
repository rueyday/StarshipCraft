using UnityEngine;

public class Asteroid : MonoBehaviour
{
    public enum AsteroidSize { Large, Medium, Small }
    public AsteroidSize Size;

    // scored=true when a player bullet did the damage.
    public void Hit(bool scored)
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnAsteroidDestroyed(this, scored);
        Destroy(gameObject);
    }

    void OnCollisionEnter(Collision col)
    {
        var rb = col.collider.attachedRigidbody;
        var ship = rb != null ? rb.GetComponent<Ship>() : null;
        if (ship != null)
        {
            // Rock smashes a block off the ship and shatters itself.
            ship.TakeHit(col.GetContact(0).point);
            FX.Explosion(col.GetContact(0).point, new Color(0.9f, 0.7f, 0.4f), 0.7f);
            Hit(false);
        }
    }
}
