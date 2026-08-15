using UnityEngine;

// The inside of a planet — its own scene, No Man's Sky-style. The ground is
// FLAT (no giant spheres to render), the weather owns the sky, and the world
// LOOPS: fly straight for LoopSize meters on X or Z and you're back where you
// started, which is how a flat map plays "round". Only one exists at a time;
// GameManager.EnterPlanet/ExitPlanet swap it in and out.
public class SurfaceWorld : MonoBehaviour
{
    public const float LoopSize = 3000f; // wrap period on X and Z
    public const float CloudTop = 650f;  // climb above this to return to space

    public Vector3 Center { get; private set; }

    public static SurfaceWorld Create(PlanetDef def, Vector3 center)
    {
        var go = new GameObject(def.name + " Surface");
        go.transform.position = center;
        var w = go.AddComponent<SurfaceWorld>();
        w.Center = center;

        // Ground slab: twice the loop span, so the edge is never visible from
        // inside the wrap zone (weather fog shortens sightlines well below
        // that anyway). 700 m thick — reads as bedrock, not a tile.
        var ground = new GameObject("Ground");
        ground.transform.SetParent(go.transform, false);
        ground.transform.localPosition = Vector3.down * 350f;
        ground.transform.localScale = new Vector3(LoopSize * 2f, 700f, LoopSize * 2f);
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
            regionRadius = LoopSize * 2f,
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
    // beneath you: dunes and ripple-scour on Titanhold, ice mounds and frozen
    // lakes on Vessa, moss fields, ponds and worn slabs on Korrath.
    void Decorate(PlanetDef def)
    {
        switch (def.weather)
        {
            case WeatherKind.Dust:
            {
                Material dune = FX.Standard(new Color(0.68f, 0.52f, 0.3f), Color.black, 0.05f, 0.3f);
                Material scour = FX.Standard(new Color(0.4f, 0.28f, 0.15f), Color.black, 0.05f, 0.25f);
                for (int i = 0; i < 34; i++)
                    Dome(RandPos(), Random.Range(60f, 150f), Random.Range(6f, 13f), dune);
                for (int i = 0; i < 16; i++)
                    Patch(RandPos(), Random.Range(50f, 110f), 0.4f, scour);
                break;
            }
            case WeatherKind.Snow:
            {
                Material mound = FX.Standard(new Color(0.9f, 0.93f, 0.97f), Color.black, 0.05f, 0.6f);
                Material ice = FX.Standard(new Color(0.65f, 0.8f, 0.95f),
                    new Color(0.1f, 0.18f, 0.28f), 0.1f, 0.97f);
                for (int i = 0; i < 26; i++)
                    Dome(RandPos(), Random.Range(35f, 90f), Random.Range(4f, 10f), mound);
                for (int i = 0; i < 9; i++)
                    Patch(RandPos(), Random.Range(80f, 190f), 0.3f, ice);
                break;
            }
            case WeatherKind.Cloud:
            {
                Material moss = FX.Standard(new Color(0.2f, 0.28f, 0.18f), Color.black, 0.05f, 0.3f);
                Material slab = FX.Standard(new Color(0.3f, 0.26f, 0.2f), Color.black, 0.1f, 0.35f);
                Material water = FX.Standard(new Color(0.1f, 0.3f, 0.55f),
                    new Color(0.02f, 0.08f, 0.16f), 0.1f, 0.95f);
                for (int i = 0; i < 18; i++)
                    Patch(RandPos(), Random.Range(60f, 140f), 0.35f, moss);
                for (int i = 0; i < 14; i++)
                    Dome(RandPos(), Random.Range(25f, 60f), Random.Range(3f, 8f), slab);
                for (int i = 0; i < 8; i++)
                    Patch(RandPos(), Random.Range(70f, 170f), 0.3f, water);
                break;
            }
        }
    }

    Vector3 RandPos() => Center + new Vector3(
        Random.Range(-0.95f, 0.95f) * LoopSize, 0f, Random.Range(-0.95f, 0.95f) * LoopSize);

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
        if (d.x >  LoopSize * 0.5f) shift.x = -LoopSize;
        if (d.x < -LoopSize * 0.5f) shift.x =  LoopSize;
        if (d.z >  LoopSize * 0.5f) shift.z = -LoopSize;
        if (d.z < -LoopSize * 0.5f) shift.z =  LoopSize;
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
