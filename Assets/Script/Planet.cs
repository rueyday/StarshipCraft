using UnityEngine;

// A round planet with procedural continents, ocean, atmosphere, a visible
// cloud shell marking the gravity band, and (optionally) an orbiting belt.
// Gravity uses the cloud-layer model — see GravityField. The cloud band sits
// at 35%–65% of the radius above the surface, with the cloud shell rendered
// mid-band so pilots can see where weightlessness begins.
public class Planet : MonoBehaviour
{
    public float Radius { get; private set; }

    Rigidbody beltRb;

    public static Planet Create(Vector3 center, float radius, int seed,
                                Color land, Color ocean, bool withBelt,
                                WeatherKind weather, string name)
    {
        var go = new GameObject(name);
        go.transform.position = center;
        var p = go.AddComponent<Planet>();
        p.Radius = radius;

        // Terrain (also the collider — mountains are real).
        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = MeshFactory.CreatePlanetMesh(seed, radius);
        go.AddComponent<MeshRenderer>().material = FX.Standard(land, Color.black, 0.05f, 0.3f);
        go.AddComponent<MeshCollider>().sharedMesh = mf.mesh;

        // Ocean: a smooth glossy sphere the valleys dip beneath.
        p.AddSphere("Ocean", radius * 0.985f,
            FX.Standard(ocean, ocean * 0.25f, 0.1f, 0.95f));

        // Atmosphere haze, and the cloud shell mid-way through the gravity band.
        p.AddSphere("Atmosphere", radius * 1.1f, FX.Ghost(new Color(0.4f, 0.7f, 1f, 0.08f)));
        p.AddSphere("Clouds", radius * 1.5f, FX.Ghost(new Color(1f, 1f, 1f, 0.13f)));

        GravityField.Sources.Add(new GravityField.Source
        {
            name = name,
            center = center,
            surfaceRadius = radius,
            g = 9f,
            cloudBottom = radius * 0.35f,
            cloudTop = radius * 0.65f,
            radarColor = new Color(0.45f, 0.65f, 1f),
            weather = weather,
        });

        if (withBelt) p.BuildBelt(seed);
        return p;
    }

    void AddSphere(string sphereName, float radius, Material mat)
    {
        var s = new GameObject(sphereName);
        s.transform.SetParent(transform, false);
        s.transform.localScale = Vector3.one * radius;
        s.AddComponent<MeshFilter>().mesh = MeshFactory.CreateSphereMesh();
        s.AddComponent<MeshRenderer>().material = mat;
    }

    // A tilted ring of big slow-orbiting rocks plus sparkling ring dust.
    // The ring sits above the cloud top (zero-g), so it never rains down.
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
            float r   = Radius * Random.Range(1.75f, 2.1f);
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

        FX.RingDust(belt.transform, Radius * 1.9f, Radius * 0.18f);
    }

    void FixedUpdate()
    {
        if (beltRb != null)
            beltRb.MoveRotation(beltRb.rotation *
                Quaternion.AngleAxis(0.4f * Time.fixedDeltaTime, transform.up));
    }
}
