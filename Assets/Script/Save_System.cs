using UnityEngine;
using System.IO;

public static class Save_System
{
    static string SavePath => Application.persistentDataPath + "/player.json";

    public static void Save_Player(Object_Handler player)
    {
        Player_Data data = new Player_Data(player);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(SavePath, json);
        Debug.Log("Saved to " + SavePath);
    }

    public static Player_Data LoadPlayer()
    {
        if (File.Exists(SavePath))
        {
            string json = File.ReadAllText(SavePath);
            Player_Data data = JsonUtility.FromJson<Player_Data>(json);
            Debug.Log("Loaded from " + SavePath);
            return data;
        }
        Debug.LogWarning("No save file found at " + SavePath);
        return null;
    }

    public static bool HasSave()
    {
        return File.Exists(SavePath);
    }
}
