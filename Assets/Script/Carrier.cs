using UnityEngine;

// The fleet carrier — the player's forward base, parked in open space.
// A landable flight deck with glowing edge strips, a command tower, and a
// marked refit pad: settle onto the pad slowly and press E to open the
// shipyard without leaving the world. Launch and stranded respawns both
// put you back on the deck.
public class Carrier : MonoBehaviour
{
    public static Carrier Instance { get; private set; }

    public Vector3 RespawnPoint => transform.position + new Vector3(0f, 14f, -55f);
    public Vector3 PadCenter    => transform.position + new Vector3(0f, 10f, 45f);

    public static Carrier Create(Vector3 pos)
    {
        var go = new GameObject("Carrier");
        go.transform.position = pos;
        var c = go.AddComponent<Carrier>();
        Instance = c;

        Material hull = FX.Standard(new Color(0.32f, 0.34f, 0.38f), Color.black, 0.8f, 0.55f);
        Material dark = FX.Standard(new Color(0.16f, 0.17f, 0.2f), Color.black, 0.7f, 0.5f);
        Color cyan = new Color(0.3f, 0.9f, 1f);

        c.Box("Hull",  new Vector3(0f, 0f, 0f),   new Vector3(34f, 12f, 170f), hull);
        c.Box("Deck",  new Vector3(0f, 7f, 0f),   new Vector3(40f, 2f, 176f),  dark);
        c.Box("Bow",   new Vector3(0f, -2f, 92f), new Vector3(20f, 8f, 16f),   hull);
        c.Box("Tower", new Vector3(14f, 16f, -30f), new Vector3(8f, 16f, 22f), hull);
        c.Box("Bridge", new Vector3(14f, 25f, -26f), new Vector3(10f, 3f, 12f), dark);

        // Glowing runway edge strips + refit pad marker.
        Material strip = FX.Standard(Color.black, cyan * 1.6f, 0f, 0.5f);
        c.Box("StripL", new Vector3(-18f, 8.1f, 0f), new Vector3(1f, 0.3f, 176f), strip, false);
        c.Box("StripR", new Vector3(18f, 8.1f, 0f),  new Vector3(1f, 0.3f, 176f), strip, false);
        c.Box("Pad",    new Vector3(0f, 8.15f, 45f), new Vector3(24f, 0.2f, 30f),
              FX.Standard(new Color(0.1f, 0.12f, 0.14f), cyan * 0.35f, 0.3f, 0.6f), false);

        var light = new GameObject("DeckLight").AddComponent<Light>();
        light.transform.SetParent(go.transform, false);
        light.transform.localPosition = new Vector3(0f, 22f, 45f);
        light.type = LightType.Point;
        light.color = cyan;
        light.range = 70f;
        light.intensity = 1.6f;

        // Runway chevrons marching down the deck toward the refit pad.
        Material chevron = FX.Standard(Color.black, cyan * 0.7f, 0f, 0.5f);
        for (int z = -70; z <= 30; z += 20)
            c.Box("Chevron", new Vector3(0f, 8.05f, z), new Vector3(20f, 0.15f, 1.6f), chevron, false);

        // Engine cluster at the stern: three bells with glowing blue cores.
        Material bell = FX.Standard(new Color(0.12f, 0.13f, 0.16f), Color.black, 0.8f, 0.5f);
        Material core = FX.Standard(Color.black, new Color(0.45f, 0.7f, 1f) * 2f, 0f, 0.5f);
        for (int i = -1; i <= 1; i++)
        {
            Vector3 basePos = new Vector3(i * 10f, 0f, -86f);
            c.Cone("EngineBell", basePos, Vector3.back, 3.2f, 4.6f, 7f, bell);
            c.Cone("EngineCore", basePos + new Vector3(0f, 0f, -6.5f), Vector3.back, 3.6f, 3.6f, 0.5f, core);
        }

        // Antenna masts on the tower.
        c.Box("Mast1", new Vector3(14f, 32f, -34f), new Vector3(0.4f, 12f, 0.4f), hull, false);
        c.Box("Mast2", new Vector3(11f, 29f, -20f), new Vector3(0.3f, 7f, 0.3f), hull, false);

        // Blinking navigation beacons: bow (green), stern (red), mast (cyan).
        c.Beacon(new Vector3(0f, 3f, 98f), new Color(0.3f, 1f, 0.4f), 0f);
        c.Beacon(new Vector3(0f, 2f, -88f), new Color(1f, 0.35f, 0.3f), 0.5f);
        c.Beacon(new Vector3(14f, 39f, -34f), cyan, 0.25f);
        return c;
    }

    void Cone(string coneName, Vector3 localPos, Vector3 dir, float r0, float r1, float len, Material mat)
    {
        var g = new GameObject(coneName);
        g.transform.SetParent(transform, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.LookRotation(dir);
        g.AddComponent<MeshFilter>().mesh = MeshFactory.CreateCone(r0, r1, len, 14);
        g.AddComponent<MeshRenderer>().material = mat;
    }

    void Beacon(Vector3 localPos, Color color, float phase)
    {
        var b = new GameObject("Beacon");
        b.transform.SetParent(transform, false);
        b.transform.localPosition = localPos;
        var l = b.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = 26f;
        var blink = b.AddComponent<BlinkLight>();
        blink.phase = phase;
    }

    void Box(string boxName, Vector3 localPos, Vector3 size, Material mat, bool collider = true)
    {
        var b = new GameObject(boxName);
        b.transform.SetParent(transform, false);
        b.transform.localPosition = localPos;
        b.transform.localScale = size;
        b.AddComponent<MeshFilter>().mesh = MeshFactory.CubeMesh();
        b.AddComponent<MeshRenderer>().material = mat;
        if (collider) b.AddComponent<BoxCollider>();
    }

    public class BlinkLight : MonoBehaviour
    {
        public float phase;
        Light l;
        void Awake() => l = GetComponent<Light>();
        void Update()
        {
            if (l != null)
                l.intensity = Mathf.Repeat(Time.time * 0.8f + phase, 1f) < 0.15f ? 2.2f : 0.05f;
        }
    }

    // True when a ship is parked on the refit pad slowly enough to service.
    public bool CanRefit(Ship ship)
    {
        if (ship == null || ship.Body == null) return false;
        Vector3 d = ship.transform.position - PadCenter;
        return Mathf.Abs(d.x) < 14f && Mathf.Abs(d.z) < 18f &&
               d.y > -4f && d.y < 14f &&
               ship.Body.velocity.magnitude < 6f;
    }
}
