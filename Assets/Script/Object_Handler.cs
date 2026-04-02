using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Object_Handler : MonoBehaviour
{
    public static Object_Handler Instance { get; private set; }

    int mode = 1;
    public int x_Rotate = 0;
    public int y_Rotate = 0;
    public int z_Rotate = 0;

    public float[] Data_X = new float[400];
    public float[] Data_Y = new float[400];
    public float[] Data_Z = new float[400];
    public int[] Data_Mode = new int[400];
    public int[] Data_Rot_X = new int[400];
    public int[] Data_Rot_Y = new int[400];
    public int[] Data_Rot_Z = new int[400];

    public int index = 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void UpdateData(float x, float y, float z, int m)
    {
        if (index >= 400)
        {
            Debug.LogWarning("Ship component limit (400) reached!");
            return;
        }
        Data_X[index] = x;
        Data_Y[index] = y;
        Data_Z[index] = z;
        Data_Mode[index] = m;
        Data_Rot_X[index] = x_Rotate;
        Data_Rot_Y[index] = y_Rotate;
        Data_Rot_Z[index] = z_Rotate;
        ++index;
    }

    public void remove_Data(int s)
    {
        --index;
        Data_X[s] = Data_X[index];
        Data_Y[s] = Data_Y[index];
        Data_Z[s] = Data_Z[index];
        Data_Mode[s] = Data_Mode[index];
        Data_Rot_X[s] = Data_Rot_X[index];
        Data_Rot_Y[s] = Data_Rot_Y[index];
        Data_Rot_Z[s] = Data_Rot_Z[index];
    }

    public void SavePlayer()
    {
        Save_System.Save_Player(this);
    }

    public void clear_Data()
    {
        index = 0;
        System.Array.Clear(Data_X, 0, 400);
        System.Array.Clear(Data_Y, 0, 400);
        System.Array.Clear(Data_Z, 0, 400);
        System.Array.Clear(Data_Mode, 0, 400);
        System.Array.Clear(Data_Rot_X, 0, 400);
        System.Array.Clear(Data_Rot_Y, 0, 400);
        System.Array.Clear(Data_Rot_Z, 0, 400);
        Debug.Log("Ship data cleared");
    }

    // Returns true if save data was loaded successfully
    public bool Load()
    {
        Player_Data loadData = Save_System.LoadPlayer();
        if (loadData == null) return false;

        index = loadData.size;
        for (int i = 0; i < index; i++)
        {
            Data_X[i] = loadData.Ship_Data_X[i];
            Data_Y[i] = loadData.Ship_Data_Y[i];
            Data_Z[i] = loadData.Ship_Data_Z[i];
            Data_Mode[i] = loadData.Ship_Data_Mode[i];
            Data_Rot_X[i] = loadData.Ship_Data_Rot_X[i];
            Data_Rot_Y[i] = loadData.Ship_Data_Rot_Y[i];
            Data_Rot_Z[i] = loadData.Ship_Data_Rot_Z[i];
        }
        return true;
    }

    public void TaskOnClick_Number(int number)
    {
        mode = number;
    }

    public void TaskOnClickRemove()
    {
        mode = 0;
        Debug.Log("Delete mode active");
    }

    public int GetMode()
    {
        return mode;
    }
}
