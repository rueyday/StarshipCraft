using UnityEngine;

// No Man's Sky-style atmosphere transitions: cross a world's cloud top and
// you enter a different scene — its weather, thickening all the way down.
//   Titanhold : a giant sand-gold dust storm
//   Korrath   : drifting white cloud banks
//   Vessa     : a blue-white snow storm
// Driven entirely by GravityField sources; one instance lives in the scene.
public class Weather : MonoBehaviour
{
    ParticleSystem dust, clouds, snow;

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
        RenderSettings.fog = kind != WeatherKind.None && k > 0.01f;
        if (RenderSettings.fog)
        {
            RenderSettings.fogMode = FogMode.Exponential;
            switch (kind)
            {
                case WeatherKind.Dust:
                    RenderSettings.fogColor = new Color(0.6f, 0.46f, 0.28f);
                    RenderSettings.fogDensity = 0.0008f + k * 0.0045f;
                    break;
                case WeatherKind.Cloud:
                    RenderSettings.fogColor = new Color(0.75f, 0.78f, 0.82f);
                    RenderSettings.fogDensity = 0.0005f + k * 0.0035f;
                    break;
                case WeatherKind.Snow:
                    RenderSettings.fogColor = new Color(0.7f, 0.78f, 0.88f);
                    RenderSettings.fogDensity = 0.0006f + k * 0.004f;
                    break;
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
