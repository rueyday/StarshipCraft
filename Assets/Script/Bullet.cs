using UnityEngine;

public class Bullet : MonoBehaviour
{
    [SerializeField] float life = 5f;

    void Awake()
    {
        Destroy(gameObject, life);
    }

    void OnCollisionEnter(Collision collision)
    {
        // Only destroy asteroids; ignore ship parts and other objects
        if (collision.gameObject.GetComponent<Asteroid>() != null)
        {
            if (Space_Manager.Instance != null)
                Space_Manager.Instance.AddScore(10);

            Destroy(collision.gameObject);
            Destroy(gameObject);
        }
        else if (!collision.gameObject.CompareTag("Player"))
        {
            // Destroy bullet on any non-player impact, but don't destroy the object
            Destroy(gameObject);
        }
    }
}
