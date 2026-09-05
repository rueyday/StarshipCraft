using UnityEngine;

// Every sound in the game, synthesized at first use — no audio files, matching
// the zero-asset rule. A small pool of AudioSources plays one-shots (3D for
// world sounds, 2D for UI); the player's engine hum is a seamless generated
// loop owned by Ship. Master volume lives in GameSettings.
public static class SFX
{
    public enum Id { Laser, Hit, Hurt, Boom, BigBoom, Click, Confirm, Warp, Clank, Place }

    const int SR = 44100;
    static AudioSource[] pool;
    static int next;
    static AudioClip[] clips;
    static AudioClip humClip;
    static bool ready;

    static void Ensure()
    {
        if (ready) return;
        ready = true;
        var root = new GameObject("SFX");
        pool = new AudioSource[14];
        for (int i = 0; i < pool.Length; i++)
        {
            var go = new GameObject("src" + i);
            go.transform.SetParent(root.transform, false);
            pool[i] = go.AddComponent<AudioSource>();
            pool[i].playOnAwake = false;
            pool[i].dopplerLevel = 0f;
            pool[i].minDistance = 14f;
            pool[i].maxDistance = 450f;
        }

        clips = new AudioClip[10];
        clips[(int)Id.Laser]   = Laser();
        clips[(int)Id.Hit]     = Hit();
        clips[(int)Id.Hurt]    = Hurt();
        clips[(int)Id.Boom]    = Boom(0.8f, 60f, 4.5f);
        clips[(int)Id.BigBoom] = Boom(1.3f, 42f, 3f);
        clips[(int)Id.Click]   = Blip(0.05f, 1700f, 70f, 0.5f);
        clips[(int)Id.Confirm] = Confirm();
        clips[(int)Id.Warp]    = Warp();
        clips[(int)Id.Clank]   = Clank();
        clips[(int)Id.Place]   = Place();
        humClip = Hum();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public static void Play(Id id, Vector3 pos, float vol = 1f, float pitch = 1f)
        => Fire(id, pos, vol, pitch, 1f);

    public static void Ui(Id id, float vol = 1f, float pitch = 1f)
        => Fire(id, Vector3.zero, vol, pitch, 0f);

    static void Fire(Id id, Vector3 pos, float vol, float pitch, float spatial)
    {
        if (!Application.isPlaying) return;
        Ensure();
        var src = pool[next];
        next = (next + 1) % pool.Length;
        src.transform.position = pos;
        src.spatialBlend = spatial;
        src.pitch = pitch;
        src.PlayOneShot(clips[(int)id], vol * GameSettings.volume);
    }

    // Looping engine hum attached to a ship; the caller drives volume/pitch.
    public static AudioSource Loop(Transform parent)
    {
        if (!Application.isPlaying) return null;
        Ensure();
        var go = new GameObject("Hum");
        go.transform.SetParent(parent, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = humClip;
        src.loop = true;
        src.spatialBlend = 1f;
        src.dopplerLevel = 0f;
        src.minDistance = 8f;
        src.maxDistance = 120f;
        src.volume = 0f;
        src.Play();
        return src;
    }

    // ── Synthesis ────────────────────────────────────────────────────────────

    static AudioClip Bake(string clipName, float[] d)
    {
        var c = AudioClip.Create(clipName, d.Length, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    static float[] Buf(float dur) => new float[(int)(SR * dur)];

    static AudioClip Laser() // falling zap with a hard edge
    {
        var d = Buf(0.16f);
        float ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            float f = Mathf.Lerp(1250f, 320f, t / 0.16f);
            ph += 2f * Mathf.PI * f / SR;
            d[i] = Mathf.Sin(ph) * Mathf.Exp(-14f * t) * 0.7f
                 + Mathf.Sign(Mathf.Sin(ph * 0.5f)) * 0.08f * Mathf.Exp(-22f * t);
        }
        return Bake("laser", d);
    }

    static AudioClip Hit() // metallic crunch
    {
        var d = Buf(0.13f);
        var rng = new System.Random(1);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            d[i] = noise * Mathf.Exp(-28f * t) * 0.65f
                 + Mathf.Sin(2f * Mathf.PI * 240f * t) * Mathf.Exp(-22f * t) * 0.4f;
        }
        return Bake("hit", d);
    }

    static AudioClip Hurt() // low thump when your own hull takes it
    {
        var d = Buf(0.3f);
        var rng = new System.Random(2);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            d[i] = Mathf.Sin(2f * Mathf.PI * 85f * t) * Mathf.Exp(-9f * t) * 0.9f
                 + ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-16f * t) * 0.3f;
        }
        return Bake("hurt", d);
    }

