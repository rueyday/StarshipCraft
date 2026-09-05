using UnityEngine;

// The inside of a planet — its own scene, No Man's Sky-style. The ground is
// FLAT (no giant spheres to render), the weather owns the sky, and the world
// LOOPS: fly straight for the world's loop length on X or Z and you're back
// where you started, which is how a flat map plays "round". Loop length comes
// from PlanetDef — bigger worlds are genuinely longer laps. Only one exists
// at a time; GameManager.EnterPlanet/ExitPlanet swap it in and out.
public class SurfaceWorld : MonoBehaviour
{
    public const float CloudTop = 650f; // climb above this to return to space

    public Vector3 Center { get; private set; }
    public float Loop { get; private set; } // wrap period on X and Z

    public static SurfaceWorld Create(PlanetDef def, Vector3 center)
    {
        var go = new GameObject(def.name + " Surface");
        go.transform.position = center;
        var w = go.AddComponent<SurfaceWorld>();
        w.Center = center;
        w.Loop = Mathf.Max(2000f, def.loopSize);

        // Ground slab: twice the loop span, so the edge is never visible from
        // inside the wrap zone (weather fog shortens sightlines well below
        // that anyway). 700 m thick — reads as bedrock, not a tile.
        var ground = new GameObject("Ground");
        ground.transform.SetParent(go.transform, false);
        ground.transform.localPosition = Vector3.down * 350f;
        ground.transform.localScale = new Vector3(w.Loop * 2f, 700f, w.Loop * 2f);
        ground.AddComponent<MeshFilter>().mesh = MeshFactory.CubeMesh();
        ground.AddComponent<MeshRenderer>().material =
            FX.Standard(def.ground, Color.black, 0.05f, 0.25f);
        ground.AddComponent<BoxCollider>();

        w.Decorate(def);

        GravityField.Sources.Add(new GravityField.Source
        {
            name = def.name,
            center = center,
            flat = true,
            groundY = center.y,
            regionRadius = w.Loop * 2f,
            g = 11f,
            cloudBottom = 350f,
            cloudTop = CloudTop,
            radarColor = def.radarColor,
            weather = def.weather,
        });
        return w;
    }

    // Low-profile ground dressing per world — nothing tall enough to be an
    // obstacle, just enough that the surface reads as a real place scrolling
    // beneath you. Counts scale with the loop length so bigger worlds stay
    // as dense as small ones.
    void Decorate(PlanetDef def)
    {
        float k = Mathf.Clamp(Loop / 3000f, 0.7f, 3f);
        switch (def.weather)
        {
            case WeatherKind.Dust: // Titanhold: dunes and wind-scour
            {
                Material dune = FX.Standard(new Color(0.68f, 0.52f, 0.3f), Color.black, 0.05f, 0.3f);
                Material scour = FX.Standard(new Color(0.4f, 0.28f, 0.15f), Color.black, 0.05f, 0.25f);
                for (int i = 0; i < 34 * k; i++)
                    Dome(RandPos(), Random.Range(60f, 150f), Random.Range(6f, 13f), dune);
                for (int i = 0; i < 16 * k; i++)
                    Patch(RandPos(), Random.Range(50f, 110f), 0.4f, scour);
                break;
            }
            case WeatherKind.Snow: // Vessa: ice mounds and frozen lakes
            {
                Material mound = FX.Standard(new Color(0.9f, 0.93f, 0.97f), Color.black, 0.05f, 0.6f);
                Material ice = FX.Standard(new Color(0.65f, 0.8f, 0.95f),
                    new Color(0.1f, 0.18f, 0.28f), 0.1f, 0.97f);
                for (int i = 0; i < 26 * k; i++)
                    Dome(RandPos(), Random.Range(35f, 90f), Random.Range(4f, 10f), mound);
                for (int i = 0; i < 9 * k; i++)
                    Patch(RandPos(), Random.Range(80f, 190f), 0.3f, ice);
                break;
            }
            case WeatherKind.Cloud: // Korrath: moss fields, ponds, worn slabs
            {
                Material moss = FX.Standard(new Color(0.2f, 0.28f, 0.18f), Color.black, 0.05f, 0.3f);
                Material slab = FX.Standard(new Color(0.3f, 0.26f, 0.2f), Color.black, 0.1f, 0.35f);
                Material water = FX.Standard(new Color(0.1f, 0.3f, 0.55f),
                    new Color(0.02f, 0.08f, 0.16f), 0.1f, 0.95f);
                for (int i = 0; i < 18 * k; i++)
                    Patch(RandPos(), Random.Range(60f, 140f), 0.35f, moss);
                for (int i = 0; i < 14 * k; i++)
                    Dome(RandPos(), Random.Range(25f, 60f), Random.Range(3f, 8f), slab);
                for (int i = 0; i < 8 * k; i++)
                    Patch(RandPos(), Random.Range(70f, 170f), 0.3f, water);
                break;
            }
            case WeatherKind.Ember: // Emberfall: cinder heaps and glowing lava pools
            {
                Material cinder = FX.Standard(new Color(0.16f, 0.12f, 0.11f), Color.black, 0.1f, 0.25f);
                Material lava = FX.Standard(new Color(0.25f, 0.08f, 0.03f),
                    new Color(1f, 0.38f, 0.08f) * 1.8f, 0f, 0.85f);
                Material crust = FX.Standard(new Color(0.3f, 0.16f, 0.1f),
                    new Color(1f, 0.3f, 0.05f) * 0.25f, 0.05f, 0.3f);
                for (int i = 0; i < 24 * k; i++)
                    Dome(RandPos(), Random.Range(40f, 100f), Random.Range(5f, 12f), cinder);
                for (int i = 0; i < 10 * k; i++)
                    Patch(RandPos(), Random.Range(60f, 150f), 0.35f, lava);
                for (int i = 0; i < 12 * k; i++)
                    Patch(RandPos(), Random.Range(40f, 90f), 0.5f, crust);
                break;
            }
        }
    }

