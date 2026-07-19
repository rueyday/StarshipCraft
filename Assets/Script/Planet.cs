using UnityEngine;

// A giant planet with a real gravity well, procedural continents and oceans,
// a hazy atmosphere, and an orbiting asteroid belt. Fly down and land — gently.
// Everything inside 3 planet radii feels inverse-square gravity, so heavy
// ships with weak engines can genuinely fail to climb back out.
public class Planet : MonoBehaviour
{
    public static Planet Instance { get; private set; }

    public float Radius { get; private set; }

    // Gravity is tuned against the ship speed cap, not hand-picked:
    //  - circular orbit skimming the surface takes ~70% of max speed
    //    (v_orbit² = g·R  →  g = (0.7·Max)²/R)
    //  - the well ends at CutoffRadii, chosen so breaking out from the
    //    surface takes ~90% of max speed flown tangent to the ground
    //    (v_esc² = 2gR(1 − 1/c)  →  c ≈ 5.76 for v_esc = 0.9·Max)
    public const float OrbitSpeed  = 0.7f * Ship.MaxSpeed;
    public const float CutoffRadii = 5.76f;
    public float SurfaceGravity => OrbitSpeed * OrbitSpeed / Radius;

    Rigidbody beltRb;

    public static Planet Create(Vector3 center, float radius, int seed = 1234)
    {
        var go = new GameObject("Planet");
        go.transform.position = center;
        var p = go.AddComponent<Planet>();
        p.Radius = radius;
        Instance = p;

        // Terrain (also the collider — mountains are real).
        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = MeshFactory.CreatePlanetMesh(seed, radius);
        go.AddComponent<MeshRenderer>().material =
            FX.Standard(new Color(0.4f, 0.34f, 0.26f), Color.black, 0.05f, 0.3f);
        go.AddComponent<MeshCollider>().sharedMesh = mf.mesh;

        // Ocean: a smooth glossy sphere the valleys dip beneath.
        p.AddSphere("Ocean", radius * 0.985f,
            FX.Standard(new Color(0.08f, 0.28f, 0.5f), new Color(0.02f, 0.08f, 0.16f), 0.1f, 0.95f));

        // Atmosphere haze.
        p.AddSphere("Atmosphere", radius * 1.07f, FX.Ghost(new Color(0.4f, 0.7f, 1f, 0.1f)));

        p.BuildBelt(seed);
        return p;
    }

    void AddSphere(string name, float radius, Material mat)
    {
        var s = new GameObject(name);
        s.transform.SetParent(transform, false);
        s.transform.localScale = Vector3.one * radius;
        s.AddComponent<MeshFilter>().mesh = MeshFactory.CreateSphereMesh();
        s.AddComponent<MeshRenderer>().material = mat;
    }

    // A tilted ring of big slow-orbiting rocks plus sparkling ring dust.
    // The whole ring is one kinematic body rotated in FixedUpdate.
    void BuildBelt(int seed)
    {
        var belt = new GameObject("Belt");
        belt.transform.SetParent(transform, false);
        belt.transform.localRotation = Quaternion.Euler(24f, 0f, 0f);
        beltRb = belt.AddComponent<Rigidbody>();
        beltRb.isKinematic = true;

        Random.State prev = Random.state;
        Random.InitState(seed);
        var rockMat = FX.Standard(new Color(0.45f, 0.4f, 0.38f), Color.black, 0.1f, 0.35f);
        for (int i = 0; i < 70; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float r   = Radius * Random.Range(1.4f, 1.8f);
            float y   = Radius * Random.Range(-0.05f, 0.05f);
            float size = Random.Range(2.5f, 8f);

            var rock = new GameObject("BeltRock");
            rock.transform.SetParent(belt.transform, false);
            rock.transform.localPosition = new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
            rock.transform.localRotation = Random.rotation;
            rock.AddComponent<MeshFilter>().mesh =
                MeshFactory.CreateAsteroidMesh(Random.Range(0, 9999), size);
            rock.AddComponent<MeshRenderer>().material = rockMat;
            rock.AddComponent<SphereCollider>().radius = size * 0.85f;
            rock.AddComponent<Asteroid>().Size = Asteroid.AsteroidSize.Large;
        }
        Random.state = prev;

        FX.RingDust(belt.transform, Radius * 1.6f, Radius * 0.2f);
    }

    void FixedUpdate()
    {
        if (beltRb != null)
            beltRb.MoveRotation(beltRb.rotation *
                Quaternion.AngleAxis(0.4f * Time.fixedDeltaTime, transform.up));
    }

    // Inverse-square pull toward the planet center, zero beyond the cutoff.
    public Vector3 GravityAccel(Vector3 pos)
    {
        Vector3 d = transform.position - pos;
        float r = d.magnitude;
        if (r > Radius * CutoffRadii || r < 0.001f) return Vector3.zero;
        r = Mathf.Max(r, Radius * 0.5f); // sane cap if something clips inside
        return d.normalized * (SurfaceGravity * Radius * Radius / (r * r));
    }
}
