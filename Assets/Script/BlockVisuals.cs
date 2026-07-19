using UnityEngine;

// Builds the visual geometry for one block, shared by ShipBuilder (preview)
// and Ship (flyable) so blocks look identical in both modes. Each functional
// block has a distinct silhouette, and every Mk II variant is visibly fancier:
//   Thruster — nozzle bell + glowing ring; Mk II: bigger bell, fins, blue flame ring
//   Gun      — long barrel; Mk II: twin barrels and a targeting fin
//   RCS      — four-nozzle pod; Mk II: bigger jets inside a glowing gyro ring
//   Hull     — plain cube; Mk II: lighter alloy with a glowing waistband
//   Armor    — bulky plated slab; Mk II: reinforced plates with glowing trim
public static class BlockVisuals
{
    public static Renderer Attach(Transform parent, BlockDef def, Faction f)
    {
        Color accent = FX.Accent(f);
        bool mk2 = def.mk == 2;
        Material dark  = FX.Standard(new Color(0.09f, 0.09f, 0.11f), Color.black, 0.8f, 0.6f);
        Material block = FX.BlockMat(f, def);

        switch (def.type)
        {
            case BlockType.Thruster:
            {
                var body = Cube(parent, Vector3.zero, new Vector3(0.95f, 0.95f, 0.8f), block);
                float bell = mk2 ? 0.52f : 0.44f;
                Cone(parent, new Vector3(0f, 0f, -0.4f), Vector3.back, 0.2f, bell, mk2 ? 0.55f : 0.5f, dark);
                Color ring = mk2 ? new Color(0.4f, 0.75f, 1f) * 2.6f : new Color(1f, 0.5f, 0.1f) * 2.2f;
                Cone(parent, new Vector3(0f, 0f, mk2 ? -0.93f : -0.88f), Vector3.back,
                     bell * 0.82f, bell * 0.82f, 0.06f, FX.Standard(Color.black, ring, 0f, 0.5f));
                if (mk2)
                {
                    Cube(parent, new Vector3(0.5f, 0f, -0.1f), new Vector3(0.1f, 0.5f, 0.75f), dark);
                    Cube(parent, new Vector3(-0.5f, 0f, -0.1f), new Vector3(0.1f, 0.5f, 0.75f), dark);
                }
                return body;
            }

            case BlockType.Gun:
            {
                var body = Cube(parent, Vector3.zero, new Vector3(0.8f, 0.8f, 0.9f), block);
                Cube(parent, new Vector3(0f, 0.32f, 0.1f), new Vector3(0.5f, 0.3f, 0.6f), dark);
                Material tip = FX.Standard(Color.black, accent * 2.5f, 0f, 0.5f);
                if (mk2)
                {
                    Cube(parent, new Vector3(0f, 0.55f, -0.1f), new Vector3(0.08f, 0.35f, 0.5f), dark);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = side * 0.16f;
                        Cone(parent, new Vector3(x, 0f, 0.45f), Vector3.forward, 0.13f, 0.1f, 0.45f, dark);
                        Cone(parent, new Vector3(x, 0f, 0.9f), Vector3.forward, 0.07f, 0.07f, 0.58f, dark);
                        Cone(parent, new Vector3(x, 0f, 1.48f), Vector3.forward, 0.08f, 0.04f, 0.08f, tip);
                    }
                }
                else
                {
                    Cone(parent, new Vector3(0f, 0f, 0.45f), Vector3.forward, 0.16f, 0.13f, 0.45f, dark);
                    Cone(parent, new Vector3(0f, 0f, 0.9f), Vector3.forward, 0.08f, 0.08f, 0.5f, dark);
                    Cone(parent, new Vector3(0f, 0f, 1.4f), Vector3.forward, 0.09f, 0.05f, 0.08f, tip);
                }
                return body;
            }

            case BlockType.Steering:
            {
                var body = Cube(parent, Vector3.zero, Vector3.one * 0.62f, block);
                Material jet = FX.Standard(new Color(0.09f, 0.09f, 0.11f), accent * 1.4f, 0.8f, 0.6f);
                float r0 = mk2 ? 0.2f : 0.17f, len = mk2 ? 0.34f : 0.3f;
                Cone(parent, new Vector3( 0.31f, 0f, 0f), Vector3.right, r0, 0.07f, len, jet);
                Cone(parent, new Vector3(-0.31f, 0f, 0f), Vector3.left,  r0, 0.07f, len, jet);
                Cone(parent, new Vector3(0f,  0.31f, 0f), Vector3.up,    r0, 0.07f, len, jet);
                Cone(parent, new Vector3(0f, -0.31f, 0f), Vector3.down,  r0, 0.07f, len, jet);
                if (mk2) // glowing gyro ring around the pod
                    Cone(parent, new Vector3(0f, 0f, -0.025f), Vector3.forward, 0.46f, 0.46f, 0.05f,
                         FX.Standard(Color.black, accent * 1.8f, 0f, 0.5f));
                return body;
            }

            case BlockType.Armor:
            {
                var body = Cube(parent, Vector3.zero, Vector3.one, block);
                Material plate = mk2
                    ? FX.Standard(new Color(0.28f, 0.26f, 0.2f), accent * 0.35f, 0.9f, 0.6f)
                    : FX.Standard(new Color(0.22f, 0.22f, 0.23f), Color.black, 0.85f, 0.45f);
                float t = mk2 ? 0.14f : 0.1f;
                Cube(parent, new Vector3(0f, 0f,  0.5f), new Vector3(0.66f, 0.66f, t), plate);
                Cube(parent, new Vector3(0f, 0f, -0.5f), new Vector3(0.66f, 0.66f, t), plate);
                Cube(parent, new Vector3( 0.5f, 0f, 0f), new Vector3(t, 0.66f, 0.66f), plate);
                Cube(parent, new Vector3(-0.5f, 0f, 0f), new Vector3(t, 0.66f, 0.66f), plate);
                Cube(parent, new Vector3(0f,  0.5f, 0f), new Vector3(0.66f, t, 0.66f), plate);
                Cube(parent, new Vector3(0f, -0.5f, 0f), new Vector3(0.66f, t, 0.66f), plate);
                return body;
            }

            case BlockType.Hull:
            {
                var body = Cube(parent, Vector3.zero, Vector3.one * 0.98f, block);
                if (mk2) // glowing alloy waistband
                    Cube(parent, Vector3.zero, new Vector3(1.0f, 0.07f, 1.0f),
                         FX.Standard(Color.black, accent * 1.2f, 0f, 0.5f));
                return body;
            }

            default: // Core
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
