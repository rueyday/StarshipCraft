using System.Collections.Generic;
using UnityEngine;

// Single entry point. Drop this on one empty GameObject in an empty scene.
// Flow: Menu → (Settings) → Build → Playing → GameOver → Build again.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    enum State { Menu, Settings, Build, Playing }
    State state = State.Menu;

    // Hard rule: the player never dies. With every engine gone the ship is
    // stranded — respawn on offer (score carries), but guns/RCS still work.
    bool PlayerStranded => PlayerShip != null && PlayerShip.ThrusterCount == 0;

    // World population, driven by GameSettings.
    const float AsteroidSpawnRadius = 120f;
    const float NPCSpawnRadius      = 160f;
    const float DespawnRadius       = 400f;

    public Ship PlayerShip { get; private set; }
    public readonly List<Ship> Ships = new List<Ship>();
    readonly List<Asteroid> asteroids = new List<Asteroid>();

    ShipBlueprint blueprint;
    ShipBuilder builder;
    Camera cam;
    Transform starfieldRoot;
    int score;
    float playTime;
    float asteroidTimer, enemyTimer, allyTimer;
    float shake;
    float fov = 60f;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var g = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = g.AddComponent<Camera>();
            g.AddComponent<AudioListener>();
        }
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.015f, 0.045f);
        cam.farClipPlane    = 5000f; // the planet must stay visible from afar

        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.18f, 0.2f, 0.28f);
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.9f;
        sun.color = new Color(0.85f, 0.9f, 1f);
        sun.transform.rotation = Quaternion.Euler(35f, -60f, 0f);

        // Starfield follows the camera's position (not rotation) so the stars
        // read as infinitely distant no matter how far the player flies.
        starfieldRoot = new GameObject("StarfieldRoot").transform;
        FX.Starfield(starfieldRoot);

        // The looming planet: fly down its gravity well to the surface, thread
        // the orbiting belt — or crash trying. Big and close enough to fill
        // half the sky, but its pull at spawn is a negligible drift.
        Planet.Create(new Vector3(0f, -600f, 1500f), 700f);

        blueprint = DefaultPlayerBlueprint();
    }

    void Update()
    {
        if (starfieldRoot != null) starfieldRoot.position = cam.transform.position;

        switch (state)
        {
            case State.Menu:
            case State.Settings:
                break;

            case State.Build:
                if (Input.GetKeyDown(KeyCode.Return) && ReadyToLaunch()) Launch();
                break;

            case State.Playing:
                playTime += Time.deltaTime;
                if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;
                if (PlayerStranded && Input.GetKeyDown(KeyCode.R)) EnterBuild(false);
                MaintainPopulation();
                FollowCamera();
                break;
        }
    }

    // ── State transitions ────────────────────────────────────────────────────

    // fresh=true starts a new scoring run (from the menu); fresh=false is a
    // respawn after being stranded — score and clock carry over.
    void EnterBuild(bool fresh)
    {
        if (fresh) { score = 0; playTime = 0f; }
        ClearWorld();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        var go = new GameObject("Builder");
        builder = go.AddComponent<ShipBuilder>();
        builder.Init(blueprint, cam);
        state = State.Build;
    }

    bool ReadyToLaunch() => blueprint.Count(BlockType.Thruster) > 0;

    void Launch()
    {
        Destroy(builder.gameObject);
        builder = null;

        PlayerShip = SpawnShip(blueprint, Faction.Player, Vector3.zero, Quaternion.identity);
        PlayerShip.gameObject.AddComponent<PlayerController>();

        for (int i = 0; i < GameSettings.allyCount; i++) SpawnAlly();
        for (int i = 0; i < GameSettings.enemyCount; i++) SpawnEnemy();
        for (int i = 0; i < GameSettings.asteroidCount; i++) SpawnAsteroidNearPlayer();

        cam.transform.position = PlayerShip.transform.position - Vector3.forward * 18f + Vector3.up * 6f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        state = State.Playing;
    }

    void ClearWorld()
    {
        foreach (var s in Ships) if (s != null) Destroy(s.gameObject);
        Ships.Clear();
        PlayerShip = null;
        foreach (var a in asteroids) if (a != null) Destroy(a.gameObject);
        asteroids.Clear();
        foreach (var b in FindObjectsOfType<Bullet>()) Destroy(b.gameObject);
    }

    // ── Spawning ─────────────────────────────────────────────────────────────

    Ship SpawnShip(ShipBlueprint bp, Faction f, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(f + "Ship");
        go.transform.SetPositionAndRotation(pos, rot);
        var ship = go.AddComponent<Ship>();
        ship.Init(bp, f);
        Ships.Add(ship);
        return ship;
    }

    void SpawnEnemy()
    {
        if (PlayerShip == null) return;
        Vector3 pos = PlayerShip.transform.position + Random.onUnitSphere * NPCSpawnRadius;
        var s = SpawnShip(NPCBlueprint(), Faction.Enemy, pos,
            Quaternion.LookRotation(PlayerShip.transform.position - pos));
        s.gameObject.AddComponent<NPCController>();
    }

    void SpawnAlly()
    {
        if (PlayerShip == null) return;
        Vector3 pos = PlayerShip.transform.position
                    + PlayerShip.transform.right * Random.Range(-20f, 20f)
                    + PlayerShip.transform.up * 8f - PlayerShip.transform.forward * 10f;
        var s = SpawnShip(NPCBlueprint(), Faction.Ally, pos, PlayerShip.transform.rotation);
        s.gameObject.AddComponent<NPCController>();
    }

    void SpawnAsteroidNearPlayer()
    {
        if (PlayerShip == null) return;
        Vector3 pos;
        int tries = 0;
        do
        {
            pos = PlayerShip.transform.position + Random.onUnitSphere * Random.Range(50f, AsteroidSpawnRadius);
            tries++;
        } while (tries < 20 && (pos - PlayerShip.transform.position).magnitude < 40f);
        SpawnAsteroid(pos, Asteroid.AsteroidSize.Large);
    }

    public void SpawnAsteroid(Vector3 pos, Asteroid.AsteroidSize size)
    {
        float radius = size == Asteroid.AsteroidSize.Large ? 3.5f
                     : size == Asteroid.AsteroidSize.Medium ? 2.0f : 1.0f;
        float speed = GameSettings.asteroidSpeed *
                      (size == Asteroid.AsteroidSize.Large ? 1f
                     : size == Asteroid.AsteroidSize.Medium ? 1.6f : 2.4f);

        var go = new GameObject("Asteroid");
        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = MeshFactory.CreateAsteroidMesh(Random.Range(0, 9999), radius);
        go.AddComponent<MeshRenderer>().material =
            FX.Standard(new Color(0.45f, 0.4f, 0.38f), Color.black, 0.1f, 0.35f);

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.mass = radius * radius;
        rb.velocity = Random.onUnitSphere * speed;
        rb.angularVelocity = Random.onUnitSphere * 1.5f;

        go.AddComponent<SphereCollider>().radius = radius * 0.85f;

        go.transform.position = pos;
        var ast = go.AddComponent<Asteroid>();
        ast.Size = size;
        asteroids.Add(ast);
    }

    // Keeps asteroid / NPC counts topped up to the settings, culls far strays.
    void MaintainPopulation()
    {
        asteroids.RemoveAll(a => a == null);
        Ships.RemoveAll(s => s == null);

        asteroidTimer -= Time.deltaTime;
        if (asteroids.Count < GameSettings.asteroidCount && asteroidTimer <= 0f)
        {
            SpawnAsteroidNearPlayer();
            asteroidTimer = 1.2f;
        }

        enemyTimer -= Time.deltaTime;
        if (CountFaction(Faction.Enemy) < GameSettings.enemyCount && enemyTimer <= 0f)
        {
            SpawnEnemy();
            enemyTimer = 4f;
        }

        allyTimer -= Time.deltaTime;
        if (CountFaction(Faction.Ally) < GameSettings.allyCount && allyTimer <= 0f)
        {
            SpawnAlly();
            allyTimer = 12f;
        }

        if (PlayerShip != null)
            foreach (var a in asteroids)
                if (a != null &&
                    (a.transform.position - PlayerShip.transform.position).magnitude > DespawnRadius)
                {
                    asteroids.Remove(a);
                    Destroy(a.gameObject);
                    break; // one per frame is plenty
                }
    }

    int CountFaction(Faction f)
    {
        int n = 0;
        foreach (var s in Ships) if (s != null && s.faction == f) n++;
        return n;
    }

    // ── Score / events ───────────────────────────────────────────────────────

    public void OnAsteroidDestroyed(Asteroid ast, bool scored)
    {
        if (!asteroids.Remove(ast)) return;

        if (scored)
            score += ast.Size == Asteroid.AsteroidSize.Large ? 100
                   : ast.Size == Asteroid.AsteroidSize.Medium ? 50 : 25;

        Vector3 p = ast.transform.position;
        if (ast.Size == Asteroid.AsteroidSize.Large)
        {
            SpawnAsteroid(p + Random.insideUnitSphere * 2f, Asteroid.AsteroidSize.Medium);
            SpawnAsteroid(p + Random.insideUnitSphere * 2f, Asteroid.AsteroidSize.Medium);
        }
        else if (ast.Size == Asteroid.AsteroidSize.Medium)
        {
            SpawnAsteroid(p + Random.insideUnitSphere * 1.5f, Asteroid.AsteroidSize.Small);
            SpawnAsteroid(p + Random.insideUnitSphere * 1.5f, Asteroid.AsteroidSize.Small);
        }
    }

    public void OnShipDestroyed(Ship ship)
    {
        Ships.Remove(ship);
        if (ship.faction == Faction.Enemy) score += 500;
    }

    // Called the moment the last engine dies. Play stays live — the stranded
    // banner and respawn key are handled in Update/GuiPlaying.
    public void OnPlayerStranded() => CameraShake(1.5f);

    public void CameraShake(float amount) => shake = Mathf.Max(shake, amount);

    // ── Camera ───────────────────────────────────────────────────────────────

    void FollowCamera()
    {
        if (PlayerShip == null) return;
        var t = PlayerShip.transform;
        Vector3 target = t.position - t.forward * 16f + t.up * 5f;
        cam.transform.position = Vector3.Lerp(cam.transform.position, target, Time.deltaTime * 5f);

        shake = Mathf.Lerp(shake, 0f, Time.deltaTime * 4f);
        cam.transform.position += Random.insideUnitSphere * shake * 0.4f;
        cam.transform.LookAt(t.position + t.forward * 6f, t.up);

        // FOV kick while boosting sells the speed.
        float targetFov = PlayerShip.Boost && Mathf.Abs(PlayerShip.ThrustInput) > 0.1f ? 72f : 60f;
        fov = Mathf.Lerp(fov, targetFov, Time.deltaTime * 5f);
        cam.fieldOfView = fov;
    }

    // ── Blueprints ───────────────────────────────────────────────────────────

    static ShipBlueprint DefaultPlayerBlueprint()
    {
        var bp = new ShipBlueprint();
        bp.TryAdd(new Vector3Int(0, 0, 1),  new BlockDef(BlockType.Hull));
        bp.TryAdd(new Vector3Int(0, 0, 2),  new BlockDef(BlockType.Gun));
        bp.TryAdd(new Vector3Int(-1, 0, 0), new BlockDef(BlockType.Hull));
        bp.TryAdd(new Vector3Int(1, 0, 0),  new BlockDef(BlockType.Hull));
        bp.TryAdd(new Vector3Int(-1, 0, -1), new BlockDef(BlockType.Thruster));
        bp.TryAdd(new Vector3Int(1, 0, -1), new BlockDef(BlockType.Thruster));
        bp.TryAdd(new Vector3Int(0, 1, 0),  new BlockDef(BlockType.Steering));
        bp.TryAdd(new Vector3Int(0, -1, 0), new BlockDef(BlockType.Steering));
        return bp;
    }

    // NPCs fly Mk II hardware once the skill slider passes 1.3.
    static ShipBlueprint NPCBlueprint()
    {
        int mk = GameSettings.npcSkill > 1.3f ? 2 : 1;
        var bp = new ShipBlueprint();
        bp.TryAdd(new Vector3Int(0, 0, 1),  new BlockDef(BlockType.Hull, mk));
        bp.TryAdd(new Vector3Int(0, 0, 2),  new BlockDef(BlockType.Gun, mk));
        bp.TryAdd(new Vector3Int(-1, 0, 0), new BlockDef(BlockType.Hull, mk));
        bp.TryAdd(new Vector3Int(1, 0, 0),  new BlockDef(BlockType.Hull, mk));
        bp.TryAdd(new Vector3Int(-1, 0, -1), new BlockDef(BlockType.Thruster, mk));
        bp.TryAdd(new Vector3Int(1, 0, -1), new BlockDef(BlockType.Thruster, mk));
        bp.TryAdd(new Vector3Int(0, 1, 0),  new BlockDef(BlockType.Steering, mk));
        bp.TryAdd(new Vector3Int(0, -1, 0), new BlockDef(BlockType.Steering, mk));
        if (Random.value > 0.5f)
        {
            bp.TryAdd(new Vector3Int(-1, 0, 1), new BlockDef(BlockType.Gun));
            bp.TryAdd(new Vector3Int(1, 0, 1),  new BlockDef(BlockType.Gun));
        }
        return bp;
    }

    // ── HUD ──────────────────────────────────────────────────────────────────

    GUIStyle sTitle, sMed, sSmall, sBtn;
    Texture2D boxTex, radarTex;
    bool stylesBuilt;
    bool showHelp;
    float titlePulse;

    void BuildStyles()
    {
        if (stylesBuilt) return;
        stylesBuilt = true;

        boxTex = new Texture2D(1, 1);
        boxTex.SetPixel(0, 0, new Color(0.03f, 0.08f, 0.12f, 0.82f));
        boxTex.Apply();

        Color cyan = new Color(0.45f, 0.95f, 1f);

        sTitle = new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        sTitle.normal.textColor = cyan;

        sMed = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        sMed.normal.textColor = Color.white;

        sSmall = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.UpperLeft, wordWrap = true };
        sSmall.normal.textColor = cyan;

        sBtn = new GUIStyle(GUI.skin.button) { fontSize = 22 };

        radarTex = MakeRadarTex(160);
    }

    void Panel(Rect r) => GUI.DrawTexture(r, boxTex);

    void OnGUI()
    {
        BuildStyles();
        float sw = Screen.width, sh = Screen.height;

        switch (state)
        {
            case State.Menu:     GuiMenu(sw, sh); break;
            case State.Settings: GuiSettings(sw, sh); break;
            case State.Build:    GuiBuild(sw, sh); break;
            case State.Playing:  GuiPlaying(sw, sh); break;
        }
    }

    void GuiMenu(float sw, float sh)
    {
        titlePulse += Time.deltaTime;
        sTitle.normal.textColor = Color.Lerp(new Color(0.45f, 0.95f, 1f),
            new Color(0.8f, 0.5f, 1f), 0.5f + 0.5f * Mathf.Sin(titlePulse * 1.4f));

        Panel(new Rect(sw / 2 - 320, sh / 2 - 190, 640, 380));
        GUI.Label(new Rect(sw / 2 - 300, sh / 2 - 170, 600, 80), "STARSHIP CRAFT", sTitle);
        GUI.Label(new Rect(sw / 2 - 300, sh / 2 - 95, 600, 75),
            "Build a block ship. Balance your engines. Survive the belt.", sMed);

        if (GUI.Button(new Rect(sw / 2 - 110, sh / 2 - 10, 220, 52), "BUILD SHIP", sBtn)) EnterBuild(true);
        if (GUI.Button(new Rect(sw / 2 - 110, sh / 2 + 55, 220, 52), "SETTINGS", sBtn)) state = State.Settings;
    }

    void GuiSettings(float sw, float sh)
    {
        Panel(new Rect(sw / 2 - 320, sh / 2 - 230, 640, 470));
        GUI.Label(new Rect(sw / 2 - 300, sh / 2 - 215, 600, 50), "DIFFICULTY", sTitle);

        float y = sh / 2 - 140;
        GameSettings.asteroidCount = SliderRow(sw, ref y, "Asteroids", GameSettings.asteroidCount, 0, 30);
        GameSettings.asteroidSpeed = SliderRowF(sw, ref y, "Asteroid speed", GameSettings.asteroidSpeed, 1f, 20f);
        GameSettings.enemyCount    = SliderRow(sw, ref y, "Enemy ships", GameSettings.enemyCount, 0, 8);
        GameSettings.allyCount     = SliderRow(sw, ref y, "Allied ships", GameSettings.allyCount, 0, 4);
        GameSettings.npcSkill      = SliderRowF(sw, ref y, "NPC speed / skill", GameSettings.npcSkill, 0.5f, 2f);

        y += 14;
        if (GUI.Button(new Rect(sw / 2 - 280, y, 170, 40), "EASY", sBtn))   GameSettings.ApplyEasy();
        if (GUI.Button(new Rect(sw / 2 - 85,  y, 170, 40), "NORMAL", sBtn)) GameSettings.ApplyNormal();
        if (GUI.Button(new Rect(sw / 2 + 110, y, 170, 40), "HARD", sBtn))   GameSettings.ApplyHard();

        if (GUI.Button(new Rect(sw / 2 - 85, y + 55, 170, 44), "BACK", sBtn)) state = State.Menu;
    }

    int SliderRow(float sw, ref float y, string label, int val, int min, int max)
        => Mathf.RoundToInt(SliderRowF(sw, ref y, label, val, min, max));

    float SliderRowF(float sw, ref float y, string label, float val, float min, float max)
    {
        GUI.Label(new Rect(sw / 2 - 280, y, 240, 26), label, sSmall);
        float v = GUI.HorizontalSlider(new Rect(sw / 2 - 20, y + 7, 240, 20), val, min, max);
        GUI.Label(new Rect(sw / 2 + 235, y, 60, 26), v.ToString("0.#"), sSmall);
        y += 42;
        return v;
    }

    void GuiBuild(float sw, float sh)
    {
        Panel(new Rect(10, 10, 350, 306));
        GUI.Label(new Rect(22, 16, 300, 30), "SHIPYARD", sSmall);
        string[] names =
        {
            "1  Hull", "2  Thruster (engine)", "3  RCS (turning jets)", "4  Gun", "5  Armor",
        };
        string[] descs =
        {
            "Structure. Mk II: lighter alloy, takes 2 hits.",
            "Pushes forward from where it sits. More thrusters = more force — big ships need banks of them. Mk II: 1.8× thrust.",
            "Turning jets. Every pod turns you faster, especially mounted far from the center of mass. Mk II: 1.9× authority.",
            "Forward cannon. More guns = faster combined fire. Mk II: quicker, faster bolts.",
            "Heavy plating that soaks 3 hits before breaking. Mk II: 5 hits, heavier.",
        };
        BlockType[] types =
        {
            BlockType.Hull, BlockType.Thruster, BlockType.Steering, BlockType.Gun, BlockType.Armor,
        };
        int selIdx = 0;
        for (int i = 0; i < 5; i++)
        {
            bool sel = builder != null && builder.Selected.type == types[i];
            if (sel) selIdx = i;
            string mk = sel && builder.Selected.mk == 2 ? "  — Mk II ★" : "";
            sSmall.normal.textColor = sel ? Color.white : new Color(0.45f, 0.95f, 1f, 0.75f);
            GUI.Label(new Rect(22, 44 + i * 24, 320, 24), (sel ? "▶ " : "   ") + names[i] + mk, sSmall);
        }
        sSmall.normal.textColor = new Color(0.8f, 0.9f, 1f, 0.9f);
        GUI.Label(new Rect(22, 168, 320, 72), descs[selIdx], sSmall);
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
        GUI.Label(new Rect(22, 244, 320, 52),
            "Same number / Tab — Mk II   ·   LMB place\nRMB remove   ·   WASD orbit   ·   Scroll zoom", sSmall);

        Panel(new Rect(10, sh - 96, 560, 86));
        GUI.Label(new Rect(22, sh - 88, 540, 26),
            $"Blocks {blueprint.Blocks.Count}   Thrusters {blueprint.Count(BlockType.Thruster)}   " +
            $"RCS {blueprint.Count(BlockType.Steering)}   Guns {blueprint.Count(BlockType.Gun)}   " +
            $"Armor {blueprint.Count(BlockType.Armor)}", sSmall);
        sSmall.normal.textColor = ReadyToLaunch() ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.6f, 0.3f);
        GUI.Label(new Rect(22, sh - 60, 540, 26),
            ReadyToLaunch() ? "Press ENTER to launch"
                            : "Add at least one Thruster to launch", sSmall);
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
        GUI.Label(new Rect(22, sh - 36, 540, 24),
            "Tip: keep thrust symmetric around the center of mass", sSmall);
    }

    void GuiPlaying(float sw, float sh)
    {
        Panel(new Rect(10, 10, 240, 92));
        GUI.Label(new Rect(22, 16, 220, 26), $"SCORE   {score}", sSmall);
        GUI.Label(new Rect(22, 40, 220, 26), $"TIME    {playTime:0}s", sSmall);
        GUI.Label(new Rect(22, 64, 220, 26),
            $"BLOCKS  {(PlayerShip != null ? PlayerShip.BlockCount : 0)}", sSmall);

        Panel(new Rect(sw - 250, 10, 240, 68));
        sSmall.alignment = TextAnchor.UpperRight;
        GUI.Label(new Rect(sw - 262, 16, 240, 26), $"HOSTILES  {CountFaction(Faction.Enemy)}", sSmall);
        sSmall.normal.textColor = new Color(0.4f, 1f, 0.6f);
        GUI.Label(new Rect(sw - 262, 40, 240, 26), $"ALLIES  {CountFaction(Faction.Ally)}", sSmall);
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
        sSmall.alignment = TextAnchor.UpperLeft;

        // Crosshair
        float cx = sw / 2f, cy = sh / 2f;
        GUI.color = new Color(0.45f, 0.95f, 1f, 0.8f);
        GUI.DrawTexture(new Rect(cx - 12, cy - 1, 8, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx + 4,  cy - 1, 8, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1, cy - 12, 2, 8), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1, cy + 4,  2, 8), Texture2D.whiteTexture);
        GUI.color = Color.white;

        DrawRadar(sw, sh);
        DrawHelp(sh);
        if (PlayerStranded) GuiStranded(sw, sh);
    }

    // ── Radar ────────────────────────────────────────────────────────────────
    // Top of the radar = the direction your nose points. Blips at the rim are
    // out of range — fly toward them.

    void DrawRadar(float sw, float sh)
    {
        if (PlayerShip == null) return;
        const float R = 82f, range = 260f;
        Vector2 c = new Vector2(sw - 112f, sh - 112f);

        GUI.DrawTexture(new Rect(c.x - R - 8, c.y - R - 8, (R + 8) * 2, (R + 8) * 2), radarTex);

        foreach (var s in Ships)
        {
            if (s == null || s == PlayerShip) continue;
            Color col = s.faction == Faction.Enemy
                ? new Color(1f, 0.3f, 0.25f) : new Color(0.35f, 1f, 0.55f);
            Blip(c, RadarPoint(s.transform.position, range, R, out bool far), col, far ? 5f : 7f, far ? 0.55f : 1f);
        }
        foreach (var a in asteroids)
        {
            if (a == null) continue;
            Vector2 p = RadarPoint(a.transform.position, range, R, out bool far);
            if (!far) Blip(c, p, new Color(0.75f, 0.7f, 0.6f), 3f, 0.6f);
        }

        // The planet: a big blue blip, rim-pinned when far.
        if (Planet.Instance != null)
        {
            Vector2 pp = RadarPoint(Planet.Instance.transform.position, range, R, out bool planetFar);
            Blip(c, pp, new Color(0.45f, 0.65f, 1f), 10f, planetFar ? 0.85f : 1f);
        }

        // Player marker + forward tick.
        Blip(c, Vector2.zero, new Color(0.45f, 0.95f, 1f), 7f, 1f);
        Blip(c, new Vector2(0f, -10f), new Color(0.45f, 0.95f, 1f), 3f, 0.8f);
    }

    Vector2 RadarPoint(Vector3 world, float range, float radius, out bool clamped)
    {
        Vector3 local = PlayerShip.transform.InverseTransformPoint(world);
        Vector2 p = new Vector2(local.x, local.z) / range;
        clamped = p.magnitude > 1f;
        if (clamped) p = p.normalized;
        return new Vector2(p.x, -p.y) * radius;
    }

    static void Blip(Vector2 center, Vector2 offset, Color col, float size, float alpha)
    {
        GUI.color = new Color(col.r, col.g, col.b, alpha);
        GUI.DrawTexture(new Rect(center.x + offset.x - size / 2f,
                                 center.y + offset.y - size / 2f, size, size),
                        Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    Texture2D MakeRadarTex(int s)
    {
        var tx = new Texture2D(s, s, TextureFormat.RGBA32, false);
        float half = (s - 1) * 0.5f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;
                Color col = Color.clear;
                if (d <= 1f)
                {
                    col = new Color(0.02f, 0.07f, 0.11f, 0.8f);
                    bool cross = Mathf.Abs(x - half) < 0.7f || Mathf.Abs(y - half) < 0.7f;
                    bool ring  = Mathf.Abs(d - 0.5f) < 0.012f;
                    if (d > 0.94f)          col = new Color(0.45f, 0.95f, 1f, 0.85f);
                    else if (cross || ring) col = new Color(0.45f, 0.95f, 1f, 0.16f);
                }
                tx.SetPixel(x, y, col);
            }
        tx.Apply();
        return tx;
    }

    // ── Help overlay ─────────────────────────────────────────────────────────

    void DrawHelp(float sh)
    {
        Panel(new Rect(10, sh - 42, 220, 32));
        GUI.Label(new Rect(22, sh - 37, 210, 26),
            showHelp ? "H  —  hide controls" : "H  —  show controls", sSmall);

        if (!showHelp) return;
        Panel(new Rect(10, sh - 332, 310, 282));
        GUI.Label(new Rect(22, sh - 322, 290, 266),
            "W / S — throttle\n" +
            "Mouse — pitch & yaw\n" +
            "Q / E — roll\n" +
            "Shift — boost      X — brake\n" +
            "Space / LMB — fire\n\n" +
            "Radar: top = your nose. Rim blips\n" +
            "are out of range. Blue = planet.\n" +
            "Fly tangent at ~70% speed to\n" +
            "orbit it; ~90% breaks free.\n" +
            "Land at under 14 m/s.", sSmall);
    }

    void GuiStranded(float sw, float sh)
    {
        Panel(new Rect(sw / 2 - 330, sh - 190, 660, 130));
        sMed.normal.textColor = new Color(1f, 0.55f, 0.25f);
        GUI.Label(new Rect(sw / 2 - 320, sh - 182, 640, 40), "STRANDED — ALL ENGINES DESTROYED", sMed);
        sMed.normal.textColor = Color.white;
        GUI.Label(new Rect(sw / 2 - 320, sh - 140, 640, 70),
            "You can't die out here. Guns and RCS still answer — fight on, or press R " +
            "to tow the core back to the shipyard and respawn (score carries over).", sMed);
        sMed.normal.textColor = Color.white;
    }
}