    static AudioClip Boom(float dur, float bassHz, float decay)
    {
        var d = Buf(dur);
        var rng = new System.Random(3);
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            float white = (float)rng.NextDouble() * 2f - 1f;
            lp += (white - lp) * 0.08f; // cheap low-pass rumble
            d[i] = lp * Mathf.Exp(-decay * t) * 1.7f
                 + Mathf.Sin(2f * Mathf.PI * bassHz * t) * Mathf.Exp(-decay * 1.3f * t) * 0.5f;
        }
        return Bake("boom", d);
    }

    static AudioClip Blip(float dur, float hz, float decay, float amp)
    {
        var d = Buf(dur);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            d[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-decay * t) * amp;
        }
        return Bake("blip", d);
    }

    static AudioClip Confirm() // bright double-ping for a landed shot
    {
        var d = Buf(0.09f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            d[i] = Mathf.Sin(2f * Mathf.PI * 2200f * t) * Mathf.Exp(-40f * t) * 0.55f
                 + Mathf.Sin(2f * Mathf.PI * 3300f * t) * Mathf.Exp(-50f * t) * 0.3f;
        }
        return Bake("confirm", d);
    }

    static AudioClip Warp() // rising shimmer for scene swaps and turbo
    {
        var d = Buf(0.6f);
        var rng = new System.Random(4);
        float ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            float k = t / 0.6f;
            float f = Mathf.Lerp(160f, 1500f, k * k);
            ph += 2f * Mathf.PI * f / SR;
            float env = Mathf.Sin(k * Mathf.PI);
            d[i] = Mathf.Sin(ph) * env * 0.55f
                 + ((float)rng.NextDouble() * 2f - 1f) * env * 0.12f;
        }
        return Bake("warp", d);
    }

    static AudioClip Clank() // anchor lock
    {
        var d = Buf(0.18f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            d[i] = (Mathf.Sin(2f * Mathf.PI * 120f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 84f * t))
                   * Mathf.Exp(-13f * t) * 0.6f
                 + Mathf.Sin(2f * Mathf.PI * 900f * t) * Mathf.Exp(-80f * t) * 0.3f;
        }
        return Bake("clank", d);
    }

    static AudioClip Place() // block snap in the shipyard
    {
        var d = Buf(0.09f);
        float ph = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            ph += 2f * Mathf.PI * Mathf.Lerp(500f, 950f, t / 0.09f) / SR;
            d[i] = Mathf.Sin(ph) * Mathf.Exp(-26f * t) * 0.5f;
        }
        return Bake("place", d);
    }

    static AudioClip Hum() // seamless 1 s loop: 55 Hz stack, integer cycles only
    {
        var d = Buf(1f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SR;
            float wobble = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 3f * t);
            d[i] = (0.5f * Mathf.Sin(2f * Mathf.PI * 55f * t)
                  + 0.25f * Mathf.Sin(2f * Mathf.PI * 110f * t)
                  + 0.12f * Mathf.Sin(2f * Mathf.PI * 165f * t)
                  + 0.08f * Mathf.Sin(2f * Mathf.PI * 220f * t)) * wobble * 0.6f;
        }
        return Bake("hum", d);
    }
}
