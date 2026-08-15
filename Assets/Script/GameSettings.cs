// Global difficulty settings, edited from the Settings screen.
public static class GameSettings
{
    public static int   asteroidCount = 8;    // asteroids kept alive around the player
    public static float asteroidSpeed = 5f;   // m/s drift speed of large asteroids
    public static int   enemyCount    = 2;    // hostile NPC ships kept alive
    public static int   allyCount     = 1;    // friendly NPC ships kept alive
    public static float npcSkill      = 1f;   // multiplies NPC turn rate, thrust and fire rate
    public static float mouseSens     = 0.55f;
    public static bool  invertY       = false;

    public static void ApplyEasy()
    {
        asteroidCount = 5;  asteroidSpeed = 3f;
        enemyCount    = 1;  allyCount     = 2;  npcSkill = 0.7f;
    }

    public static void ApplyNormal()
    {
        asteroidCount = 8;  asteroidSpeed = 5f;
        enemyCount    = 2;  allyCount     = 1;  npcSkill = 1f;
    }

    public static void ApplyHard()
    {
        asteroidCount = 14; asteroidSpeed = 9f;
        enemyCount    = 4;  allyCount     = 1;  npcSkill = 1.5f;
    }
}
