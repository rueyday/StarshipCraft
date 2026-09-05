using System.Collections.Generic;
using UnityEngine;

// Gravity 2.0 — the cloud-layer model. Every gravity source (round planet or
// flat super-giant region) defines a cloud band by altitude:
//   below cloudBottom : constant g straight "down"
//   inside the band   : g fades linearly to zero as you climb
//   above cloudTop    : hard zero — space stays clean, no long-range wells
// What it feels like inside a world's atmosphere (below its cloud top).
public enum WeatherKind { None, Dust, Cloud, Snow, Ember }

public static class GravityField
{
    public class Source
    {
        public WeatherKind weather = WeatherKind.None;
        public string  name;
        public Vector3 center;        // planet center, or region center (flat)
        public float   surfaceRadius; // round planets; 0 for flat regions
        public bool    flat;
        public float   groundY;       // flat regions: ground level (world Y)
        public float   regionRadius;  // flat regions: horizontal reach
        public float   g = 9f;        // m/s² below the clouds
        public float   cloudBottom, cloudTop; // altitudes above surface/ground
        public Color   radarColor = new Color(0.45f, 0.65f, 1f);
    }

    public static readonly List<Source> Sources = new List<Source>();

    public static void Clear() => Sources.Clear();

    public static Vector3 Sample(Vector3 pos)
    {
        foreach (var s in Sources)
        {
            float alt;
            Vector3 down;
            if (s.flat)
            {
                Vector3 d = pos - s.center;
                d.y = 0f;
                if (d.magnitude > s.regionRadius) continue;
                alt = pos.y - s.groundY;
                down = Vector3.down;
            }
            else
            {
                Vector3 d = pos - s.center;
                alt = d.magnitude - s.surfaceRadius;
                down = d.sqrMagnitude > 0.001f ? -d.normalized : Vector3.down;
            }

            if (alt > s.cloudTop) continue;
            float k = alt <= s.cloudBottom
                ? 1f
                : 1f - (alt - s.cloudBottom) / Mathf.Max(s.cloudTop - s.cloudBottom, 1f);
            return down * s.g * k;
        }
        return Vector3.zero;
    }
}
