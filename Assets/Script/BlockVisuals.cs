using UnityEngine;

// Builds the visual geometry for one block, shared by ShipBuilder (preview)
// and Ship (flyable) so blocks look identical in both modes. Each functional
// block has a distinct silhouette:
//   Thruster — engine body with a flared nozzle bell and glowing exhaust ring
//   Gun      — armored body with a long two-stage barrel and glowing muzzle
//   RCS      — small pod with four jet nozzles pointing sideways
public static class BlockVisuals
{
    // Returns the block's primary renderer (Ship pulses the Core's emission).
    public static Renderer Attach(Transform parent, BlockType type, Faction f)
    {
        Color accent = FX.Accent(f);
        Material dark  = FX.Standard(new Color(0.09f, 0.09f, 0.11f), Color.black, 0.8f, 0.6f);
        Material block = FX.BlockMat(f, type);

        switch (type)
        {
            case BlockType.Thruster:
            {
                var body = Cube(parent, Vector3.zero, new Vector3(0.95f, 0.95f, 0.8f), block);
                Cone(parent, new Vector3(0f, 0f, -0.4f), Vector3.back, 0.2f, 0.44f, 0.5f, dark);
                // glowing exhaust ring at the bell's mouth
                Cone(parent, new Vector3(0f, 0f, -0.88f), Vector3.back, 0.36f, 0.36f, 0.06f,
                     FX.Standard(Color.black, new Color(1f, 0.5f, 0.1f) * 2.2f, 0f, 0.5f));
                return body;
            }

            case BlockType.Gun:
            {
                var body = Cube(parent, Vector3.zero, new Vector3(0.8f, 0.8f, 0.9f), block);
                Cube(parent, new Vector3(0f, 0.32f, 0.1f), new Vector3(0.5f, 0.3f, 0.6f), dark);
                Cone(parent, new Vector3(0f, 0f, 0.45f), Vector3.forward, 0.16f, 0.13f, 0.45f, dark);
                Cone(parent, new Vector3(0f, 0f, 0.9f), Vector3.forward, 0.08f, 0.08f, 0.5f, dark);
                // glowing muzzle tip
                Cone(parent, new Vector3(0f, 0f, 1.4f), Vector3.forward, 0.09f, 0.05f, 0.08f,
                     FX.Standard(Color.black, accent * 2.5f, 0f, 0.5f));
                return body;
            }

            case BlockType.Steering:
            {
                var body = Cube(parent, Vector3.zero, Vector3.one * 0.62f, block);
                Material jet = FX.Standard(new Color(0.09f, 0.09f, 0.11f), accent * 1.4f, 0.8f, 0.6f);
                Cone(parent, new Vector3( 0.31f, 0f, 0f), Vector3.right, 0.17f, 0.07f, 0.3f, jet);
                Cone(parent, new Vector3(-0.31f, 0f, 0f), Vector3.left,  0.17f, 0.07f, 0.3f, jet);
                Cone(parent, new Vector3(0f,  0.31f, 0f), Vector3.up,    0.17f, 0.07f, 0.3f, jet);
                Cone(parent, new Vector3(0f, -0.31f, 0f), Vector3.down,  0.17f, 0.07f, 0.3f, jet);
                return body;
            }

            default: // Core, Hull
                return Cube(parent, Vector3.zero, Vector3.one * 0.98f, block);
        }
    }

    static Renderer Cube(Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = new GameObject("Part");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().mesh = MeshFactory.CubeMesh();
        var r = go.AddComponent<MeshRenderer>();
        r.material = mat;
        return r;
    }

    static void Cone(Transform parent, Vector3 pos, Vector3 dir,
                     float r0, float r1, float length, Material mat)
    {
        var go = new GameObject("Part");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.LookRotation(dir);
        go.AddComponent<MeshFilter>().mesh = MeshFactory.CreateCone(r0, r1, length);
        go.AddComponent<MeshRenderer>().material = mat;
    }
}
