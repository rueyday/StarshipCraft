using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Generator : MonoBehaviour
{
    public static Generator Instance { get; private set; }

    public GameObject Hull;
    public GameObject Half_Hull;
    public GameObject One_Third_Hull;
    public GameObject One_Sixth_Hull;
    public GameObject Five_Sixth_Hull;
    public GameObject One_Half_Hull_two;
    public GameObject One_Sixth_Hull_two;
    public GameObject Five_Sixth_Hull_two;
    public GameObject Gun_1;
    public GameObject Thruster_1;

    Object_Handler Handler;

    void Start()
    {
        Instance = this;
        GameObject Ref = GameObject.Find("GameManager");
        Handler = Ref.GetComponent<Object_Handler>();
    }

    void OnMouseDown()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            Vector3 localHit = hit.transform.InverseTransformPoint(hit.point);
            localHit.Normalize();

            float ax = Mathf.Abs(localHit.x);
            float ay = Mathf.Abs(localHit.y);
            float az = Mathf.Abs(localHit.z);

            if (ax > ay && ax > az)
                AddCube(localHit.x > 0 ? Vector3.right : Vector3.left, Handler.GetMode(), true);
            else if (ay > ax && ay > az)
                AddCube(localHit.y > 0 ? Vector3.up : Vector3.down, Handler.GetMode(), true);
            else
                AddCube(localHit.z > 0 ? Vector3.forward : Vector3.back, Handler.GetMode(), true);
        }
    }

    // Returns the prefab for a given mode, or null if not found
    GameObject GetPrefab(int m)
    {
        switch (m)
        {
            case 1:   return Hull;
            case 2:   return Half_Hull;
            case 3:   return One_Third_Hull;
            case 4:   return One_Sixth_Hull;
            case 5:   return Five_Sixth_Hull;
            case 6:   return One_Half_Hull_two;
            case 7:   return One_Sixth_Hull_two;
            case 8:   return Five_Sixth_Hull_two;
            case 100: return Gun_1;
            case 200: return Thruster_1;
            default:  return null;
        }
    }

    void AddCube(Vector3 direction, int m, bool kind)
    {
        // Delete mode
        if (m == 0)
        {
            if (!kind) return;

            // Find this object's index in the handler data (with bounds check to prevent infinite loop)
            int search = 0;
            Vector3 pos = gameObject.transform.position;
            while (search < Handler.index &&
                   (pos.x != Handler.Data_X[search] ||
                    pos.y != Handler.Data_Y[search] ||
                    pos.z != Handler.Data_Z[search]))
            {
                ++search;
            }

            if (search < Handler.index)
            {
                Handler.remove_Data(search);
                Destroy(gameObject);
            }
            else
            {
                Debug.LogWarning("Delete: component not found in data at " + pos);
            }
            return;
        }

        GameObject prefab = GetPrefab(m);
        if (prefab == null)
        {
            Debug.LogWarning("No prefab for mode " + m);
            return;
        }

        Vector3 newPosition = transform.position + direction;
        GameObject newCube = Instantiate(prefab, newPosition, Quaternion.identity);
        newCube.transform.Rotate(Handler.x_Rotate, Handler.y_Rotate, Handler.z_Rotate);
        newCube.transform.SetParent(transform.parent != null ? transform.parent : transform);

        if (kind)
            Handler.UpdateData(newPosition.x, newPosition.y, newPosition.z, m);
    }

    public void LoadPlayer()
    {
        if (!Handler.Load())
        {
            Debug.Log("No save data found, starting with blank ship.");
            return;
        }

        // Destroy all existing added pieces (children/siblings but not self, camera, or local point)
        Transform parentTransform = transform.parent != null ? transform.parent : transform;
        List<GameObject> toDestroy = new List<GameObject>();

        foreach (Transform child in parentTransform)
        {
            if (child.gameObject != gameObject &&
                child.name != "Main Camera" &&
                child.name != "Local_Point")
            {
                toDestroy.Add(child.gameObject);
            }
        }
        // Also clear Generator's own children if it is the root
        if (transform.parent == null)
        {
            foreach (Transform child in transform)
            {
                if (child.name != "Main Camera" && child.name != "Local_Point")
                    toDestroy.Add(child.gameObject);
            }
        }
        foreach (var go in toDestroy)
            Destroy(go);

        // Re-create all saved pieces without modifying the data arrays
        int savedCount = Handler.index;
        for (int i = 0; i < savedCount; i++)
        {
            Vector3 pos = new Vector3(Handler.Data_X[i], Handler.Data_Y[i], Handler.Data_Z[i]);
            Handler.x_Rotate = Handler.Data_Rot_X[i];
            Handler.y_Rotate = Handler.Data_Rot_Y[i];
            Handler.z_Rotate = Handler.Data_Rot_Z[i];
            int mode = Handler.Data_Mode[i];
            if (mode != 0)
                AddCube(pos - transform.position, mode, false);
        }

        Handler.x_Rotate = 0;
        Handler.y_Rotate = 0;
        Handler.z_Rotate = 0;
    }

    public void SavePlayer()
    {
        Handler.SavePlayer();
    }
}
