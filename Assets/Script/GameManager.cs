using System.Collections.Generic;
using UnityEngine;

// Single entry point. Drop this on one empty GameObject in an empty scene.
// Flow: Menu → (Settings) → Build → Playing → GameOver → Build again.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    enum State { Menu, Settings, Multiplayer, Build, Playing }
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

    // Scene swapping: null currentPlanet = space; otherwise we're inside that
    // planet's looping SurfaceWorld and the space content sleeps.
    PlanetDef[] planetDefs;
    Transform spaceRoot;
    SurfaceWorld surface;
    PlanetDef currentPlanet;
    Vector3 entryDir = Vector3.forward;
    Vector3 sunDir = Vector3.down;

    // Deep-space landmarks (asteroid fields, the wreck) for radar and map.
    struct Poi { public string name; public Vector3 pos; }
    readonly List<Poi> pois = new List<Poi>();

    // A drifting cluster of destructible rocks. wreck=true also scatters the
    // hull of a broken capital ship through the middle, beacon still blinking.
    void CreateAsteroidField(string fieldName, Vector3 center, float radius, int count, bool wreck)
    {
        var root = new GameObject(fieldName);
        root.transform.SetParent(spaceRoot, false);
        root.transform.position = center;

        for (int i = 0; i < count; i++)
        {
            float size = Random.Range(3f, 14f);
            float kind = Random.value;
            var rock = new GameObject("FieldRock");
            rock.transform.SetParent(root.transform, false);
            rock.transform.localPosition = Random.insideUnitSphere * radius;
            rock.transform.localRotation = Random.rotation;
            rock.AddComponent<MeshFilter>().mesh =
                MeshFactory.CreateAsteroidMesh(Random.Range(0, 9999), size);
            rock.AddComponent<MeshRenderer>().material =
                kind < 0.6f  ? FX.Standard(new Color(0.45f, 0.4f, 0.38f), Color.black, 0.1f, 0.35f)
              : kind < 0.85f ? FX.Standard(new Color(0.68f, 0.76f, 0.84f), Color.black, 0.15f, 0.8f)
                             : FX.Standard(new Color(0.35f, 0.32f, 0.3f), Color.black, 0.95f, 0.75f);
            rock.AddComponent<SphereCollider>().radius = size * 0.85f;
            rock.AddComponent<Asteroid>().Size = Asteroid.AsteroidSize.Large;
        }

        if (wreck)
        {
            Material scrap = FX.Standard(new Color(0.2f, 0.21f, 0.24f), Color.black, 0.8f, 0.4f);
            for (int i = 0; i < 9; i++)
            {
                var piece = new GameObject("WreckPiece");
                piece.transform.SetParent(root.transform, false);
                piece.transform.localPosition = Random.insideUnitSphere * 90f;
                piece.transform.localRotation = Random.rotation;
                piece.transform.localScale = new Vector3(
                    Random.Range(3f, 9f), Random.Range(1.5f, 4f), Random.Range(6f, 26f));
                piece.AddComponent<MeshFilter>().mesh = MeshFactory.CubeMesh();
                piece.AddComponent<MeshRenderer>().material = scrap;
                piece.AddComponent<BoxCollider>();
            }
            var beacon = new GameObject("DistressBeacon");
            beacon.transform.SetParent(root.transform, false);
            var l = beacon.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.3f, 0.25f);
            l.range = 120f;
            beacon.AddComponent<Carrier.BlinkLight>().phase = 0.3f;
        }

        pois.Add(new Poi { name = fieldName.ToUpper(), pos = center });
    }

    // Big fading title cards ("ENTERING KORRATH — CLOUD BANKS").
    string announceText = "";
    float announceT;
    float menuAngle;

    void Announce(string msg) { announceText = msg; announceT = 4f; }

    // Combat-feel feedback + shipyard toast + multiplayer glue.
    float hitMarkT, dmgT;
    Vector3 dmgDir = Vector3.forward;
    string builderToast = "";
    float toastT;
    string joinIp = "192.168.1.";

    // Which slice of the universe the player occupies (for crew zone-matching).
    public string ZoneName => currentPlanet == null ? "SPACE" : currentPlanet.name;

    public void OnPlayerHitConfirm()
    {
        hitMarkT = 0.22f;
        SFX.Ui(SFX.Id.Confirm, 0.45f);
    }

    public void OnPlayerDamaged(Vector3 towardAttacker)
    {
        if (towardAttacker.sqrMagnitude < 0.01f) return;
        dmgDir = towardAttacker.normalized;
        dmgT = 1.2f;
    }

    public void BuilderToast(string msg) { builderToast = msg; toastT = 2.5f; }

    static string WeatherLabel(WeatherKind k) =>
        k == WeatherKind.Dust ? "GIANT DUST STORM"
        : k == WeatherKind.Snow ? "SNOW STORM"
        : k == WeatherKind.Cloud ? "CLOUD BANKS"
        : k == WeatherKind.Ember ? "ASH & EMBER STORM" : "";
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
        Application.targetFrameRate = 60; // phones default to 30

        cam = Camera.main;
        if (cam == null)
        {
            var g = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = g.AddComponent<Camera>();
            g.AddComponent<AudioListener>();
        }
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.015f, 0.045f);
        cam.farClipPlane    = 48000f; // the whole widened system stays visible

        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.18f, 0.2f, 0.28f);
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.9f;
        sun.color = new Color(0.85f, 0.9f, 1f);
        sun.transform.rotation = Quaternion.Euler(35f, -60f, 0f);
        sunDir = sun.transform.rotation * Vector3.forward;

        // Starfield follows the camera's position (not rotation) so the stars
        // read as infinitely distant no matter how far the player flies.
        starfieldRoot = new GameObject("StarfieldRoot").transform;
        FX.Starfield(starfieldRoot);

        // The space scene: carrier + every planet's globe, all visible at once.
        // Fly into a globe's cloud shell and you enter that planet's own flat,
        // looping surface scene (see EnterPlanet / SurfaceWorld).
        GravityField.Clear();
        spaceRoot = new GameObject("SpaceWorld").transform;
        Carrier.Create(Vector3.zero).transform.SetParent(spaceRoot, true);

        planetDefs = new PlanetDef[]
        {
            new PlanetDef
            {
                name = "Korrath", spacePos = new Vector3(0f, -1600f, 5200f), radius = 1400f,
                land = new Color(0.4f, 0.34f, 0.26f), ocean = new Color(0.08f, 0.28f, 0.5f),
                ground = new Color(0.35f, 0.3f, 0.23f), hasOcean = true, hasBelt = true,
                weather = WeatherKind.Cloud, radarColor = new Color(0.45f, 0.65f, 1f),
                loopSize = 6000f,
            },
            new PlanetDef
            {
                name = "Vessa", spacePos = new Vector3(-8500f, 1600f, -6000f), radius = 650f,
                land = new Color(0.75f, 0.8f, 0.85f), ocean = new Color(0.5f, 0.65f, 0.8f),
                ground = new Color(0.72f, 0.78f, 0.85f), hasOcean = true,
                weather = WeatherKind.Snow, radarColor = new Color(0.6f, 0.8f, 1f),
                loopSize = 4000f,
            },
            new PlanetDef
            {
                name = "Titanhold", spacePos = new Vector3(20000f, -1500f, 3000f), radius = 3500f,
                land = new Color(0.62f, 0.45f, 0.22f),
                ground = new Color(0.55f, 0.4f, 0.22f), hasRing = true,
                weather = WeatherKind.Dust, radarColor = new Color(1f, 0.75f, 0.3f),
                loopSize = 9000f,
            },
            new PlanetDef
            {
                name = "Emberfall", spacePos = new Vector3(-15000f, -2600f, 10500f), radius = 1000f,
                land = new Color(0.3f, 0.14f, 0.1f),
                ground = new Color(0.13f, 0.1f, 0.09f),
                weather = WeatherKind.Ember, radarColor = new Color(1f, 0.45f, 0.25f),
                loopSize = 5000f,
            },
        };
        foreach (var def in planetDefs)
            SpacePlanet.Create(def).transform.SetParent(spaceRoot, true);

        // Deep-space points of interest — landmarks (and cover) on the long
        // hauls between worlds. The Graveyard hides a wrecked capital ship.
        CreateAsteroidField("Shatter Field", new Vector3(7500f, 600f, -2500f), 700f, 42, false);
        CreateAsteroidField("The Spindle", new Vector3(-4500f, -900f, 8500f), 550f, 30, false);
        CreateAsteroidField("The Graveyard", new Vector3(9500f, -2200f, 9500f), 850f, 48, true);

        // The star itself: a blazing disc with a soft halo, hung where the
        // directional light actually comes from.
        var star = new GameObject("Star");
        star.transform.SetParent(spaceRoot, false);
        star.transform.position = -sunDir * 9500f;
        star.transform.localScale = Vector3.one * 520f;
        star.AddComponent<MeshFilter>().mesh = MeshFactory.CreateSphereMesh();
        star.AddComponent<MeshRenderer>().material =
            FX.Standard(Color.white, new Color(1f, 0.95f, 0.8f) * 3.5f, 0f, 0.5f);
        var halo = new GameObject("Halo");
        halo.transform.SetParent(star.transform, false);
        halo.transform.localScale = Vector3.one * 3.2f;
        halo.AddComponent<MeshFilter>().mesh = MeshFactory.CreateSphereMesh();
        halo.AddComponent<MeshRenderer>().material = FX.Ghost(new Color(1f, 0.9f, 0.6f, 0.06f));

        new GameObject("Weather").AddComponent<Weather>();

        mapCenter = Vector3.zero;
        foreach (var def in planetDefs) mapCenter += def.spacePos;
        mapCenter /= planetDefs.Length + 1; // + carrier at origin

        new GameObject("NetLink").AddComponent<NetLink>();

        // Your last design survives between sessions (same codec the network
        // and clipboard sharing use).
        var saved = NetCodec.Decode(PlayerPrefs.GetString("ship", ""));
        blueprint = saved != null && saved.Blocks.Count > 1 ? saved : DefaultPlayerBlueprint();
    }

    void Update()
    {
        if (starfieldRoot != null) starfieldRoot.position = cam.transform.position;
        if (announceT > 0f) announceT -= Time.deltaTime;
        if (hitMarkT > 0f) hitMarkT -= Time.deltaTime;
        if (dmgT > 0f) dmgT -= Time.deltaTime;
        if (toastT > 0f) toastT -= Time.deltaTime;

        TouchControls.ShipyardMode = state == State.Build;
        TouchControls.Poll();
        if (TouchControls.MapTap)
        {
            TouchControls.MapTap = false;
            if (state == State.Playing) ToggleMap();
        }

        switch (state)
        {
            case State.Menu:
            case State.Settings:
            case State.Multiplayer:
                // Cinematic drift around the carrier while the menus are up.
                menuAngle += Time.deltaTime * 4f;
                var mrot = Quaternion.Euler(10f, menuAngle, 0f);
                cam.transform.position = mrot * new Vector3(0f, 32f, -150f);
                cam.transform.LookAt(new Vector3(0f, 8f, 0f));
                break;

            case State.Build:
                if (Input.GetKeyDown(KeyCode.Return) && ReadyToLaunch()) Launch();
                break;

            case State.Playing:
                playTime += Time.deltaTime;
                if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;
                if (Input.GetKeyDown(KeyCode.M)) ToggleMap();
                if (!mapView)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1)) camMode = 1;
                    if (Input.GetKeyDown(KeyCode.Alpha2)) camMode = 2;
                    if (Input.GetKeyDown(KeyCode.Alpha3)) camMode = 3;
                }
                if (PlayerStranded && Input.GetKeyDown(KeyCode.R)) EnterBuild(false);
                if (Carrier.Instance != null && Carrier.Instance.CanRefit(PlayerShip) &&
                    Input.GetKeyDown(KeyCode.E)) EnterBuild(false);
                CheckSceneTransitions();
                MaintainPopulation();
                if (mapView) MapCamera();
                else FollowCamera();
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

        Vector3 spawn = Carrier.Instance != null ? Carrier.Instance.RespawnPoint : Vector3.zero;
        PlayerShip = SpawnShip(blueprint, Faction.Player, spawn, Quaternion.identity);
        PlayerShip.gameObject.AddComponent<PlayerController>();

        for (int i = 0; i < GameSettings.allyCount; i++) SpawnAlly();
        for (int i = 0; i < GameSettings.enemyCount; i++) SpawnEnemy();
        for (int i = 0; i < GameSettings.asteroidCount; i++) SpawnAsteroidNearPlayer();

        cam.transform.position = PlayerShip.transform.position - Vector3.forward * 18f + Vector3.up * 6f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        state = State.Playing;
        Announce("ALL SYSTEMS ONLINE — GOOD HUNTING");
        SFX.Ui(SFX.Id.Warp, 0.8f);

        // Persist the design and tell the crew what we're flying.
        string code = NetCodec.Encode(blueprint);
        PlayerPrefs.SetString("ship", code);
        PlayerPrefs.Save();
        if (NetLink.Instance != null && NetLink.Instance.Active)
            NetLink.Instance.Session.SetLocalShipCode(code);
    }

    void ClearWorld()
    {
        foreach (var s in Ships) if (s != null) Destroy(s.gameObject);
        Ships.Clear();
        PlayerShip = null;
        foreach (var a in asteroids) if (a != null) Destroy(a.gameObject);
        asteroids.Clear();
        foreach (var b in FindObjectsOfType<Bullet>()) Destroy(b.gameObject);

        // Always rebuild from space — tear down any surface scene.
        if (surface != null) { Destroy(surface.gameObject); surface = null; }
        currentPlanet = null;
        GravityField.Clear();
        if (spaceRoot != null) spaceRoot.gameObject.SetActive(true);
    }

    // ── Scene swapping (No Man's Sky-style) ──────────────────────────────────

    void CheckSceneTransitions()
    {
        if (PlayerShip == null) return;
        if (currentPlanet == null)
        {
            foreach (var def in planetDefs)
                if ((PlayerShip.transform.position - def.spacePos).magnitude < def.EntryRadius)
                {
                    EnterPlanet(def);
                    break;
                }
        }
        else if (PlayerShip.transform.position.y - surface.Center.y > SurfaceWorld.CloudTop + 60f)
        {
            ExitPlanet();
        }
    }

    // Punch through the cloud shell → the planet's own flat, looping scene.
    void EnterPlanet(PlanetDef def)
    {
        currentPlanet = def;
        entryDir = (PlayerShip.transform.position - def.spacePos).normalized;
        DespawnTransient();
        spaceRoot.gameObject.SetActive(false);
        GravityField.Clear();
        surface = SurfaceWorld.Create(def, new Vector3(0f, -60000f, 0f));

        float speed = PlayerShip.Body.velocity.magnitude;
        PlayerShip.transform.position = surface.Center + Vector3.up * 600f;
        Vector3 level = Vector3.ProjectOnPlane(PlayerShip.transform.forward, Vector3.up);
        if (level.sqrMagnitude < 0.01f) level = Vector3.forward;
        PlayerShip.transform.rotation =
            Quaternion.LookRotation((level.normalized + Vector3.down * 0.35f).normalized);
        if (!PlayerShip.Body.isKinematic)
            PlayerShip.Body.velocity = PlayerShip.transform.forward * Mathf.Clamp(speed, 15f, 45f);

        SnapCamera();
        FX.Flash(PlayerShip.transform.position, Color.white, 5f, 0.5f);
        SFX.Ui(SFX.Id.Warp, 1f, 1.1f);
        Announce($"ENTERING {def.name.ToUpper()} — {WeatherLabel(def.weather)}");
    }

    // Climb out of the clouds → back to space, just off the globe's shell.
    void ExitPlanet()
    {
        var def = currentPlanet;
        currentPlanet = null;
        DespawnTransient();
        Destroy(surface.gameObject);
        surface = null;
        GravityField.Clear();
        spaceRoot.gameObject.SetActive(true);

        float speed = PlayerShip.Body.velocity.magnitude;
        PlayerShip.transform.position = def.spacePos + entryDir * (def.EntryRadius + 150f);
        PlayerShip.transform.rotation = Quaternion.LookRotation(entryDir);
        if (!PlayerShip.Body.isKinematic)
            PlayerShip.Body.velocity = entryDir * Mathf.Max(speed, 25f);

        SnapCamera();
        FX.Flash(PlayerShip.transform.position, Color.white, 5f, 0.5f);
        SFX.Ui(SFX.Id.Warp, 1f, 0.9f);
        Announce($"LEAVING {def.name.ToUpper()} — OPEN SPACE");
    }

    // NPCs, rocks and bolts stay behind when the scene changes; population
    // control refills the new scene within seconds.
    void DespawnTransient()
    {
        for (int i = Ships.Count - 1; i >= 0; i--)
            if (Ships[i] != null && Ships[i] != PlayerShip)
            {
                Destroy(Ships[i].gameObject);
                Ships.RemoveAt(i);
            }
        foreach (var a in asteroids) if (a != null) Destroy(a.gameObject);
        asteroids.Clear();
        foreach (var b in FindObjectsOfType<Bullet>()) Destroy(b.gameObject);
    }

    void SnapCamera()
    {
        var t = PlayerShip.transform;
        cam.transform.position = t.position - t.forward * 16f + t.up * 5f;
        cam.transform.LookAt(t.position + t.forward * 6f, t.up);
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

    // Inside a surface scene, keep spawns above the ground and below the clouds.
    Vector3 ClampToScene(Vector3 pos)
    {
        if (surface != null)
            pos.y = Mathf.Clamp(pos.y, surface.Center.y + 40f,
                                surface.Center.y + SurfaceWorld.CloudTop - 60f);
        return pos;
    }

    void SpawnEnemy()
    {
        if (PlayerShip == null) return;
        Vector3 pos = ClampToScene(PlayerShip.transform.position + Random.onUnitSphere * NPCSpawnRadius);
        bool heavy = Random.value < 0.22f; // armored gunship: tougher, worth 900
        var s = SpawnShip(heavy ? HeavyBlueprint() : NPCBlueprint(), Faction.Enemy, pos,
            Quaternion.LookRotation(PlayerShip.transform.position - pos));
        s.scoreValue = heavy ? 900 : 500;
        s.gameObject.AddComponent<NPCController>();
    }

    void SpawnAlly()
    {
        if (PlayerShip == null) return;
        Vector3 pos = ClampToScene(PlayerShip.transform.position
                    + PlayerShip.transform.right * Random.Range(-20f, 20f)
                    + PlayerShip.transform.up * 8f - PlayerShip.transform.forward * 10f);
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
        // Rock, ice or nickel-iron — a little mineral variety in the belt.
        float kind = Random.value;
        go.AddComponent<MeshRenderer>().material =
            kind < 0.6f  ? FX.Standard(new Color(0.45f, 0.4f, 0.38f), Color.black, 0.1f, 0.35f)
          : kind < 0.85f ? FX.Standard(new Color(0.68f, 0.76f, 0.84f), Color.black, 0.15f, 0.8f)
                         : FX.Standard(new Color(0.35f, 0.32f, 0.3f), Color.black, 0.95f, 0.75f);

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

        // No asteroid rain under a gravity field — canyon flying stays clean.
        bool inGravity = PlayerShip != null &&
            GravityField.Sample(PlayerShip.transform.position).sqrMagnitude > 0.01f;

        asteroidTimer -= Time.deltaTime;
        if (!inGravity && asteroids.Count < GameSettings.asteroidCount && asteroidTimer <= 0f)
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
        if (ship.faction == Faction.Enemy) score += ship.scoreValue;
    }

    // Called the moment the last engine dies. Play stays live — the stranded
    // banner and respawn key are handled in Update/GuiPlaying.
    public void OnPlayerStranded()
    {
        CameraShake(1.5f);
        SFX.Ui(SFX.Id.Hurt, 1f, 0.65f);
        Announce("STRANDED — ALL ENGINES LOST");
    }

    public void CameraShake(float amount) => shake = Mathf.Max(shake, amount);

    // ── Camera ───────────────────────────────────────────────────────────────

    void FollowCamera()
    {
        if (PlayerShip == null) return;
        if (camMode == 3) { FreeCamera(); return; }

        var t = PlayerShip.transform;
        bool rear = camMode == 2;
        Vector3 target = t.position + t.forward * (rear ? 16f : -16f) + t.up * 5f;
        cam.transform.position = Vector3.Lerp(cam.transform.position, target, Time.deltaTime * 5f);

        shake = Mathf.Lerp(shake, 0f, Time.deltaTime * 4f);
        cam.transform.position += Random.insideUnitSphere * shake * 0.4f;
        cam.transform.LookAt(t.position + t.forward * (rear ? -6f : 6f), t.up);

        // FOV kick while boosting sells the speed; turbo stretches it further.
        float targetFov = PlayerShip.TurboActive ? 84f
            : PlayerShip.Boost && Mathf.Abs(PlayerShip.ThrustInput) > 0.1f ? 72f : 60f;
        fov = Mathf.Lerp(fov, targetFov, Time.deltaTime * 5f);
        cam.fieldOfView = fov;
    }

    // Spectator cam: mouse looks, WASD flies, Q/E down/up, Shift is fast.
    // The ship drifts untouched while you frame the shot.
    void FreeCamera()
    {
        var e = cam.transform.eulerAngles;
        float pitch = e.x > 180f ? e.x - 360f : e.x;
        pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2f, -89f, 89f);
        cam.transform.rotation = Quaternion.Euler(pitch, e.y + Input.GetAxis("Mouse X") * 2f, 0f);

        Vector3 move = cam.transform.rotation * new Vector3(
            Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        move.y += (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        float speed = Input.GetKey(KeyCode.LeftShift) ? 200f : 50f;
        cam.transform.position += move * speed * Time.deltaTime;
        cam.fieldOfView = 60f;
    }

    void ToggleMap()
    {
        mapView = !mapView;
        Cursor.lockState = mapView ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = mapView;
    }

    // Orbit the real scene from high above — the world is its own map.
    void MapCamera()
    {
        if (Input.GetMouseButton(0))
        {
            mapYaw += Input.GetAxis("Mouse X") * 3f;
            mapPitch = Mathf.Clamp(mapPitch - Input.GetAxis("Mouse Y") * 3f, 10f, 85f);
        }
        mapDist = Mathf.Clamp(mapDist - Input.GetAxis("Mouse ScrollWheel") * 5000f, 2500f, 45000f);
        var rot = Quaternion.Euler(mapPitch, mapYaw, 0f);
        Vector3 center = currentPlanet == null || surface == null
            ? mapCenter
            : surface.Center + Vector3.up * 300f;
        float dist = currentPlanet == null ? mapDist : Mathf.Min(mapDist, 5000f);
        cam.transform.position = center + rot * new Vector3(0f, 0f, -dist);
        cam.transform.rotation = rot;
        cam.fieldOfView = 60f;
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

    // Armored gunship: an armor prow, four engines, triple guns. Slower to
    // turn, hard to crack, pays a 900-point bounty.
    static ShipBlueprint HeavyBlueprint()
    {
        int mk = GameSettings.npcSkill > 1.3f ? 2 : 1;
        var bp = new ShipBlueprint();
        bp.TryAdd(new Vector3Int(1, 0, 0),  new BlockDef(BlockType.Hull, mk));
        bp.TryAdd(new Vector3Int(-1, 0, 0), new BlockDef(BlockType.Hull, mk));
        bp.TryAdd(new Vector3Int(0, 1, 0),  new BlockDef(BlockType.Hull, mk));
        bp.TryAdd(new Vector3Int(0, -1, 0), new BlockDef(BlockType.Hull, mk));
        bp.TryAdd(new Vector3Int(0, 0, 1),  new BlockDef(BlockType.Hull, mk));
        bp.TryAdd(new Vector3Int(0, 0, -1), new BlockDef(BlockType.Hull, mk));
        bp.TryAdd(new Vector3Int(1, 0, 1),  new BlockDef(BlockType.Armor, mk));
        bp.TryAdd(new Vector3Int(-1, 0, 1), new BlockDef(BlockType.Armor, mk));
        bp.TryAdd(new Vector3Int(0, 0, 2),  new BlockDef(BlockType.Gun, mk));
        bp.TryAdd(new Vector3Int(1, 1, 0),  new BlockDef(BlockType.Gun));
        bp.TryAdd(new Vector3Int(-1, 1, 0), new BlockDef(BlockType.Gun));
        bp.TryAdd(new Vector3Int(1, 0, -1), new BlockDef(BlockType.Thruster, mk));
        bp.TryAdd(new Vector3Int(-1, 0, -1), new BlockDef(BlockType.Thruster, mk));
        bp.TryAdd(new Vector3Int(0, 1, -1), new BlockDef(BlockType.Thruster, mk));
        bp.TryAdd(new Vector3Int(0, -1, -1), new BlockDef(BlockType.Thruster, mk));
        bp.TryAdd(new Vector3Int(1, -1, 0), new BlockDef(BlockType.Steering, mk));
        bp.TryAdd(new Vector3Int(-1, -1, 0), new BlockDef(BlockType.Steering, mk));
        return bp;
    }

    // ── HUD ──────────────────────────────────────────────────────────────────

    GUIStyle sTitle, sMed, sSmall, sBtn;
    Texture2D boxTex, radarTex;
    bool stylesBuilt;
    bool showHelp;
    float titlePulse;
    Rect radarRect;

    // Camera modes: 1 chase, 2 rear view, 3 free spectator cam.
    int camMode = 1;

    // 3D system map (M or click the radar): orbits the real scene from afar.
    bool mapView;
    float mapYaw = 30f, mapPitch = 55f, mapDist = 16000f;
    Vector3 mapCenter;

    // Free cam and the map both steal WASD/mouse from the ship.
    public bool ShipInputSuspended => mapView || camMode == 3;

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

        radarTex = MakeRadarTex(256);
    }

    void Panel(Rect r) => GUI.DrawTexture(r, boxTex);

    float uiScale = 1f;

    void OnGUI()
    {
        BuildStyles();
        // One scale factor keeps the HUD legible on 4K monitors and phones;
        // GUI.matrix also remaps IMGUI event coordinates, so buttons still work.
        uiScale = TouchControls.Enabled
            ? Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 480f)
            : Mathf.Max(1f, Screen.height / 1400f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
        float sw = Screen.width / uiScale, sh = Screen.height / uiScale;

        switch (state)
        {
            case State.Menu:        GuiMenu(sw, sh); break;
            case State.Settings:    GuiSettings(sw, sh); break;
            case State.Multiplayer: GuiMultiplayer(sw, sh); break;
            case State.Build:       GuiBuild(sw, sh); break;
            case State.Playing:     GuiPlaying(sw, sh); break;
        }

        if (state == State.Playing || state == State.Build)
            TouchControls.Draw(uiScale, state == State.Playing && !mapView);

        // Title card: fades in the last second of its life.
        if (state == State.Playing && announceT > 0f && announceText.Length > 0)
        {
            float a = Mathf.Clamp01(announceT);
            sMed.fontSize = 30;
            sMed.normal.textColor = new Color(0.75f, 0.95f, 1f, a);
            GUI.Label(new Rect(sw / 2 - 400, sh * 0.2f, 800, 44), announceText, sMed);
            sMed.fontSize = 24;
            sMed.normal.textColor = Color.white;
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

        if (GUI.Button(new Rect(sw / 2 - 110, sh / 2 - 25, 220, 48), "SINGLE PLAYER", sBtn))
        { SFX.Ui(SFX.Id.Click); EnterBuild(true); }
        if (GUI.Button(new Rect(sw / 2 - 110, sh / 2 + 32, 220, 48), "MULTIPLAYER — LAN", sBtn))
        { SFX.Ui(SFX.Id.Click); state = State.Multiplayer; }
        if (GUI.Button(new Rect(sw / 2 - 110, sh / 2 + 89, 220, 48), "SETTINGS", sBtn))
        { SFX.Ui(SFX.Id.Click); state = State.Settings; }
    }

    // ── Multiplayer lobby: player-hosted LAN, no servers to run ──────────────

    void GuiMultiplayer(float sw, float sh)
    {
        var net = NetLink.Instance;
        Panel(new Rect(sw / 2 - 320, sh / 2 - 220, 640, 440));
        GUI.Label(new Rect(sw / 2 - 300, sh / 2 - 205, 600, 50), "LAN CO-OP — BETA", sTitle);
        GUI.Label(new Rect(sw / 2 - 280, sh / 2 - 135, 560, 70),
            "One player hosts — their machine is the server. Everyone else joins the " +
            "host's IP on the same network. Crews see each other's ships and designs; " +
            "combat and NPCs stay local in this beta.", sMed);

        float y = sh / 2 - 40;
        if (net != null && net.Active)
        {
            sMed.normal.textColor = new Color(0.4f, 1f, 0.6f);
            GUI.Label(new Rect(sw / 2 - 280, y, 560, 32), net.Session.Status, sMed);
            sMed.normal.textColor = Color.white;
            if (GUI.Button(new Rect(sw / 2 - 235, y + 50, 220, 46), "LAUNCH", sBtn))
            { SFX.Ui(SFX.Id.Click); EnterBuild(true); }
            if (GUI.Button(new Rect(sw / 2 + 15, y + 50, 220, 46), "DISCONNECT", sBtn))
            { SFX.Ui(SFX.Id.Click); net.Session.Shutdown(); }
        }
        else
        {
            if (GUI.Button(new Rect(sw / 2 - 235, y, 220, 46), "HOST A GAME", sBtn))
            {
                SFX.Ui(SFX.Id.Click);
                if (NetLink.Instance.Session.StartHost()) EnterBuild(true);
            }
            GUI.Label(new Rect(sw / 2 + 15, y - 26, 220, 24), "Host's IP address:", sSmall);
            joinIp = GUI.TextField(new Rect(sw / 2 + 15, y, 220, 30), joinIp, 24);
            if (GUI.Button(new Rect(sw / 2 + 15, y + 38, 220, 40), "JOIN", sBtn))
            {
                SFX.Ui(SFX.Id.Click);
                if (NetLink.Instance.Session.Join(joinIp.Trim())) EnterBuild(true);
            }
            if (net != null && net.Session.Status.Length > 0)
            {
                sSmall.normal.textColor = new Color(1f, 0.6f, 0.3f);
                GUI.Label(new Rect(sw / 2 - 280, y + 92, 560, 26), net.Session.Status, sSmall);
                sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
            }
        }

        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f, 0.6f);
        GUI.Label(new Rect(sw / 2 - 280, sh / 2 + 130, 560, 26),
            "Internet matchmaking — coming later. Port 7777 must be reachable on the LAN.", sSmall);
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
        if (GUI.Button(new Rect(sw / 2 - 85, sh / 2 + 160, 170, 44), "BACK", sBtn))
        { SFX.Ui(SFX.Id.Click); state = State.Menu; }
    }

    void GuiSettings(float sw, float sh)
    {
        Panel(new Rect(sw / 2 - 320, sh / 2 - 280, 640, 580));
        GUI.Label(new Rect(sw / 2 - 300, sh / 2 - 265, 600, 50), "SETTINGS", sTitle);

        float y = sh / 2 - 195;
        GameSettings.asteroidCount = SliderRow(sw, ref y, "Asteroids", GameSettings.asteroidCount, 0, 30);
        GameSettings.asteroidSpeed = SliderRowF(sw, ref y, "Asteroid speed", GameSettings.asteroidSpeed, 1f, 20f);
        GameSettings.enemyCount    = SliderRow(sw, ref y, "Enemy ships", GameSettings.enemyCount, 0, 8);
        GameSettings.allyCount     = SliderRow(sw, ref y, "Allied ships", GameSettings.allyCount, 0, 4);
        GameSettings.npcSkill      = SliderRowF(sw, ref y, "NPC speed / skill", GameSettings.npcSkill, 0.5f, 2f);
        GameSettings.mouseSens     = SliderRowF(sw, ref y, "Mouse sensitivity", GameSettings.mouseSens, 0.2f, 1.5f);
        GameSettings.volume        = SliderRowF(sw, ref y, "Sound volume", GameSettings.volume, 0f, 1f);
        GameSettings.invertY = GUI.Toggle(new Rect(sw / 2 - 20, y, 260, 26),
            GameSettings.invertY, "  Invert mouse Y");
        y += 36;

        y += 8;
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
        Panel(new Rect(10, 10, 350, 332));
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
            var row = new Rect(22, 44 + i * 24, 320, 24);
            GUI.Label(row, (sel ? "▶ " : "   ") + names[i] + mk, sSmall);
            // Rows are tappable/clickable too (again on the selected row = Mk toggle).
            if (builder != null && GUI.Button(row, "", GUIStyle.none))
            { SFX.Ui(SFX.Id.Click, 0.5f); builder.SelectFromUi(types[i]); }
        }
        sSmall.normal.textColor = new Color(0.8f, 0.9f, 1f, 0.9f);
        GUI.Label(new Rect(22, 168, 320, 72), descs[selIdx], sSmall);
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
        GUI.Label(new Rect(22, 244, 320, 78),
            "Same number / Tab — Mk II   ·   LMB place\nRMB remove   ·   WASD orbit   ·   Scroll zoom\nC — copy ship code   ·   V — paste ship code", sSmall);

        if (toastT > 0f)
        {
            sMed.normal.textColor = new Color(0.4f, 1f, 0.7f, Mathf.Clamp01(toastT));
            GUI.Label(new Rect(sw / 2 - 300, sh - 140, 600, 34), builderToast, sMed);
            sMed.normal.textColor = Color.white;
        }

        // Engineering readout: live stats + a balance verdict, so builders can
        // see what the physics will do before they launch.
        {
            float mass = 0f, thrustN = 0f, heat = 0f;
            Vector3 com = Vector3.zero, tc = Vector3.zero;
            foreach (var kv in blueprint.Blocks)
            {
                float m = ShipBlueprint.MassOf(kv.Value);
                mass += m;
                com += (Vector3)kv.Key * m;
                if (kv.Value.type == BlockType.Thruster)
                {
                    float t = 220f * ShipBlueprint.ThrustMult(kv.Value);
                    thrustN += t;
                    tc += (Vector3)kv.Key * t;
                    heat += kv.Value.mk == 2 ? 10f : 4f;
                }
            }
            com /= mass;
            float steer = 0.35f;
            foreach (var kv in blueprint.Blocks)
                if (kv.Value.type == BlockType.Steering)
                    steer += (0.7f + 0.5f * ((Vector3)kv.Key - com).magnitude)
                           * ShipBlueprint.SteerMult(kv.Value);

            Panel(new Rect(sw - 340, 10, 330, 150));
            GUI.Label(new Rect(sw - 328, 16, 310, 26), "ENGINEERING", sSmall);
            GUI.Label(new Rect(sw - 328, 42, 310, 26),
                $"Mass {mass:0.0}    Accel {(mass > 0 ? thrustN / mass : 0):0.0} m/s²", sSmall);
            GUI.Label(new Rect(sw - 328, 66, 310, 26),
                $"Turn {steer:0.0}    Turbo pool {heat:0}s", sSmall);
            if (thrustN > 0f)
            {
                Vector2 off = new Vector2(tc.x / thrustN - com.x, tc.y / thrustN - com.y);
                bool balanced = off.magnitude < 0.25f;
                sSmall.normal.textColor = balanced
                    ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 0.6f, 0.3f);
                GUI.Label(new Rect(sw - 328, 94, 310, 48), balanced
                    ? "Thrust balanced — flies straight"
                    : "Thrust off-center — ship will veer\n(align orange & yellow markers)", sSmall);
                sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
            }
        }

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

        GUI.enabled = ReadyToLaunch();
        if (GUI.Button(new Rect(sw - 200, sh - 76, 186, 56), "LAUNCH ▶", sBtn))
        { SFX.Ui(SFX.Id.Click); Launch(); }
        GUI.enabled = true;
    }

    void GuiPlaying(float sw, float sh)
    {
        if (mapView) { GuiMap(sw, sh); return; }

        Panel(new Rect(10, 10, 250, 142));
        GUI.Label(new Rect(22, 16, 230, 26), $"SCORE   {score}", sSmall);
        GUI.Label(new Rect(22, 40, 230, 26), $"TIME    {playTime:0}s", sSmall);
        GUI.Label(new Rect(22, 64, 230, 26),
            $"BLOCKS  {(PlayerShip != null ? PlayerShip.BlockCount : 0)}", sSmall);
        float spd = PlayerShip != null && PlayerShip.Body != null && !PlayerShip.Body.isKinematic
            ? PlayerShip.Body.velocity.magnitude : 0f;
        string altText = currentPlanet != null && surface != null && PlayerShip != null
            ? $"   ALT {PlayerShip.transform.position.y - surface.Center.y:0}m" : "";
        GUI.Label(new Rect(22, 88, 230, 26), $"SPEED   {spd:0} m/s{altText}", sSmall);
        if (PlayerShip != null && (PlayerShip.Anchored || PlayerShip.ArmorMode))
        {
            sSmall.normal.textColor = PlayerShip.Anchored
                ? new Color(1f, 0.7f, 0.25f) : new Color(0.4f, 1f, 0.6f);
            GUI.Label(new Rect(22, 114, 230, 26),
                (PlayerShip.Anchored ? "ANCHORED (G)  " : "") +
                (PlayerShip.ArmorMode ? "ARMOR UP (F)" : ""), sSmall);
            sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
        }

        bool crewed = NetLink.Instance != null && NetLink.Instance.Active;
        Panel(new Rect(sw - 250, 10, 240, crewed ? 92 : 68));
        sSmall.alignment = TextAnchor.UpperRight;
        GUI.Label(new Rect(sw - 262, 16, 240, 26), $"HOSTILES  {CountFaction(Faction.Enemy)}", sSmall);
        sSmall.normal.textColor = new Color(0.4f, 1f, 0.6f);
        GUI.Label(new Rect(sw - 262, 40, 240, 26), $"ALLIES  {CountFaction(Faction.Ally)}", sSmall);
        if (crewed)
        {
            sSmall.normal.textColor = new Color(0.3f, 0.9f, 1f);
            GUI.Label(new Rect(sw - 262, 64, 240, 26), $"CREW  {NetLink.Instance.CrewCount}", sSmall);
        }
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

        // Hit marker: an X flare on the crosshair when your shot lands.
        if (hitMarkT > 0f)
        {
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
            GUI.color = new Color(1f, 0.55f, 0.3f, Mathf.Clamp01(hitMarkT / 0.22f));
            GUI.DrawTexture(new Rect(cx - 16, cy - 1.5f, 10, 3), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 6,  cy - 1.5f, 10, 3), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1.5f, cy - 16, 3, 10), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1.5f, cy + 6,  3, 10), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        // Damage direction: a red bar orbiting the crosshair, pointing at
        // whoever just hit you.
        if (dmgT > 0f && PlayerShip != null)
        {
            Vector3 local = cam.transform.InverseTransformDirection(dmgDir);
            float aDeg = Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(aDeg, new Vector2(cx, cy));
            GUI.color = new Color(1f, 0.3f, 0.25f, Mathf.Clamp01(dmgT / 1.2f));
            GUI.DrawTexture(new Rect(cx - 16, cy - 92, 32, 7), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        DrawLeadReticle(sw, sh);

        DrawTurboBar(sw, sh);
        if (Carrier.Instance != null && Carrier.Instance.CanRefit(PlayerShip))
        {
            if (GUI.Button(new Rect(sw / 2 - 160, sh / 2 + 60, 320, 44),
                TouchControls.Enabled ? "REFIT AT THE HANGAR" : "E  —  REFIT AT THE HANGAR", sBtn))
            { SFX.Ui(SFX.Id.Click); EnterBuild(false); }
        }

        DrawRadar(sw, sh);
        DrawHelp(sh);
        if (PlayerStranded) GuiStranded(sw, sh);
    }

    // Lead reticle: for the nearest hostile roughly ahead, mark where a bolt
    // fired NOW would meet them — put the crosshair on the diamond, not the ship.
    void DrawLeadReticle(float sw, float sh)
    {
        if (PlayerShip == null || PlayerShip.Body == null) return;
        Vector3 myPos = PlayerShip.transform.position;
        Vector3 myVel = PlayerShip.Body.isKinematic ? Vector3.zero : PlayerShip.Body.velocity;

        Ship best = null;
        float bestAngle = 32f;
        foreach (var s in Ships)
        {
            if (s == null || s.faction != Faction.Enemy || s.Body == null) continue;
            Vector3 to = s.transform.position - myPos;
            if (to.sqrMagnitude > 300f * 300f) continue;
            float ang = Vector3.Angle(PlayerShip.transform.forward, to);
            if (ang < bestAngle) { bestAngle = ang; best = s; }
        }
        if (best == null) return;

        Vector3 relPos = best.transform.position - myPos;
        Vector3 relVel = (best.Body.isKinematic ? Vector3.zero : best.Body.velocity) - myVel;
        float t;
        if (!Targeting.Lead(relPos, relVel, 90f, out t) || t > 5f) return;

        Vector3 aim = best.transform.position + relVel * t;
        Vector3 sp = cam.WorldToScreenPoint(aim);
        if (sp.z < 0f) return;
        float x = sp.x / uiScale, y = sh - sp.y / uiScale;

        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, new Vector2(x, y));
        GUI.color = new Color(1f, 0.4f, 0.3f, 0.9f);
        GUI.DrawTexture(new Rect(x - 9, y - 9, 18, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 9, y + 7, 18, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x - 9, y - 9, 2, 18), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x + 7, y - 9, 2, 18), Texture2D.whiteTexture);
        GUI.matrix = m;
        GUI.color = Color.white;
    }

    // Turbo heat pool — drained by turbo, refilled while cruising.
    void DrawTurboBar(float sw, float sh)
    {
        if (PlayerShip == null) return;
        float w = 220f, h = 10f, x = sw / 2f - w / 2f, y = sh - 34f;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
        float f = PlayerShip.Heat01;
        GUI.color = PlayerShip.TurboActive ? new Color(1f, 0.55f, 0.15f)
                  : Color.Lerp(new Color(1f, 0.4f, 0.3f), new Color(0.35f, 0.9f, 1f), f);
        GUI.DrawTexture(new Rect(x, y, w * f, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        sSmall.alignment = TextAnchor.UpperCenter;
        GUI.Label(new Rect(x, y - 24, w, 22),
            PlayerShip.TurboActive ? "TURBO" : "TURBO (T)", sSmall);
        sSmall.alignment = TextAnchor.UpperLeft;
    }

    // ── Radar ────────────────────────────────────────────────────────────────
    // Top of the radar = the direction your nose points. Blips at the rim are
    // out of range — fly toward them.

    void DrawRadar(float sw, float sh)
    {
        if (PlayerShip == null) return;
        const float R = 112f, range = 260f;
        Vector2 c = new Vector2(sw - 142f, sh - 142f);

        radarRect = new Rect(c.x - R - 10, c.y - R - 10, (R + 10) * 2, (R + 10) * 2);
        GUI.DrawTexture(radarRect, radarTex);
        if (Event.current.type == EventType.MouseDown &&
            radarRect.Contains(Event.current.mousePosition)) ToggleMap();

        foreach (var s in Ships)
        {
            if (s == null || s == PlayerShip) continue;
            Color col = s.faction == Faction.Enemy
                ? new Color(1f, 0.3f, 0.25f) : new Color(0.35f, 1f, 0.55f);
            Blip(c, RadarPoint(s.transform.position, range, R, out bool far), col, far ? 7f : 9f, far ? 0.55f : 1f);
        }
        foreach (var a in asteroids)
        {
            if (a == null) continue;
            Vector2 p = RadarPoint(a.transform.position, range, R, out bool far);
            if (!far) Blip(c, p, new Color(0.75f, 0.7f, 0.6f), 4f, 0.6f);
        }

        // Nav blips (space only): every planet plus the carrier — rim-pinned
        // when far, so they double as compass needles. Inside a surface scene
        // the sky is the only exit, so there is nothing to point at.
        if (currentPlanet == null)
        {
            foreach (var def in planetDefs)
            {
                Vector2 sp = RadarPoint(def.spacePos, range, R, out bool srcFar);
                Blip(c, sp, def.radarColor, 13f, srcFar ? 0.85f : 1f);
            }
            foreach (var poi in pois) // faint gray: asteroid fields, the wreck
            {
                Vector2 pp = RadarPoint(poi.pos, range, R, out bool poiFar);
                Blip(c, pp, new Color(0.7f, 0.75f, 0.8f), poiFar ? 6f : 8f, 0.5f);
            }
            if (Carrier.Instance != null)
            {
                Vector2 cp = RadarPoint(Carrier.Instance.transform.position, range, R, out bool carFar);
                Blip(c, cp, new Color(0.3f, 0.9f, 1f), carFar ? 8f : 11f, 0.95f);
            }
        }

        // Player marker + forward tick.
        Blip(c, Vector2.zero, new Color(0.45f, 0.95f, 1f), 9f, 1f);
        Blip(c, new Vector2(0f, -13f), new Color(0.45f, 0.95f, 1f), 4f, 0.8f);

        // Nav readout: nearest destination in space, escape altitude in-world.
        string nav;
        if (currentPlanet == null)
        {
            string best = "";
            float bestD = float.MaxValue;
            foreach (var def in planetDefs)
            {
                float d = (def.spacePos - PlayerShip.transform.position).magnitude - def.EntryRadius;
                if (d < bestD) { bestD = d; best = def.name.ToUpper(); }
            }
            float dc = (Carrier.Instance != null
                ? (Carrier.Instance.transform.position - PlayerShip.transform.position).magnitude
                : float.MaxValue);
            if (dc < bestD) { bestD = dc; best = "CARRIER"; }
            nav = $"{best}  {Mathf.Max(0f, bestD) / 1000f:0.0} km";
        }
        else
        {
            float climb = surface.Center.y + SurfaceWorld.CloudTop + 60f - PlayerShip.transform.position.y;
            nav = climb > 0f ? $"EXIT: CLIMB {climb:0} m" : "EXITING…";
        }
        sSmall.alignment = TextAnchor.UpperCenter;
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f, 0.9f);
        GUI.Label(new Rect(c.x - 110, c.y + R - 26, 220, 22), nav, sSmall);
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f, 0.6f);
        GUI.Label(new Rect(c.x - 80, c.y + R - 4, 160, 20), "M — map", sSmall);
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
        sSmall.alignment = TextAnchor.UpperLeft;
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

    // ── System map overlay ───────────────────────────────────────────────────
    // The world is its own map: the camera orbits the real scene from afar,
    // and these labels pin names onto the actual bodies.

    void GuiMap(float sw, float sh)
    {
        Panel(new Rect(10, 10, 360, 66));
        GUI.Label(new Rect(22, 16, 340, 26), "SYSTEM MAP", sSmall);
        GUI.Label(new Rect(22, 40, 340, 26), "Drag — orbit · Scroll — zoom · M — close", sSmall);

        if (currentPlanet == null)
        {
            foreach (var def in planetDefs)
                MapLabel(def.spacePos, def.name, def.radarColor);
            foreach (var poi in pois)
                MapLabel(poi.pos, poi.name, new Color(0.7f, 0.75f, 0.8f));
            if (Carrier.Instance != null)
                MapLabel(Carrier.Instance.transform.position, "CARRIER", new Color(0.3f, 0.9f, 1f));
        }
        else if (surface != null)
        {
            MapLabel(surface.Center, currentPlanet.name + " — surface loop", currentPlanet.radarColor);
        }
        if (PlayerShip != null)
            MapLabel(PlayerShip.transform.position, "YOU", Color.white);
    }

    void MapLabel(Vector3 world, string label, Color col)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z < 0f) return;
        float x = sp.x / uiScale;
        float y = (Screen.height - sp.y) / uiScale;
        Blip(new Vector2(x, y), Vector2.zero, col, 11f, 1f);
        sSmall.alignment = TextAnchor.UpperCenter;
        sSmall.normal.textColor = col;
        GUI.Label(new Rect(x - 90, y + 10, 180, 24), label, sSmall);
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f);
        sSmall.alignment = TextAnchor.UpperLeft;
    }

    // ── Help overlay ─────────────────────────────────────────────────────────

    void DrawHelp(float sh)
    {
        Panel(new Rect(10, sh - 42, 220, 32));
        GUI.Label(new Rect(22, sh - 37, 210, 26),
            showHelp ? "H  —  hide controls" : "H  —  show controls", sSmall);

        if (!showHelp) return;
        Panel(new Rect(10, sh - 500, 320, 450));
        GUI.Label(new Rect(22, sh - 490, 300, 434),
            "W / S — throttle\n" +
            "Mouse or A / D — turn\n" +
            "Q / E — roll\n" +
            "Shift — boost      X — brake\n" +
            "T — turbo (drains the heat bar;\n" +
            "     better engines last longer)\n" +
            "G — anchor: lock dead still\n" +
            "F — armor mode: plating soaks\n" +
            "     all hits until it breaks\n" +
            "Space / LMB — fire\n" +
            "1 / 2 / 3 — chase / rear / free cam\n" +
            "M — 3D system map\n\n" +
            "Radar: top = your nose. Rim blips =\n" +
            "far away. Colored dots = planets,\n" +
            "cyan = carrier, gray = rock fields.\n" +
            "Fly into a planet's cloud shell to\n" +
            "enter its world; climb back above\n" +
            "the clouds to leave. Surfaces loop\n" +
            "around. Land at under 14 m/s.", sSmall);
    }

    void GuiStranded(float sw, float sh)
    {
        Panel(new Rect(sw / 2 - 330, sh - 250, 660, 190));
        sMed.normal.textColor = new Color(1f, 0.55f, 0.25f);
        GUI.Label(new Rect(sw / 2 - 320, sh - 242, 640, 40), "STRANDED — ALL ENGINES DESTROYED", sMed);
        sMed.normal.textColor = Color.white;
        GUI.Label(new Rect(sw / 2 - 320, sh - 200, 640, 70),
            "You can't die out here. Guns and RCS still answer — fight on, or respawn: " +
            "the core gets towed back to the shipyard and your score carries over.", sMed);
        if (GUI.Button(new Rect(sw / 2 - 105, sh - 122, 210, 44), "RESPAWN  (R)", sBtn))
        { SFX.Ui(SFX.Id.Click); EnterBuild(false); }
    }
}
