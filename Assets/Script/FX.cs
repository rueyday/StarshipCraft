using System.Collections.Generic;
using UnityEngine;

// All visual effects, generated in code — no textures or prefabs on disk.
public static class FX
{
    // ── Faction accent colors ────────────────────────────────────────────────

    public static Color Accent(Faction f)
    {
        switch (f)
        {
            case Faction.Player: return new Color(0.25f, 0.9f, 1f);   // cyan
            case Faction.Ally:   return new Color(0.35f, 1f, 0.55f);  // green
            default:             return new Color(1f, 0.3f, 0.25f);   // red
        }
    }

    // ── Materials ────────────────────────────────────────────────────────────

    static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

    public static Material Standard(Color color, Color emission, float metallic = 0.6f, float smooth = 0.65f)
    {
        string key = $"{color}|{emission}|{metallic}|{smooth}";
        if (matCache.TryGetValue(key, out var m)) return m;

        m = new Material(Shader.Find("Standard"));
        m.color = color;
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smooth);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
        }
        matCache[key] = m;
        return m;
    }

    public static Material BlockMat(Faction f, BlockDef d)
    {
        Color a = Accent(f);
        bool mk2 = d.mk == 2;
        switch (d.type)
        {
            case BlockType.Core:
                return Standard(Color.Lerp(a, Color.white, 0.3f), a * 2.2f, 0.2f, 0.9f);
            case BlockType.Armor:
                return mk2 ? Standard(new Color(0.2f, 0.19f, 0.16f), a * 0.12f, 0.9f, 0.55f)
                           : Standard(new Color(0.15f, 0.15f, 0.15f), Color.black, 0.85f, 0.4f);
            case BlockType.Thruster:
                return mk2 ? Standard(new Color(0.22f, 0.23f, 0.28f), new Color(0.4f, 0.7f, 1f) * 0.35f, 0.8f, 0.8f)
                           : Standard(new Color(0.16f, 0.16f, 0.19f), new Color(1f, 0.45f, 0.1f) * 0.25f);
            case BlockType.Steering:
                return mk2 ? Standard(new Color(0.26f, 0.28f, 0.34f), a * 0.9f, 0.7f, 0.8f)
                           : Standard(new Color(0.2f, 0.22f, 0.26f), a * 0.55f);
            case BlockType.Gun:
                return mk2 ? Standard(new Color(0.18f, 0.17f, 0.22f), a * 0.6f, 0.9f, 0.85f)
                           : Standard(new Color(0.14f, 0.14f, 0.17f), a * 0.35f, 0.8f, 0.75f);
            default: // Hull
                return mk2 ? Standard(new Color(0.42f, 0.46f, 0.55f), a * 0.15f, 0.85f, 0.8f)
                           : Standard(new Color(0.3f, 0.33f, 0.4f), a * 0.07f, 0.75f, 0.7f);
        }
    }

    public static Material Ghost(Color c)
    {
        var m = new Material(Shader.Find("Standard"));
        m.SetFloat("_Mode", 3f); // transparent
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.renderQueue = 3000;
        m.color = c;
        return m;
    }

    // Soft radial dot used by every particle system and trail.
    static Texture2D dotTex;
    static Texture2D DotTex()
    {
        if (dotTex != null) return dotTex;
        const int s = 64;
        dotTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), Vector2.one * (s - 1) * 0.5f) / (s * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                dotTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        dotTex.Apply();
        return dotTex;
    }

    static Material particleMat;
    public static Material ParticleMat()
    {
        if (particleMat != null) return particleMat;
        particleMat = new Material(Shader.Find("Particles/Standard Unlit"));
        particleMat.SetTexture("_MainTex", DotTex());
        particleMat.SetFloat("_Mode", 4f); // additive
        particleMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        particleMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        particleMat.SetInt("_ZWrite", 0);
        particleMat.EnableKeyword("_ALPHABLEND_ON");
        particleMat.renderQueue = 3000;
        return particleMat;
    }

    // ── Particle systems ─────────────────────────────────────────────────────

    static ParticleSystem NewSystem(string name, Transform parent, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        go.GetComponent<ParticleSystemRenderer>().material = ParticleMat();
        var em = ps.emission; em.enabled = false;
        return ps;
    }

    // Continuous engine plume behind a thruster block. Rate driven by Ship.
    public static ParticleSystem EngineFlame(Transform parent, Vector3 localPos, Color tint)
    {
        var ps = NewSystem("Flame", parent, localPos);
        ps.transform.localRotation = Quaternion.LookRotation(Vector3.back);

        var main = ps.main;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startColor      = new ParticleSystem.MinMaxGradient(tint, new Color(1f, 0.55f, 0.1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 200;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 8f;
        shape.radius    = 0.12f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.05f), 0.6f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;
        return ps;
    }

    // Cold-gas sparkle puffs around an RCS pod while the ship turns.
    public static ParticleSystem RcsPuff(Transform parent, Color tint)
    {
        var ps = NewSystem("RcsPuff", parent, Vector3.zero);

        var main = ps.main;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSize       = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startColor      = new ParticleSystem.MinMaxGradient(Color.white, tint);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = 60;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.42f;

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;
        return ps;
    }

    // ── One-shot effects ─────────────────────────────────────────────────────

    static void Burst(Vector3 pos, Color c, int count, float speed, float size, float life)
    {
        var ps = NewSystem("Burst", null, Vector3.zero);
        ps.transform.position = pos;

        var main = ps.main;
        main.startLifetime   = new ParticleSystem.MinMaxCurve(life * 0.4f, life);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
        main.startSize       = new ParticleSystem.MinMaxCurve(size * 0.4f, size);
        main.startColor      = new ParticleSystem.MinMaxGradient(Color.white, c);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles    = count;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.1f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 0.4f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ps.Emit(count);
        Object.Destroy(ps.gameObject, life + 0.5f);
    }

    public static void Impact(Vector3 pos, Color c)  => Burst(pos, c, 18, 9f, 0.3f, 0.5f);

    public static void Explosion(Vector3 pos, Color c, float scale = 1f)
    {
        Burst(pos, c, (int)(50 * scale), 14f * scale, 0.7f * scale, 1.1f);
        Burst(pos, new Color(1f, 0.6f, 0.15f), (int)(30 * scale), 7f * scale, 1f * scale, 0.8f);
        Flash(pos, c, 6f * scale, 0.35f);
    }

    public static void MuzzleFlash(Vector3 pos, Color c)
    {
        Burst(pos, c, 8, 4f, 0.25f, 0.15f);
        Flash(pos, c, 2.5f, 0.08f);
    }

    // Brief point light that fades out.
    public static void Flash(Vector3 pos, Color c, float intensity, float life)
    {
        var go = new GameObject("Flash");
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = intensity * 5f;
        go.AddComponent<FadeLight>().life = life;
    }

    // Tumbling block that flies off a damaged ship, glows, then fades away.
    public static void Debris(Vector3 pos, Quaternion rot, Material mat, Vector3 vel)
    {
        var go = new GameObject("Debris");
        go.transform.SetPositionAndRotation(pos, rot);
        go.AddComponent<MeshFilter>().mesh = MeshFactory.CubeMesh();
        go.AddComponent<MeshRenderer>().material = mat;
        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.velocity = vel + Random.insideUnitSphere * 3f;
        rb.angularVelocity = Random.insideUnitSphere * 6f;
        Object.Destroy(go, 3f);
    }

    // Sparkling dust torus for a planet's ring. Donut shape emits around the
    // local Z axis, so tip it 90° to lie in the belt's XZ plane.
    public static void RingDust(Transform parent, float ringRadius, float tubeRadius)
    {
        var ps = NewSystem("RingDust", parent, Vector3.zero);
        ps.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var main = ps.main;
        main.startLifetime   = 1e6f;
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(0.75f, 0.85f, 1f, 0.55f), new Color(1f, 0.9f, 0.7f, 0.35f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles    = 900;

        var shape = ps.shape;
        shape.shapeType   = ParticleSystemShapeType.Donut;
        shape.radius      = ringRadius;
        shape.donutRadius = tubeRadius;
        ps.Emit(900);
    }

    // Static field of glowing star particles surrounding the play area.
    public static void Starfield(Transform parent)
    {
        var ps = NewSystem("Starfield", parent, Vector3.zero);
        var main = ps.main;
        main.startLifetime   = 1e6f;
        main.startSpeed      = 0f;
        main.startSize       = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
        main.startColor      = new ParticleSystem.MinMaxGradient(
            new Color(0.7f, 0.8f, 1f), new Color(1f, 0.95f, 0.8f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles    = 1200;

        var shape = ps.shape;
        shape.shapeType   = ParticleSystemShapeType.Sphere;
        shape.radius      = 550f;
        shape.radiusThickness = 0.35f; // hollow-ish shell so stars stay distant
        ps.Emit(1200);
    }
}

public class FadeLight : MonoBehaviour
{
    public float life = 0.2f;
    float t;
    Light l;
    float startIntensity;

    void Start() { l = GetComponent<Light>(); startIntensity = l.intensity; }

    void Update()
    {
        t += Time.deltaTime;
        if (t >= life) { Destroy(gameObject); return; }
        l.intensity = startIntensity * (1f - t / life);
    }
}
