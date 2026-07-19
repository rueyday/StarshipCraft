using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Minecraft-style build mode. Aim at a block face with the mouse:
//  LMB place the selected block on that face, RMB remove the aimed block.
//  1-4 pick the block type, WASD/arrows orbit, scroll zooms.
// Works on a preview object with one BoxCollider per block for face raycasts.
public class ShipBuilder : MonoBehaviour
{
    public ShipBlueprint Blueprint { get; private set; }
    public BlockType Selected { get; private set; } = BlockType.Hull;

    Camera cam;
    readonly Dictionary<Vector3Int, GameObject> objs = new Dictionary<Vector3Int, GameObject>();
    GameObject ghost;
    Material ghostMat;
    float yaw = 35f, pitch = 18f, dist = 9f;
    float pulseT;

    public void Init(ShipBlueprint blueprint, Camera camera)
    {
        Blueprint = blueprint;
        cam = camera;
        foreach (var kv in Blueprint.Blocks) CreateObj(kv.Key, kv.Value, false);

        ghost = new GameObject("Ghost");
        ghost.transform.SetParent(transform, false);
        ghost.AddComponent<MeshFilter>().mesh = MeshFactory.CubeMesh();
        ghostMat = FX.Ghost(new Color(0.3f, 0.9f, 1f, 0.35f));
        ghost.AddComponent<MeshRenderer>().material = ghostMat;
        ghost.SetActive(false);
    }

    void CreateObj(Vector3Int pos, BlockType type, bool animate)
    {
        var go = new GameObject(type.ToString());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = (Vector3)pos;
        BlockVisuals.Attach(go.transform, type, Faction.Player);
        go.AddComponent<BoxCollider>(); // full 1m cell so face-aiming stays easy
        objs[pos] = go;
        if (animate) StartCoroutine(PopIn(go.transform));
    }

    // Placement animation: block scales up with a springy overshoot.
    IEnumerator PopIn(Transform t)
    {
        float e = 0f;
        while (t != null && e < 0.22f)
        {
            e += Time.deltaTime;
            float k = Mathf.Clamp01(e / 0.22f);
            t.localScale = Vector3.one * (k < 0.7f ? k / 0.7f * 1.12f : Mathf.Lerp(1.12f, 1f, (k - 0.7f) / 0.3f));
            yield return null;
        }
        if (t != null) t.localScale = Vector3.one;
    }

    void Update()
    {
        // ── Palette ──
        if (Input.GetKeyDown(KeyCode.Alpha1)) Selected = BlockType.Hull;
        if (Input.GetKeyDown(KeyCode.Alpha2)) Selected = BlockType.Thruster;
        if (Input.GetKeyDown(KeyCode.Alpha3)) Selected = BlockType.Steering;
        if (Input.GetKeyDown(KeyCode.Alpha4)) Selected = BlockType.Gun;

        // ── Orbit camera ──
        float ox = Input.GetAxis("Horizontal");
        float oy = Input.GetAxis("Vertical");
        if (Input.GetMouseButton(2)) { ox += Input.GetAxis("Mouse X") * 4f; oy += Input.GetAxis("Mouse Y") * 4f; }
        yaw   += ox * 90f * Time.deltaTime;
        pitch  = Mathf.Clamp(pitch + oy * 70f * Time.deltaTime, -80f, 80f);
        dist   = Mathf.Clamp(dist - Input.GetAxis("Mouse ScrollWheel") * 5f, 4f, 30f);

        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.position = rot * new Vector3(0f, 0f, -dist);
        cam.transform.rotation = rot;

        // ── Aim + place/remove ──
        ghost.SetActive(false);
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 100f) && hit.transform.parent == transform)
        {
            var aimed = Vector3Int.RoundToInt(hit.transform.localPosition);
            var target = aimed + Vector3Int.RoundToInt(
                transform.InverseTransformDirection(hit.normal));

            if (!Blueprint.Blocks.ContainsKey(target))
            {
                ghost.SetActive(true);
                ghost.transform.localPosition = (Vector3)target;
                pulseT += Time.deltaTime;
                var c = ghostMat.color;
                c.a = 0.22f + 0.16f * Mathf.Sin(pulseT * 6f);
                ghostMat.color = c;

                if (Input.GetMouseButtonDown(0) && Blueprint.TryAdd(target, Selected))
                {
                    CreateObj(target, Selected, true);
                    FX.Impact(transform.TransformPoint((Vector3)target), new Color(0.4f, 0.9f, 1f));
                }
            }

            if (Input.GetMouseButtonDown(1))
                foreach (var cell in Blueprint.Remove(aimed))
                {
                    if (objs.TryGetValue(cell, out var go))
                    {
                        FX.Impact(go.transform.position, new Color(1f, 0.6f, 0.3f));
                        Destroy(go);
                        objs.Remove(cell);
                    }
                }
        }
    }
}
