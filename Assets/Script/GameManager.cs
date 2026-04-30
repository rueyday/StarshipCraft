using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Drop one empty GameObject called "GameManager" in any scene and attach this.
// Everything else is created at runtime — no other scene setup needed.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    enum State { Menu, Playing, Dead, GameOver }
    State state = State.Menu;

    [SerializeField] int   startLives    = 3;
    [SerializeField] int   baseAsteroids = 5;
    [SerializeField] float spawnRadius   = 80f;
    [SerializeField] float safeRadius    = 18f;

    int   score, wave, lives;
    float deadTimer;

    PlayerShip player;
    Camera     cam;
    readonly List<Asteroid> asteroids = new List<Asteroid>();

    // ── HUD styles ───────────────────────────────────────────────────────────
    GUIStyle sTitle, sMed, sSmall;
    bool stylesBuilt;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        SetupCamera();
        SpawnPlayer(Vector3.zero);
    }

    void Update()
    {
        switch (state)
        {
            case State.Menu:
                if (Input.anyKeyDown) StartGame();
                break;

            case State.Playing:
                FollowCamera();
                if (asteroids.Count == 0) StartWave();
                break;

            case State.Dead:
                deadTimer -= Time.deltaTime;
                FollowCamera();
                if (deadTimer <= 0f) Respawn();
                break;

            case State.GameOver:
                if (Input.GetKeyDown(KeyCode.R)) SceneManager.LoadScene(0);
                break;
        }
    }

    // ── Game flow ────────────────────────────────────────────────────────────

    void StartGame()
    {
        score = 0; wave = 0; lives = startLives;
        state = State.Playing;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        ClearAsteroids();
        StartWave();
    }

    void StartWave()
    {
        wave++;
        int count = baseAsteroids + (wave - 1) * 2;
        Vector3 origin = player != null ? player.transform.position : Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            Vector3 pos; int tries = 0;
            do { pos = origin + Random.onUnitSphere * spawnRadius; tries++; }
            while (pos.magnitude < safeRadius && tries < 25);
            SpawnAsteroid(pos, Asteroid.AsteroidSize.Large);
        }
    }

    void SpawnPlayer(Vector3 pos)
    {
        if (player != null) Destroy(player.gameObject);

        var go = new GameObject("Player");
        go.tag = "Player";
        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = MeshFactory.CreateShipMesh();
        go.AddComponent<MeshRenderer>().material = LitMat(new Color(0.3f, 0.65f, 1f));

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity  = false;
        rb.drag        = 0.5f;
        rb.angularDrag = 3f;

        // Convex MeshCollider matches the dart shape exactly
        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = mf.mesh;
        col.convex     = true;

        go.transform.position = pos;
        player = go.AddComponent<PlayerShip>();
    }

    void Respawn()
    {
        SpawnPlayer(Vector3.zero);
        player.BeginInvincible(3f);
        state = State.Playing;
    }

    // ── Asteroids ────────────────────────────────────────────────────────────

    public void SpawnAsteroid(Vector3 pos, Asteroid.AsteroidSize size)
    {
        float radius = size == Asteroid.AsteroidSize.Large  ? 3.5f
                     : size == Asteroid.AsteroidSize.Medium ? 2.0f : 1.0f;
        float speed  = size == Asteroid.AsteroidSize.Large  ? 4f
                     : size == Asteroid.AsteroidSize.Medium ? 7f   : 11f;

        var go = new GameObject("Asteroid");
        var mf = go.AddComponent<MeshFilter>();
        mf.mesh = MeshFactory.CreateAsteroidMesh(Random.Range(0, 9999), radius);
        go.AddComponent<MeshRenderer>().material = LitMat(new Color(0.55f, 0.45f, 0.35f));

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity      = false;
        rb.drag            = 0f;
        rb.velocity        = Random.onUnitSphere * speed;
        rb.angularVelocity = Random.onUnitSphere * 1.5f;

        // SphereCollider is cheaper than MeshCollider for asteroids
        var col = go.AddComponent<SphereCollider>();
        col.radius = radius * 0.85f;

        go.transform.position = pos;
        var ast = go.AddComponent<Asteroid>();
        ast.Size = size;
        asteroids.Add(ast);
    }

    public void OnAsteroidDestroyed(Asteroid ast)
    {
        if (!asteroids.Remove(ast)) return;

        score += ast.Size == Asteroid.AsteroidSize.Large  ? 100
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

    public void OnPlayerHit()
    {
        if (state != State.Playing) return;
        lives--;
        Destroy(player.gameObject);
        if (lives <= 0)
        {
            state = State.GameOver;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
        else
        {
            state     = State.Dead;
            deadTimer = 2.5f;
        }
    }

    void ClearAsteroids()
    {
        foreach (var a in asteroids) if (a != null) Destroy(a.gameObject);
        asteroids.Clear();
    }

    // ── Camera ───────────────────────────────────────────────────────────────

    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var g = new GameObject("Main Camera"); g.tag = "MainCamera";
            cam = g.AddComponent<Camera>();
            g.AddComponent<AudioListener>();
        }
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.02f, 0.02f, 0.07f);
        cam.farClipPlane     = 800f;
    }

    void FollowCamera()
    {
        if (player == null || cam == null) return;
        Vector3 target = player.transform.position
                       - player.transform.forward * 14f
                       + player.transform.up      *  4f;
        cam.transform.position = Vector3.Lerp(cam.transform.position, target, Time.deltaTime * 6f);
        cam.transform.LookAt(player.transform.position + player.transform.forward * 4f, player.transform.up);
    }

    // ── Utilities ────────────────────────────────────────────────────────────

    static Material LitMat(Color c)
    {
        var m = new Material(Shader.Find("Standard")); m.color = c; return m;
    }

    // ── HUD ──────────────────────────────────────────────────────────────────

    void BuildStyles()
    {
        if (stylesBuilt) return; stylesBuilt = true;

        sTitle = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        sTitle.normal.textColor = Color.white;

        sMed = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
        sMed.normal.textColor = Color.white;

        sSmall = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperLeft };
        sSmall.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
    }

    void OnGUI()
    {
        BuildStyles();
        float sw = Screen.width, sh = Screen.height;

        switch (state)
        {
            case State.Menu:
                GUI.Label(new Rect(sw/2-300, sh/2-120, 600, 80), "STARSHIP CRAFT", sTitle);
                sTitle.fontSize = 26; sTitle.normal.textColor = Color.yellow;
                GUI.Label(new Rect(sw/2-250, sh/2-30, 500, 50), "Press any key to play", sTitle);
                sTitle.fontSize = 54; sTitle.normal.textColor = Color.white;
                sMed.normal.textColor = new Color(1f,1f,1f,0.65f);
                GUI.Label(new Rect(sw/2-300, sh/2+35, 600, 70),
                    "WASD — Thrust / Strafe     Mouse — Aim     Q / E — Roll\n" +
                    "Space / LMB — Fire     X — Brake     Shift — Boost", sMed);
                sMed.normal.textColor = Color.white;
                break;

            case State.Playing:
            case State.Dead:
                GUI.Label(new Rect(12, 10, 200, 28), $"SCORE   {score}", sSmall);
                GUI.Label(new Rect(12, 36, 200, 28), $"WAVE    {wave}",  sSmall);
                DrawLives(sw);
                DrawCrosshair(sw, sh);
                if (state == State.Dead)
                {
                    sMed.normal.textColor = new Color(1f, 0.65f, 0.2f);
                    GUI.Label(new Rect(sw/2-220, sh/2-30, 440, 60),
                        $"Respawning in {Mathf.CeilToInt(deadTimer)}...", sMed);
                    sMed.normal.textColor = Color.white;
                }
                break;

            case State.GameOver:
                sTitle.normal.textColor = Color.red;
                GUI.Label(new Rect(sw/2-300, sh/2-110, 600, 80), "GAME OVER", sTitle);
                sTitle.normal.textColor = Color.white;
                GUI.Label(new Rect(sw/2-220, sh/2-20,  440, 50), $"Final Score: {score}", sMed);
                GUI.Label(new Rect(sw/2-160, sh/2+40,  320, 40), "Press R to restart",    sMed);
                break;
        }
    }

    void DrawLives(float sw)
    {
        string h = ""; for (int i = 0; i < lives; i++) h += "♥ ";
        sSmall.alignment = TextAnchor.UpperRight;
        GUI.Label(new Rect(sw - 160f, 10f, 150f, 28f), h, sSmall);
        sSmall.alignment = TextAnchor.UpperLeft;
    }

    void DrawCrosshair(float sw, float sh)
    {
        float cx = sw/2f, cy = sh/2f, s = 10f, t = 2f;
        GUI.color = new Color(1f, 1f, 1f, 0.75f);
        GUI.DrawTexture(new Rect(cx-s,   cy-t/2, s*2, t),   Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx-t/2, cy-s,   t,   s*2), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
