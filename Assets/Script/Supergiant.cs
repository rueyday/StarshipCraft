using System.Collections.Generic;
using UnityEngine;

// Titanhold — the Saturn of this system. A brown-and-gold super-giant so big
// the surface reads as flat: streamed chunks of pillars, canyon walls and
// loose rocks (freshly random every visit), ground-level dust storms that
// haze your vision, and a gorgeous banded ring that will strip a careless
// ship to its core. Constant gravity below the cloud deck (see GravityField).
public class Supergiant : MonoBehaviour
{
    public static Supergiant Instance { get; private set; }

    public Vector3 Center { get; private set; }
    public float GroundY { get; private set; }
    public float RegionRadius { get; private set; }

    const float ChunkSize = 220f;
    const int   ViewChunks = 2; // 5×5 grid around the player

    // The ring: a vast tilted band wrapping the globe 4 km below the play
    // cap, with a Saturn-style gap off the sphere's flank. Beautiful from
    // afar; inside the band, ships take a block every tick.
    const float RingDepth = 4000f, RingHalfThick = 250f;
    float ringInner, ringOuter;
    Quaternion ringTilt;
    float ringTick;

    readonly Dictionary<Vector2Int, GameObject> chunks = new Dictionary<Vector2Int, GameObject>();
    Material groundMat;

    public static Supergiant Create(Vector3 center, float regionRadius, string name)
    {
        var go = new GameObject(name);
        go.transform.position = center;
        var p = go.AddComponent<Supergiant>();
        Instance = p;
        p.Center = center;
        p.GroundY = center.y;
        p.RegionRadius = regionRadius;

        // Saturn palette: dark caramel ground under a butterscotch globe.
        p.groundMat = FX.Standard(new Color(0.55f, 0.4f, 0.22f), Color.black, 0.05f, 0.25f);

        GravityField.Sources.Add(new GravityField.Source
        {
            name = name,
            center = center,
            flat = true,
            groundY = center.y,
            regionRadius = regionRadius,
            g = 11f,
            cloudBottom = 350f,
            cloudTop = 650f,
            radarColor = new Color(1f, 0.75f, 0.3f),
            weather = WeatherKind.Dust,
        });

        // The planet itself: a colossal butterscotch sphere. The playable flat
        // region rides its top cap — at 30 km radius the curvature under the
        // streamed terrain is a few dozen meters, invisible from the canyons,
        // while from space Titanhold finally reads as a giant globe.
        const float bodyR = 30000f;
        var body = new GameObject("PlanetBody");
        body.transform.SetParent(go.transform, false);
        body.transform.localPosition = Vector3.down * (bodyR + 10f);
        body.transform.localScale = Vector3.one * bodyR;
        body.AddComponent<MeshFilter>().mesh = MeshFactory.CreateSphereMesh();
        body.AddComponent<MeshRenderer>().material =
            FX.Standard(new Color(0.62f, 0.45f, 0.22f), Color.black, 0.05f, 0.35f);
        body.AddComponent<MeshCollider>().sharedMesh = MeshFactory.CreateSphereMesh();

        var skirt = new GameObject("Haze");
        skirt.transform.SetParent(go.transform, false);
        skirt.transform.localPosition = Vector3.up * 150f;
        skirt.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        skirt.AddComponent<MeshFilter>().mesh =
            MeshFactory.CreateCone(regionRadius * 1.05f, regionRadius * 1.05f, 300f, 48);
        skirt.AddComponent<MeshRenderer>().material = FX.Ghost(new Color(0.95f, 0.7f, 0.35f, 0.08f));

        // Cloud decks tinted sandy.
        p.AddCloudDisc(360f, 0.1f);
        p.AddCloudDisc(640f, 0.14f);

        p.BuildRing();
        return p;
    }

    void AddCloudDisc(float altitude, float alpha)
    {
        var d = new GameObject("CloudDeck");
        d.transform.SetParent(transform, false);
        d.transform.localPosition = Vector3.up * altitude;
        d.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        d.AddComponent<MeshFilter>().mesh = MeshFactory.CreateCone(RegionRadius, RegionRadius, 4f);
        d.AddComponent<MeshRenderer>().material = FX.Ghost(new Color(0.95f, 0.85f, 0.65f, alpha));
    }

    // Four sparkling bands of gold and cream wrapping the globe, tilted 10°,
    // and the whole disc SPINS — mid-ring debris streams past at over 2 km/s,
    // which is exactly why touching it shreds a ship.
    Transform ringRoot;
    const float RingSpinDeg = 8f;

