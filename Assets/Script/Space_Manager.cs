using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Space_Manager : MonoBehaviour
{
    public static Space_Manager Instance { get; private set; }

    [Header("References")]
    public SceneChanger Scene;
    public GameObject Astroid;
    public GameObject Cockpit;

    [Header("Asteroid Spawning")]
    [SerializeField] int asteroidCount = 120;
    [SerializeField] float spawnRadius = 200f;
    [SerializeField] float minSpawnDistance = 30f; // keep asteroids away from spawn point

    [Header("Player Health")]
    [SerializeField] int maxHealth = 100;
    int currentHealth;
    bool isDead = false;

    // Score
    int score = 0;

    // Ship reference for speed display
    Rigidbody shipRb;

    // HUD style
    GUIStyle healthStyle;
    GUIStyle scoreStyle;
    GUIStyle hintStyle;
    GUIStyle gameOverStyle;
    GUIStyle gameOverSubStyle;
    bool stylesReady = false;

    Generator Base;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        currentHealth = maxHealth;

        Base = Cockpit.GetComponent<Generator>();
        Base.LoadPlayer();
        Base.enabled = false;

        shipRb = Cockpit.GetComponent<Rigidbody>();

        // Tag all ship parts so Asteroid.cs can detect them
        foreach (Transform t in Cockpit.GetComponentsInChildren<Transform>(true))
            t.gameObject.tag = "Player";
        Cockpit.tag = "Player";

        SpawnAsteroids();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void SpawnAsteroids()
    {
        for (int i = 0; i < asteroidCount; i++)
        {
            Vector3 pos;
            int attempts = 0;
            do
            {
                pos = new Vector3(
                    Random.Range(-spawnRadius, spawnRadius),
                    Random.Range(-spawnRadius, spawnRadius),
                    Random.Range(-spawnRadius, spawnRadius)
                );
                attempts++;
            } while (pos.magnitude < minSpawnDistance && attempts < 20);

            GameObject ast = Instantiate(Astroid, pos, Random.rotation);
            // Add Asteroid behaviour component at runtime
            if (ast.GetComponent<Asteroid>() == null)
                ast.AddComponent<Asteroid>();
        }
    }

    void Update()
    {
        if (isDead)
        {
            if (Input.GetKeyDown(KeyCode.R))
                Restart();
            if (Input.GetKeyDown(KeyCode.O))
                ReturnToMenu();
            return;
        }

        if (Input.GetKeyDown(KeyCode.O))
            ReturnToMenu();
    }

    public void TakeDamage(int amount)
    {
        if (isDead) return;
        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            OnDeath();
        }
    }

    public void AddScore(int points)
    {
        score += points;
    }

    void OnDeath()
    {
        isDead = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Disable ship controls
        Ship_Movement sm = Cockpit.GetComponent<Ship_Movement>();
        if (sm != null) sm.enabled = false;
    }

    void Restart()
    {
        score = 0;
        SceneManager.LoadScene("Galaxy");
    }

    void ReturnToMenu()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Scene.LoadScene("Menu");
    }

    // ---------- HUD ----------

    void InitStyles()
    {
        if (stylesReady) return;
        stylesReady = true;

        healthStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        healthStyle.normal.textColor = Color.white;

        scoreStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        scoreStyle.normal.textColor = Color.yellow;

        hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.LowerLeft
        };
        hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);

        gameOverStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 52,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        gameOverStyle.normal.textColor = Color.red;

        gameOverSubStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            alignment = TextAnchor.MiddleCenter
        };
        gameOverSubStyle.normal.textColor = Color.white;
    }

    void OnGUI()
    {
        InitStyles();

        float sw = Screen.width;
        float sh = Screen.height;

        if (isDead)
        {
            DrawGameOver(sw, sh);
            return;
        }

        DrawHealthBar(sw, sh);
        DrawScore(sw, sh);
        DrawSpeed(sw, sh);
        DrawHints(sw, sh);
        DrawCrosshair(sw, sh);
    }

    void DrawHealthBar(float sw, float sh)
    {
        float barW = 250f;
        float barH = 28f;
        float x = 20f;
        float y = 20f;

        // Background
        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.Box(new Rect(x, y, barW, barH), "");

        // Health fill (green -> red)
        float pct = (float)currentHealth / maxHealth;
        GUI.color = Color.Lerp(Color.red, Color.green, pct);
        GUI.DrawTexture(new Rect(x + 2, y + 2, (barW - 4) * pct, barH - 4), Texture2D.whiteTexture);

        // Label
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y, barW, barH),
            $"  HP  {currentHealth} / {maxHealth}", healthStyle);
    }

    void DrawScore(float sw, float sh)
    {
        GUI.color = Color.white;
        GUI.Box(new Rect(sw / 2f - 80f, 20f, 160f, 36f), $"SCORE  {score}", scoreStyle);
    }

    void DrawSpeed(float sw, float sh)
    {
        float spd = shipRb != null ? shipRb.velocity.magnitude : 0f;
        GUI.color = Color.white;
        GUI.Box(new Rect(sw - 180f, 20f, 160f, 36f),
            $"SPD  {spd:F0} m/s", scoreStyle);
    }

    void DrawHints(float sw, float sh)
    {
        string hints =
            "W/S — Thrust    A/D — Strafe\n" +
            "Mouse — Aim     Q/E — Roll\n" +
            "LShift — Boost  Space — Brake\n" +
            "LMB / Space — Fire    O — Menu";

        GUI.color = new Color(1, 1, 1, 0.55f);
        GUI.Label(new Rect(12f, sh - 90f, 360f, 85f), hints, hintStyle);
    }

    void DrawCrosshair(float sw, float sh)
    {
        float cx = sw / 2f;
        float cy = sh / 2f;
        float size = 10f;
        float thick = 2f;

        GUI.color = new Color(1, 1, 1, 0.8f);
        GUI.DrawTexture(new Rect(cx - size, cy - thick / 2f, size * 2, thick), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - thick / 2f, cy - size, thick, size * 2), Texture2D.whiteTexture);

        // Small center dot
        GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
    }

    void DrawGameOver(float sw, float sh)
    {
        // Dark overlay
        GUI.color = new Color(0, 0, 0, 0.65f);
        GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float cx = sw / 2f;
        float cy = sh / 2f;

        GUI.Label(new Rect(cx - 300f, cy - 120f, 600f, 100f), "SHIP DESTROYED", gameOverStyle);
        GUI.Label(new Rect(cx - 300f, cy - 20f,  600f, 50f),  $"Final Score: {score}", gameOverSubStyle);
        GUI.Label(new Rect(cx - 300f, cy + 40f,  600f, 50f),  "Press R to Restart  |  O to Return to Menu", gameOverSubStyle);
    }
}
