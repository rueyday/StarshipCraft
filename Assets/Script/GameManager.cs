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

    // Scene swapping: null currentPlanet = space; otherwise we're inside that
    // planet's looping SurfaceWorld and the space content sleeps.
    PlanetDef[] planetDefs;
    Transform spaceRoot;
    SurfaceWorld surface;
    PlanetDef currentPlanet;
    Vector3 entryDir = Vector3.forward;
    Vector3 sunDir = Vector3.down;

    // Big fading title cards ("ENTERING KORRATH — CLOUD BANKS").
    string announceText = "";
    float announceT;
    float menuAngle;

    void Announce(string msg) { announceText = msg; announceT = 4f; }

    static string WeatherLabel(WeatherKind k) =>
        k == WeatherKind.Dust ? "GIANT DUST STORM"
        : k == WeatherKind.Snow ? "SNOW STORM"
        : k == WeatherKind.Cloud ? "CLOUD BANKS" : "";
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
        cam.farClipPlane    = 34000f; // planets + Titanhold's full ring stay visible

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
                name = "Korrath", spacePos = new Vector3(0f, -1400f, 3400f), radius = 1400f,
                land = new Color(0.4f, 0.34f, 0.26f), ocean = new Color(0.08f, 0.28f, 0.5f),
                ground = new Color(0.35f, 0.3f, 0.23f), hasOcean = true, hasBelt = true,
                weather = WeatherKind.Cloud, radarColor = new Color(0.45f, 0.65f, 1f),
            },
            new PlanetDef
            {
                name = "Vessa", spacePos = new Vector3(-5200f, 900f, -3800f), radius = 650f,
                land = new Color(0.75f, 0.8f, 0.85f), ocean = new Color(0.5f, 0.65f, 0.8f),
                ground = new Color(0.72f, 0.78f, 0.85f), hasOcean = true,
                weather = WeatherKind.Snow, radarColor = new Color(0.6f, 0.8f, 1f),
            },
            new PlanetDef
            {
                name = "Titanhold", spacePos = new Vector3(16000f, -1200f, 2500f), radius = 3500f,
                land = new Color(0.62f, 0.45f, 0.22f),
                ground = new Color(0.55f, 0.4f, 0.22f), hasRing = true,
                weather = WeatherKind.Dust, radarColor = new Color(1f, 0.75f, 0.3f),
            },
        };
        foreach (var def in planetDefs)
            SpacePlanet.Create(def).transform.SetParent(spaceRoot, true);

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

        blueprint = DefaultPlayerBlueprint();
    }

    void Update()
    {
        if (starfieldRoot != null) starfieldRoot.position = cam.transform.position;
        if (announceT > 0f) announceT -= Time.deltaTime;

        switch (state)
        {
            case State.Menu:
            case State.Settings:
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
        mapDist = Mathf.Clamp(mapDist - Input.GetAxis("Mouse ScrollWheel") * 4000f, 2500f, 30000f);
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
    float mapYaw = 30f, mapPitch = 55f, mapDist = 9000f;
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

        if (GUI.Button(new Rect(sw / 2 - 110, sh / 2 - 25, 220, 48), "SINGLE PLAYER", sBtn)) EnterBuild(true);
        GUI.enabled = false;
        GUI.Button(new Rect(sw / 2 - 110, sh / 2 + 32, 220, 48), "MULTIPLAYER — SOON", sBtn);
        GUI.enabled = true;
        if (GUI.Button(new Rect(sw / 2 - 110, sh / 2 + 89, 220, 48), "SETTINGS", sBtn)) state = State.Settings;
    }

    void GuiSettings(float sw, float sh)
    {
        Panel(new Rect(sw / 2 - 320, sh / 2 - 260, 640, 540));
        GUI.Label(new Rect(sw / 2 - 300, sh / 2 - 245, 600, 50), "SETTINGS", sTitle);

        float y = sh / 2 - 175;
        GameSettings.asteroidCount = SliderRow(sw, ref y, "Asteroids", GameSettings.asteroidCount, 0, 30);
        GameSettings.asteroidSpeed = SliderRowF(sw, ref y, "Asteroid speed", GameSettings.asteroidSpeed, 1f, 20f);
        GameSettings.enemyCount    = SliderRow(sw, ref y, "Enemy ships", GameSettings.enemyCount, 0, 8);
        GameSettings.allyCount     = SliderRow(sw, ref y, "Allied ships", GameSettings.allyCount, 0, 4);
        GameSettings.npcSkill      = SliderRowF(sw, ref y, "NPC speed / skill", GameSettings.npcSkill, 0.5f, 2f);
        GameSettings.mouseSens     = SliderRowF(sw, ref y, "Mouse sensitivity", GameSettings.mouseSens, 0.2f, 1.5f);
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

        DrawTurboBar(sw, sh);
        if (Carrier.Instance != null && Carrier.Instance.CanRefit(PlayerShip))
        {
            sMed.normal.textColor = new Color(0.3f, 0.9f, 1f);
            GUI.Label(new Rect(sw / 2 - 200, sh / 2 + 60, 400, 36), "E  —  refit at the hangar", sMed);
            sMed.normal.textColor = Color.white;
        }

        DrawRadar(sw, sh);
        DrawHelp(sh);
        if (PlayerStranded) GuiStranded(sw, sh);
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
            if (Carrier.Instance != null)
            {
                Vector2 cp = RadarPoint(Carrier.Instance.transform.position, range, R, out bool carFar);
                Blip(c, cp, new Color(0.3f, 0.9f, 1f), carFar ? 8f : 11f, 0.95f);
            }
        }

        // Player marker + forward tick.
        Blip(c, Vector2.zero, new Color(0.45f, 0.95f, 1f), 9f, 1f);
        Blip(c, new Vector2(0f, -13f), new Color(0.45f, 0.95f, 1f), 4f, 0.8f);

        sSmall.alignment = TextAnchor.UpperCenter;
        sSmall.normal.textColor = new Color(0.45f, 0.95f, 1f, 0.7f);
        GUI.Label(new Rect(c.x - 80, c.y + R - 8, 160, 22), "M — map", sSmall);
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
        float y = Screen.height - sp.y;
        Blip(new Vector2(sp.x, y), Vector2.zero, col, 11f, 1f);
        sSmall.alignment = TextAnchor.UpperCenter;
        sSmall.normal.textColor = col;
        GUI.Label(new Rect(sp.x - 90, y + 10, 180, 24), label, sSmall);
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
            "far away. Blue dots = planets,\n" +
            "orange = Titanhold, cyan = carrier.\n" +
            "Fly into a planet's cloud shell to\n" +
            "enter its world; climb back above\n" +
            "the clouds to leave. Surfaces loop\n" +
            "around. Land at under 14 m/s.", sSmall);
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