    Vector3 RandPos() => Center + new Vector3(
        Random.Range(-0.95f, 0.95f) * Loop, 0f, Random.Range(-0.95f, 0.95f) * Loop);

    void Dome(Vector3 pos, float width, float height, Material mat)
    {
        var d = new GameObject("Dome");
        d.transform.SetParent(transform, true);
        d.transform.position = pos;
        d.transform.localScale = new Vector3(width, height, width * Random.Range(0.7f, 1.3f));
        d.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        d.AddComponent<MeshFilter>().mesh = MeshFactory.CreateSphereMesh();
        d.AddComponent<MeshRenderer>().material = mat;
    }

    void Patch(Vector3 pos, float width, float height, Material mat)
    {
        var d = new GameObject("Patch");
        d.transform.SetParent(transform, true);
        d.transform.position = pos + Vector3.up * height * 0.5f;
        d.transform.localScale = new Vector3(width, height, width * Random.Range(0.6f, 1.4f));
        d.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        d.AddComponent<MeshFilter>().mesh = MeshFactory.CubeMesh();
        d.AddComponent<MeshRenderer>().material = mat;
    }

    // How far a position sits outside the wrap zone, as the teleport shift
    // that brings it back in on the far side (zero when inside).
    public Vector3 WrapDelta(Vector3 pos)
    {
        Vector3 d = pos - Center;
        Vector3 shift = Vector3.zero;
        if (d.x >  Loop * 0.5f) shift.x = -Loop;
        if (d.x < -Loop * 0.5f) shift.x =  Loop;
        if (d.z >  Loop * 0.5f) shift.z = -Loop;
        if (d.z < -Loop * 0.5f) shift.z =  Loop;
        return shift;
    }

    void FixedUpdate()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        foreach (var ship in gm.Ships)
        {
            if (ship == null) continue;
            Vector3 shift = WrapDelta(ship.transform.position);
            if (shift == Vector3.zero) continue;
            ship.transform.position += shift;
            if (ship.Body != null && !ship.Body.isKinematic) ship.Body.position += shift;
            // Shift the camera too so the player never sees the seam.
            if (ship == gm.PlayerShip && Camera.main != null)
                Camera.main.transform.position += shift;
        }
    }
}
