using UnityEngine;
using UnityEngine.SceneManagement;

public class Gun : MonoBehaviour
{
    public Transform BulletSpawnPoint;
    public GameObject Prefab;

    [SerializeField] float bulletSpeed = 60f;
    [SerializeField] float fireRate = 0.25f; // seconds between shots

    bool active = false;
    float nextFireTime = 0f;

    void Start()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        active = (sceneName == "Galaxy");
    }

    void Update()
    {
        if (!active) return;

        bool fireInput = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0);
        if (fireInput && Time.time >= nextFireTime)
        {
            Fire();
            nextFireTime = Time.time + fireRate;
        }
    }

    void Fire()
    {
        if (Prefab == null || BulletSpawnPoint == null) return;

        GameObject bullet = Instantiate(Prefab, BulletSpawnPoint.position, BulletSpawnPoint.rotation);
        Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
        if (bulletRb != null)
        {
            // Inherit ship velocity so bullets don't fall behind
            Rigidbody shipRb = GetComponentInParent<Rigidbody>();
            Vector3 inheritedVelocity = shipRb != null ? shipRb.velocity : Vector3.zero;
            bulletRb.velocity = BulletSpawnPoint.forward * bulletSpeed + inheritedVelocity;
        }
    }
}
