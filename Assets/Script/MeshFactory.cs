using System.Collections.Generic;
using UnityEngine;

public static class MeshFactory
{
    // Dart-shaped spaceship: pointed nose, swept wings, dorsal fin
    public static Mesh CreateShipMesh()
    {
        var verts = new Vector3[]
        {
            new Vector3( 0.00f,  0.00f,  1.80f), // 0 nose
            new Vector3(-1.20f, -0.10f, -0.80f), // 1 left wing tip
            new Vector3( 1.20f, -0.10f, -0.80f), // 2 right wing tip
            new Vector3( 0.00f, -0.10f, -1.20f), // 3 tail
            new Vector3( 0.00f,  0.55f, -0.15f), // 4 dorsal fin
            new Vector3( 0.00f, -0.20f,  0.30f), // 5 belly
        };
        var tris = new int[]
        {
            0, 4, 2,  0, 1, 4,  1, 3, 4,  4, 3, 2,  // top faces
            0, 2, 5,  0, 5, 1,  1, 5, 3,  5, 2, 3,  // bottom faces
        };
        return Build("Ship", verts, tris);
    }

    // Icosphere with Perlin-noise displacement — looks like a rocky asteroid
    public static Mesh CreateAsteroidMesh(int seed, float radius = 1f)
    {
        var (verts, tris) = Icosphere(2);
        for (int i = 0; i < verts.Length; i++)
        {
            float n = Mathf.PerlinNoise(
                verts[i].x * 1.8f + seed * 0.137f,
                verts[i].z * 1.8f + seed * 0.093f);
            verts[i] = verts[i].normalized * radius * (0.72f + n * 0.56f);
        }
        return Build("Asteroid", verts, tris);
    }

    // ── Icosphere ────────────────────────────────────────────────────────────

    static (Vector3[] v, int[] t) Icosphere(int subs)
    {
        float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var v = new List<Vector3>
        {
            N(-1, g, 0), N( 1, g, 0), N(-1,-g, 0), N( 1,-g, 0),
            N( 0,-1, g), N( 0, 1, g), N( 0,-1,-g), N( 0, 1,-g),
            N( g, 0,-1), N( g, 0, 1), N(-g, 0,-1), N(-g, 0, 1),
        };
        var f = new List<int>
        {
             0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
             1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
             3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
             4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
        };
        for (int s = 0; s < subs; s++)
        {
            var cache = new Dictionary<long, int>();
            var nf = new List<int>();
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i+1], c = f[i+2];
                int ab = Mid(a, b, v, cache), bc = Mid(b, c, v, cache), ca = Mid(c, a, v, cache);
                nf.AddRange(new[]{ a,ab,ca, b,bc,ab, c,ca,bc, ab,bc,ca });
            }
            f = nf;
        }
        return (v.ToArray(), f.ToArray());
    }

    static Vector3 N(float x, float y, float z) => new Vector3(x, y, z).normalized;

    static int Mid(int a, int b, List<Vector3> v, Dictionary<long, int> c)
    {
        long key = (long)Mathf.Min(a,b) * 1_000_000 + Mathf.Max(a,b);
        if (c.TryGetValue(key, out int idx)) return idx;
        idx = v.Count;
        v.Add(((v[a] + v[b]) * 0.5f).normalized);
        c[key] = idx;
        return idx;
    }

    static Mesh Build(string name, Vector3[] verts, int[] tris)
    {
        var m = new Mesh { name = name };
        m.vertices  = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
