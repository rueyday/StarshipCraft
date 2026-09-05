using UnityEngine;

// One world = a globe you see out in space + a flat looping surface scene you
// get teleported into when you fly through its cloud shell (see SurfaceWorld
// and GameManager.EnterPlanet). This file holds the definition data and the
// space-side visual.
public class PlanetDef
{
    public string  name;
    public Vector3 spacePos;
    public float   radius;          // visual globe radius
    public Color   land, ocean, ground;
    public bool    hasOcean, hasBelt, hasRing;
    public WeatherKind weather;
    public Color   radarColor;
    public float   loopSize = 3000f; // surface scene wrap period — bigger world, longer lap

    // Fly inside this radius (the visible cloud shell) and you enter the world.
    public float EntryRadius => radius * 1.5f;
}

// The globe out in space. Purely a landmark you fly into — no gravity, no
// terrain collider — except Titanhold's spinning ring, which really bites.
public class SpacePlanet : MonoBehaviour
{
    public PlanetDef Def { get; private set; }

    const float RingHalfThick = 250f, RingSpinDeg = 8f;
    Transform ringRoot, globe, cloudShell;
    float ringInner, ringOuter, ringTick, spinDeg;
    Quaternion ringTilt;

    public static SpacePlanet Create(PlanetDef def)
    {
        var go = new GameObject(def.name);
        go.transform.position = def.spacePos;
        var p = go.AddComponent<SpacePlanet>();
        p.Def = def;

        // Terrain globe (Perlin mountains) on its own child so it can slowly
        // rotate; optional ocean poking through the valleys.
        var globe = new GameObject("Globe");
        globe.transform.SetParent(go.transform, false);
        globe.AddComponent<MeshFilter>().mesh =
            MeshFactory.CreatePlanetMesh(def.name.GetHashCode() & 0xFFF, def.radius);
        globe.AddComponent<MeshRenderer>().material = FX.Standard(def.land, Color.black, 0.05f, 0.3f);
        p.globe = globe.transform;
        p.spinDeg = Mathf.Clamp(1.5f * 700f / def.radius, 0.3f, 2f);
        if (def.hasOcean)
            p.AddSphere("Ocean", def.radius * 0.985f,
                FX.Standard(def.ocean, def.ocean * 0.25f, 0.1f, 0.95f));

        p.AddSphere("Atmosphere", def.radius * 1.1f, FX.Ghost(new Color(0.4f, 0.7f, 1f, 0.08f)));

        // The cloud shell IS the door: fly through it to enter the world.
        Color shell = def.weather == WeatherKind.Dust  ? new Color(0.9f, 0.75f, 0.5f, 0.16f)
                    : def.weather == WeatherKind.Snow  ? new Color(0.85f, 0.9f, 1f, 0.15f)
                    : def.weather == WeatherKind.Ember ? new Color(1f, 0.5f, 0.3f, 0.17f)
                    : new Color(1f, 1f, 1f, 0.14f);
        p.cloudShell = p.AddSphere("CloudShell", def.EntryRadius, FX.Ghost(shell));

        if (def.hasBelt) p.BuildBelt();
        if (def.hasRing) p.BuildRing();
        return p;
    }

    Transform AddSphere(string sphereName, float radius, Material mat)
    {
        var s = new GameObject(sphereName);
        s.transform.SetParent(transform, false);
        s.transform.localScale = Vector3.one * radius;
        s.AddComponent<MeshFilter>().mesh = MeshFactory.CreateSphereMesh();
        s.AddComponent<MeshRenderer>().material = mat;
        return s.transform;
    }