    void BuildRing()
    {
        // Inner edge clears the sphere's flank at ring depth (~15 km radial
        // for the 30 km globe) leaving a visible Saturn gap.
        ringInner = RegionRadius * 2.75f;
        ringOuter = RegionRadius * 3.5f;
        ringTilt  = Quaternion.Euler(10f, 0f, 0f);

        var ring = new GameObject("Ring");
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = Vector3.down * RingDepth;
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
            FX.RingDust(ring.transform, r, span * 0.1f, 2200, 85f,
                a[i], new Color(a[i].r, a[i].g * 0.9f, a[i].b * 0.7f, a[i].a * 0.6f));
        }

        // Visible debris racing with the ring — the tell that it bites.
        var rockMatRing = FX.Standard(new Color(0.8f, 0.65f, 0.4f), Color.black, 0.1f, 0.4f);
        for (int i = 0; i < 60; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float r   = Mathf.Lerp(ringInner, ringOuter, Random.value);
            float size = Random.Range(25f, 70f);
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

    // True when a world position is inside the deadly ring band.
    public bool InRing(Vector3 pos)
    {
        Vector3 local = Quaternion.Inverse(ringTilt) *
                        (pos - (Center + Vector3.down * RingDepth));
        float radial = new Vector2(local.x, local.z).magnitude;
        return radial > ringInner && radial < ringOuter && Mathf.Abs(local.y) < RingHalfThick;
    }

    void Update()
    {
        if (ringRoot != null)
            ringRoot.Rotate(Vector3.up * RingSpinDeg * Time.deltaTime, Space.Self);

        var gm = GameManager.Instance;
        if (gm == null) return;

        // Ring shredding: every tick, everything inside the band loses a block.
        ringTick -= Time.deltaTime;
        if (ringTick <= 0f)
        {
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

        if (gm.PlayerShip == null) return;
        EnsureChunks(gm.PlayerShip.transform.position);
    }

    // ── Chunk streaming ──────────────────────────────────────────────────────

    public void EnsureChunks(Vector3 worldPos)
    {
        Vector3 local = worldPos - Center;
        Vector2 flat = new Vector2(local.x, local.z);
        if (flat.magnitude > RegionRadius + ChunkSize * 3f)
        {
            if (chunks.Count > 0) ClearChunks();
            return;
        }

        int px = Mathf.FloorToInt(local.x / ChunkSize);
        int pz = Mathf.FloorToInt(local.z / ChunkSize);

        var wanted = new HashSet<Vector2Int>();
        for (int x = px - ViewChunks; x <= px + ViewChunks; x++)
            for (int z = pz - ViewChunks; z <= pz + ViewChunks; z++)
            {
                var key = new Vector2Int(x, z);
                Vector2 c = (new Vector2(x, z) + Vector2.one * 0.5f) * ChunkSize;
                if (c.magnitude > RegionRadius) continue;
                wanted.Add(key);
                if (!chunks.ContainsKey(key)) chunks[key] = BuildChunk(key);
            }

        var drop = new List<Vector2Int>();
        foreach (var kv in chunks)
            if (!wanted.Contains(kv.Key)) drop.Add(kv.Key);
        foreach (var key in drop)
        {
            Destroy(chunks[key]);
            chunks.Remove(key);
        }
    }

    void ClearChunks()
    {
        foreach (var go in chunks.Values) Destroy(go);
        chunks.Clear();
    }

    // Bare wind-scoured ground — the scene down here is the dust storm
    // itself, not terrain furniture.
    GameObject BuildChunk(Vector2Int key)
    {
        var chunk = new GameObject($"Chunk {key.x},{key.y}");
        chunk.transform.SetParent(transform, false);
        chunk.transform.localPosition = new Vector3(
            (key.x + 0.5f) * ChunkSize, 0f, (key.y + 0.5f) * ChunkSize);

        // Deep mesa slab: 700 m of rock so the terrain always meets the globe
        // beneath it (the sphere cap droops ~600 m at the region edge).
        var ground = new GameObject("Ground");
        ground.transform.SetParent(chunk.transform, false);
        ground.transform.localPosition = Vector3.down * 350f;
        ground.transform.localScale = new Vector3(ChunkSize, 700f, ChunkSize);
        ground.AddComponent<MeshFilter>().mesh = MeshFactory.CubeMesh();
        ground.AddComponent<MeshRenderer>().material = groundMat;
        ground.AddComponent<BoxCollider>();
        return chunk;
    }
}
