using UnityEngine;

// No Man's Sky-style atmosphere transitions: cross a world's cloud top and
// you enter a different scene — its weather, thickening all the way down.
//   Titanhold : a giant sand-gold dust storm
//   Korrath   : drifting white cloud banks
//   Vessa     : a blue-white snow storm
// Driven entirely by GravityField sources; one instance lives in the scene.
public class Weather : MonoBehaviour
{
    static readonly Color SpaceBg = new Color(0.01f, 0.015f, 0.045f);

    ParticleSystem dust, clouds, snow;
    float lightningT = 6f;

    void Update()
    {
        var gm = GameManager.Instance;
        var ship = gm != null ? gm.PlayerShip : null;
        if (ship == null) { Apply(WeatherKind.None, 0f, Vector3.down, Vector3.zero); return; }

        Vector3 pos = ship.transform.position;
        WeatherKind kind = WeatherKind.None;
        float k = 0f;
        Vector3 down = Vector3.down;

        foreach (var s in GravityField.Sources)
        {
            if (s.weather == WeatherKind.None) continue;
            float alt;
            Vector3 d;
            if (s.flat)
            {
                Vector3 f = pos - s.center; f.y = 0f;
                if (f.magnitude > s.regionRadius) continue;
                alt = pos.y - s.groundY;
                d = Vector3.down;
            }
            else
            {
                Vector3 f = pos - s.center;
                alt = f.magnitude - s.surfaceRadius;
                d = f.sqrMagnitude > 0.001f ? -f.normalized : Vector3.down;
            }
            if (alt > s.cloudTop || alt < -100f) continue;
            kind = s.weather;
            k = Mathf.Clamp01(1f - alt / s.cloudTop); // 0 at cloud top → 1 at the ground
            down = d;
            break;
        }
        Apply(kind, k, down, pos);
    }

    void Apply(WeatherKind kind, float k, Vector3 down, Vector3 pos)
    {
        Color skyTarget = SpaceBg;
        RenderSettings.fog = kind != WeatherKind.None && k > 0.01f;
        if (RenderSettings.fog)
        {
            RenderSettings.fogMode = FogMode.Exponential;
            switch (kind)
            {
                case WeatherKind.Dust:
                    RenderSettings.fogColor = new Color(0.6f, 0.46f, 0.28f);
                    RenderSettings.fogDensity = 0.0008f + k * 0.0045f;
                    skyTarget = Color.Lerp(SpaceBg, new Color(0.42f, 0.3f, 0.15f), k);
                    break;
                case WeatherKind.Cloud:
                    RenderSettings.fogColor = new Color(0.75f, 0.78f, 0.82f);
                    RenderSettings.fogDensity = 0.0005f + k * 0.0035f;
                    skyTarget = Color.Lerp(SpaceBg, new Color(0.45f, 0.5f, 0.58f), k);
                    break;
                case WeatherKind.Snow:
                    RenderSettings.fogColor = new Color(0.7f, 0.78f, 0.88f);
                    RenderSettings.fogDensity = 0.0006f + k * 0.004f;
                    skyTarget = Color.Lerp(SpaceBg, new Color(0.52f, 0.62f, 0.74f), k);
                    break;
            }
        }

        // The sky itself changes world: brown murk, gray overcast, pale ice.
        var cam = Camera.main;
        if (cam != null)
            cam.backgroundColor = Color.Lerp(cam.backgroundColor, skyTarget, Time.deltaTime * 1.5f);

        // Lightning deep inside Korrath's cloud banks.
        if (kind == WeatherKind.Cloud && k > 0.3f)
        {
            lightningT -= Time.deltaTime;
            if (lightningT <= 0f)
            {
                lightningT = Random.Range(4f, 11f);
                Vector3 strike = pos + new Vector3(
                    Random.Range(-300f, 300f), Random.Range(80f, 250f), Random.Range(-300f, 300f));
                FX.Flash(strike, new Color(0.85f, 0.9f, 1f), 12f, 0.4f);
                if (GameManager.Instance != null) GameManager.Instance.CameraShake(0.2f);
            }
        }

        // Dust: wind-blown grit racing sideways past the ship.
        if (kind == WeatherKind.Dust && dust == null) dust = FX.DustStorm();
        Drive(dust, kind == WeatherKind.Dust ? k * 160f : 0f, pos + new Vector3(20f, 5f, 0f));

        // Clouds: huge soft banks drifting around you.
        if (kind == WeatherKind.Cloud && clouds == null) clouds = FX.CloudPuffs();
        Drive(clouds, kind == WeatherKind.Cloud ? k * 40f : 0f, pos);

        // Snow: flakes streaming down toward the planet.
        if (kind == WeatherKind.Snow && snow == null) snow = FX.SnowStorm();
        if (snow != null)
        {
            snow.transform.rotation = Quaternion.LookRotation(down);
            Drive(snow, kind == WeatherKind.Snow ? k * 320f : 0f, pos - down * 60f);
        }
    }

    static void Drive(ParticleSystem ps, float rate, Vector3 pos)
    {
        if (ps == null) return;
        ps.transform.position = pos;
        var em = ps.emission;
        em.rateOverTime = rate;
    }
}