    // Tilted ring of slow rocks + dust (Korrath). Sits outside the cloud
    // shell, so you can thread it without triggering entry.
    void BuildBelt()
    {
        var belt = new GameObject("Belt");
        belt.transform.SetParent(transform, false);
        belt.transform.localRotation = Quaternion.Euler(24f, 0f, 0f);
        var beltRb = belt.AddComponent<Rigidbody>();
        beltRb.isKinematic = true;

        var rockMat = FX.Standard(new Color(0.45f, 0.4f, 0.38f), Color.black, 0.1f, 0.35f);
        for (int i = 0; i < 70; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float r   = Def.radius * Random.Range(1.75f, 2.1f);
            float size = Random.Range(2.5f, 8f);
            var rock = new GameObject("BeltRock");
            rock.transform.SetParent(belt.transform, false);
            rock.transform.localPosition = new Vector3(
                Mathf.Cos(ang) * r, Def.radius * Random.Range(-0.05f, 0.05f), Mathf.Sin(ang) * r);
            rock.transform.localRotation = Random.rotation;
            rock.AddComponent<MeshFilter>().mesh =
                MeshFactory.CreateAsteroidMesh(Random.Range(0, 9999), size);
            rock.AddComponent<MeshRenderer>().material = rockMat;
            rock.AddComponent<SphereCollider>().radius = size * 0.85f;
            rock.AddComponent<Asteroid>().Size = Asteroid.AsteroidSize.Large;
        }
        FX.RingDust(belt.transform, Def.radius * 1.9f, Def.radius * 0.18f);
    }

    // The Saturn ring (Titanhold): four golden bands + racing debris, in the
    // plane of the globe's equator, spinning. Inside the band = a block per tick.
    void BuildRing()
    {
        ringInner = Def.radius * 2.6f;
        ringOuter = Def.radius * 3.4f;
        ringTilt  = Quaternion.Euler(10f, 0f, 0f);

        var ring = new GameObject("Ring");
        ring.transform.SetParent(transform, false);
        ring.transform.localRotation = ringTilt;
        ringRoot = ring.transform;

        float span = ringOuter - ringInner;
        Color[] a =
        {
            new Color(1f, 0.85f, 0.5f, 0.6f),  new Color(0.95f, 0.9f, 0.75f, 0.5f),
            new Color(0.9f, 0.7f, 0.4f, 0.55f), new Color(1f, 0.95f, 0.85f, 0.4f),
        };
        for (int i = 0; i < 4; i++)
        {
            float r = ringInner + span * (0.12f + 0.25f * i);
            FX.RingDust(ring.transform, r, span * 0.1f, 2200, 55f,
                a[i], new Color(a[i].r, a[i].g * 0.9f, a[i].b * 0.7f, a[i].a * 0.6f));
        }

        var rockMatRing = FX.Standard(new Color(0.8f, 0.65f, 0.4f), Color.black, 0.1f, 0.4f);
        for (int i = 0; i < 60; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float r   = Mathf.Lerp(ringInner, ringOuter, Random.value);
            float size = Random.Range(18f, 55f);
            var rock = new GameObject("RingRock");
            rock.transform.SetParent(ring.transform, false);
            rock.transform.localPosition = new Vector3(
                Mathf.Cos(ang) * r, Random.Range(-120f, 120f), Mathf.Sin(ang) * r);
            rock.transform.localRotation = Random.rotation;
            rock.AddComponent<MeshFilter>().mesh =
                MeshFactory.CreateAsteroidMesh(Random.Range(0, 9999), size);
            rock.AddComponent<MeshRenderer>().material = rockMatRing;
        }
    }

    public bool InRing(Vector3 pos)
    {
        if (ringRoot == null) return false;
        Vector3 local = Quaternion.Inverse(ringTilt) * (pos - transform.position);
        float radial = new Vector2(local.x, local.z).magnitude;
        return radial > ringInner && radial < ringOuter && Mathf.Abs(local.y) < RingHalfThick;
    }

    void Update()
    {
        // Worlds are alive: terrain turns slowly, clouds drift against it.
        if (globe != null) globe.Rotate(Vector3.up * spinDeg * Time.deltaTime, Space.Self);
        if (cloudShell != null) cloudShell.Rotate(Vector3.up * -spinDeg * 0.6f * Time.deltaTime, Space.Self);

        if (ringRoot == null) return;
        ringRoot.Rotate(Vector3.up * RingSpinDeg * Time.deltaTime, Space.Self);

        var gm = GameManager.Instance;
        if (gm == null) return;
        ringTick -= Time.deltaTime;
        if (ringTick > 0f) return;
        ringTick = 0.45f;
        for (int i = gm.Ships.Count - 1; i >= 0; i--)
        {
            var s = gm.Ships[i];
            if (s == null || !InRing(s.transform.position)) continue;
            Vector3 hit = s.transform.position + Random.onUnitSphere * 2f;
            FX.Impact(hit, new Color(1f, 0.85f, 0.5f));
            s.TakeHit(hit);
        }
    }
}
