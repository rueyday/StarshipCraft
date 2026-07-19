using UnityEngine;

// Glowing energy bolt with a light trail. Damages asteroids and ships of
// other factions; passes are prevented by simple faction checks, not layers.
public class Bullet : MonoBehaviour
{
    public Faction faction;

    public static Bullet Spawn(Vector3 pos, Vector3 velocity, Faction faction, Collider owner = null)
    {
        var go = new GameObject("Bullet");
        go.transform.position = pos;

        Color c = FX.Accent(faction);
        go.AddComponent<MeshFilter>().mesh = MeshFactory.CubeMesh();
        go.transform.localScale = new Vector3(0.12f, 0.12f, 0.6f);
        go.transform.rotation = Quaternion.LookRotation(velocity);
        go.AddComponent<MeshRenderer>().material =
            FX.Standard(Color.white, c * 3f, 0f, 0.5f);

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = 0.18f;
        trail.endWidth = 0f;
        trail.material = FX.ParticleMat();
        trail.startColor = c;
        trail.endColor = new Color(c.r, c.g, c.b, 0f);

        var col = go.AddComponent<SphereCollider>();
        col.radius = 0.15f;
        if (owner != null) Physics.IgnoreCollision(col, owner);

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.velocity = velocity;

        var b = go.AddComponent<Bullet>();
        b.faction = faction;
        return b;
    }

    void Awake() => Destroy(gameObject, 3f);

    void OnCollisionEnter(Collision col)
    {
        var other = col.collider.attachedRigidbody;

        // Check the collider itself first: belt rocks share the ring's
        // kinematic rigidbody, so the Asteroid lives on the collider's object.
        var ast = col.collider.GetComponent<Asteroid>();
        if (ast == null && other != null) ast = other.GetComponent<Asteroid>();
        if (ast != null)
        {
            FX.Impact(transform.position, new Color(1f, 0.8f, 0.5f));
            ast.Hit(faction == Faction.Player);
            Destroy(gameObject);
            return;
        }

        var ship = other != null ? other.GetComponent<Ship>() : null;
        if (ship != null)
        {
            if (ship.faction == faction) { Destroy(gameObject); return; }
            ship.TakeHit(col.GetContact(0).point);
            Destroy(gameObject);
            return;
        }

        Destroy(gameObject);
    }
}
