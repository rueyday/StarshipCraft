using UnityEngine;

public class Asteroid : MonoBehaviour
{
    public enum AsteroidSize { Large, Medium, Small }
    public AsteroidSize Size;

    // Called by Bullet on hit
    public void Hit()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnAsteroidDestroyed(this);
        Destroy(gameObject);
    }

    void OnCollisionEnter(Collision col)
    {
        if (col.gameObject.CompareTag("Player") && GameManager.Instance != null)
            GameManager.Instance.OnPlayerHit();
    }
}
