using System.Collections.Generic;
using UnityEngine;

public static class MeshFactory
{
    static Mesh cube;

    // Unit cube with hard-edged normals (24 verts), shared by every block.
    public static Mesh CubeMesh()
    {
        if (cube != null) return cube;
        var v = new List<Vector3>();
        var t = new List<int>();
        var normals = new[]
        {
            Vector3.up, Vector3.down, Vector3.left,
            Vector3.right, Vector3.forward, Vector3.back,
        };
        foreach (var n in normals)
        {
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up);
            Vector3 w = Vector3.Cross(n, u);
            int b = v.Count;
            v.Add((n - u - w) * 0.5f); v.Add((n - u + w) * 0.5f);
            v.Add((n + u + w) * 0.5f); v.Add((n + u - w) * 0.5f);
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        cube = Build("Cube", v.ToArray(), t.ToArray());
        return cube;
    }

    // Capped truncated cone along +Z: radius r0 at z=0, r1 at z=height.
    public static Mesh CreateCone(float r0, float r1, float height, int segs = 16)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i < segs; i++)
        {
            float a = i * Mathf.PI * 2f / segs;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            v.Add(new Vector3(c * r0, s * r0, 0f));
            v.Add(new Vector3(c * r1, s * r1, height));
        }
        int baseC = v.Count; v.Add(new Vector3(0f, 0f, 0f));
        int topC  = v.Count; v.Add(new Vector3(0f, 0f, height));
        for (int i = 0; i < segs; i++)
        {
            int i0 = i * 2, i1 = i * 2 + 1;
            int j0 = ((i + 1) % segs) * 2, j1 = j0 + 1;
            t.AddRange(new[] { i0, j0, i1, j0, j1, i1 }); // side
            t.AddRange(new[] { baseC, j0, i0 });          // base cap (faces -Z)
            t.AddRange(new[] { topC, i1, j1 });           // top cap (faces +Z)
        }
        return Build("Cone", v.ToArray(), t.ToArray());
    }

    // One mesh containing a cube per occupied grid cell. Assigned to a convex
    // MeshCollider, PhysX cooks it down to the ship's convex hull.
    public static Mesh BuildHullMesh(IEnumerable<Vector3Int> cells)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        var src = CubeMesh();
        foreach (var c in cells)
        {
            int b = v.Count;
            foreach (var p in src.vertices) v.Add(p + (Vector3)c);
            foreach (var i in src.triangles) t.Add(b + i);
        }
        return Build("Hull", v.ToArray(), t.ToArray());
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

    static Mesh unitSphere;

    // Smooth unit-radius sphere (used for oceans and atmospheres).
    public static Mesh CreateSphereMesh()
    {
        if (unitSphere != null) return unitSphere;
        var (v, t) = Icosphere(3);
        unitSphere = Build("Sphere", v, t);
        return unitSphere;
    }

    // Planet terrain: dense icosphere with layered Perlin mountains and
    // valleys. Valleys dip below the ocean sphere, forming continents.
    public static Mesh CreatePlanetMesh(int seed, float radius)
    {
        var (verts, tris) = Icosphere(4);
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 n = verts[i];
            float h = Mathf.PerlinNoise(n.x * 2.2f + seed * 0.13f, n.z * 2.2f + seed * 0.71f)
                    + 0.5f * Mathf.PerlinNoise(n.y * 4.5f + seed * 0.37f, n.x * 4.5f);
            h /= 1.5f;
            verts[i] = n * radius * (0.94f + h * 0.12f);
        }
        return Build("Planet", verts, tris);
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
        long key = (long)Mathf.Min(a,b) * 1000000 + Mathf.Max(a,b);
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
