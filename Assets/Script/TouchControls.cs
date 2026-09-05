using UnityEngine;

// Mobile control layer (Android/iOS): a virtual steering stick on the left,
// a throttle rail on the right edge, and action buttons — read straight from
// Input.touches because IMGUI only sees one finger. Inert on desktop: every
// entry point early-outs unless the platform is actually mobile.
//
// Coordinates: touch space is y-UP from the bottom-left; IMGUI is y-DOWN from
// the top-left. Zones live in touch space; Draw() converts.
public static class TouchControls
{
    public static bool Enabled => Application.isMobilePlatform;

    public static Vector2 Steer;      // -1..1, x = yaw, y = pitch (up = nose up)
    public static float Throttle;     // -1..1 while a finger rides the rail
    public static bool FireHeld, TurboHeld;
    public static bool RemoveMode;    // shipyard: taps remove instead of place

    // One-frame taps; the consumer resets them.
    public static bool AnchorTap, ArmorTap, MapTap;

    static int steerId = -1;
    static Vector2 steerOrigin;

    static float S => Mathf.Min(Screen.width, Screen.height);

    // Zones (touch space, y up).
    static Rect SteerZone   => new Rect(0f, 0f, Screen.width * 0.42f, Screen.height * 0.78f);
    static Rect ThrottleRail => new Rect(Screen.width - S * 0.14f, Screen.height * 0.16f,
                                         S * 0.14f, Screen.height * 0.6f);
    static Rect FireBtn   => new Rect(Screen.width - S * 0.42f, S * 0.05f, S * 0.24f, S * 0.24f);
    static Rect TurboBtn  => Btn(0);
    static Rect AnchorBtn => Btn(1);
    static Rect ArmorBtn  => Btn(2);
    static Rect MapBtn    => Btn(3);
    static Rect Btn(int i) => new Rect(Screen.width - S * (0.42f - 0.115f * i),
                                       S * 0.32f, S * 0.1f, S * 0.1f);

    // Called once per frame (GameManager.Update) before controllers read input.
    public static void Poll()
    {
        if (!Enabled) return;

        bool fire = false, turbo = false, rail = false;
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            Vector2 p = t.position;

            if (t.phase == TouchPhase.Began)
            {
                if (steerId < 0 && SteerZone.Contains(p)) { steerId = t.fingerId; steerOrigin = p; }
                else if (AnchorBtn.Contains(p)) AnchorTap = true;
                else if (ArmorBtn.Contains(p))  ArmorTap = true;
                else if (MapBtn.Contains(p))    MapTap = true;
                else if (RemoveBtn.Contains(p)) RemoveMode = !RemoveMode;
            }

            if (t.fingerId == steerId)
            {
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                { steerId = -1; Steer = Vector2.zero; }
                else
                    Steer = Vector2.ClampMagnitude((p - steerOrigin) / (S * 0.13f), 1f);
            }

            if (FireBtn.Contains(p)) fire = true;
            if (TurboBtn.Contains(p)) turbo = true;
            if (ThrottleRail.Contains(p))
            {
                rail = true;
                Throttle = Mathf.Clamp((p.y - ThrottleRail.center.y) / (ThrottleRail.height * 0.45f), -1f, 1f);
            }
        }
        FireHeld = fire;
        TurboHeld = turbo;
        if (!rail) Throttle = 0f;
        if (Input.touchCount == 0) { steerId = -1; Steer = Vector2.zero; }
    }

    // Shipyard-only button (drawn/polled only in Build state via flags below).
    public static bool ShipyardMode;
    static Rect RemoveBtn => ShipyardMode
        ? new Rect(Screen.width - S * 0.18f, Screen.height - S * 0.18f, S * 0.14f, S * 0.14f)
        : new Rect(-10f, -10f, 0f, 0f);

    // ── Overlay rendering (inside GameManager.OnGUI, GUI.matrix already scaled) ──

    public static void Draw(float uiScale, bool flight)
    {
        if (!Enabled) return;
        Color faint = new Color(0.45f, 0.95f, 1f, 0.18f);
        Color line  = new Color(0.45f, 0.95f, 1f, 0.55f);

        if (flight)
        {
            // Steering stick: base ring at the anchor, knob at the deflection.
            if (steerId >= 0)
            {
                Vector2 c = ToGui(steerOrigin, uiScale);
                float r = S * 0.13f / uiScale;
                Box(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), faint);
                Vector2 k = ToGui(steerOrigin + Steer * S * 0.13f, uiScale);
                Box(new Rect(k.x - 14f, k.y - 14f, 28f, 28f), line);
            }

            DrawZone(ThrottleRail, uiScale, faint, "THR");
            DrawZone(FireBtn, uiScale, FireHeld ? line : faint, "FIRE");
            DrawZone(TurboBtn, uiScale, TurboHeld ? line : faint, "TRB");
            DrawZone(AnchorBtn, uiScale, faint, "ANC");
            DrawZone(ArmorBtn, uiScale, faint, "ARM");
            DrawZone(MapBtn, uiScale, faint, "MAP");

            // Throttle position marker.
            Vector2 railC = ToGui(ThrottleRail.center, uiScale);
            float half = ThrottleRail.height * 0.45f / uiScale;
            Box(new Rect(railC.x - ThrottleRail.width * 0.5f / uiScale,
                         railC.y - Throttle * half - 3f,
                         ThrottleRail.width / uiScale, 6f), line);
        }
        else if (ShipyardMode)
        {
            DrawZone(RemoveBtn, uiScale, RemoveMode ? new Color(1f, 0.5f, 0.3f, 0.7f) : faint,
                     RemoveMode ? "DEL!" : "DEL");
        }
    }

    static void DrawZone(Rect touchRect, float uiScale, Color c, string label)
    {
        Rect r = ToGuiRect(touchRect, uiScale);
        Box(r, c);
        var st = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
        };
        st.normal.textColor = new Color(c.r, c.g, c.b, Mathf.Min(1f, c.a + 0.35f));
        GUI.Label(r, label, st);
    }

    static void Box(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - 2f, r.y, 2f, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static Vector2 ToGui(Vector2 touchPos, float uiScale)
        => new Vector2(touchPos.x / uiScale, (Screen.height - touchPos.y) / uiScale);

    static Rect ToGuiRect(Rect t, float uiScale)
        => new Rect(t.x / uiScale, (Screen.height - t.yMax) / uiScale,
                    t.width / uiScale, t.height / uiScale);
}
