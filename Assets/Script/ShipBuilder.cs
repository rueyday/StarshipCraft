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
    public BlockDef Selected { get; private set; } = new BlockDef(BlockType.Hull);

    Camera cam;
    readonly Dictionary<Vector3Int, GameObject> objs = new Dictionary<Vector3Int, GameObject>();
    GameObject ghost;
    Material ghostMat;
    GameObject comMarker, thrustMarker;
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

        // Balance gizmos: yellow = center of mass, orange = thrust centroid.
        // Line them up (in X/Y) and the ship flies straight under power.
        comMarker    = Marker(new Color(1f, 0.9f, 0.2f));
        thrustMarker = Marker(new Color(1f, 0.5f, 0.1f));
    }

    GameObject Marker(Color c)
    {
        var m = new GameObject("Marker");
        m.transform.SetParent(transform, false);
        m.transform.localScale = Vector3.one * 0.34f;
        m.AddComponent<MeshFilter>().mesh = MeshFactory.CreateSphereMesh();
        m.AddComponent<MeshRenderer>().material = FX.Standard(Color.black, c * 2.2f, 0f, 0.5f);
        return m;
    }

    void UpdateMarkers()
    {
        float mass = 0f, thrust = 0f;
        Vector3 com = Vector3.zero, tc = Vector3.zero;
        foreach (var kv in Blueprint.Blocks)
        {
            float m = ShipBlueprint.MassOf(kv.Value);
            mass += m;
            com += (Vector3)kv.Key * m;
            if (kv.Value.type == BlockType.Thruster)
            {
                float t = ShipBlueprint.ThrustMult(kv.Value);
                thrust += t;
                tc += (Vector3)kv.Key * t;
            }
        }
        com /= mass;
        comMarker.transform.localPosition = com + Vector3.up * 0.02f;
        thrustMarker.SetActive(thrust > 0f);
        if (thrust > 0f)
            thrustMarker.transform.localPosition =
                new Vector3(tc.x / thrust, tc.y / thrust, com.z) + Vector3.up * 0.02f;
    }

    void CreateObj(Vector3Int pos, BlockDef def, bool animate)
    {
        var go = new GameObject(def.type.ToString());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = (Vector3)pos;
        BlockVisuals.Attach(go.transform, def, Faction.Player);
        go.AddComponent<BoxCollider>(); // full 1m cell so face-aiming stays easy
        objs[pos] = go;
        if (animate) StartCoroutine(PopIn(go.transform));
    }

    // Rebuild every block object from the (replaced) blueprint.
    void ReloadVisuals()
    {
        foreach (var kv in objs) if (kv.Value != null) Destroy(kv.Value);
        objs.Clear();
        foreach (var kv in Blueprint.Blocks) CreateObj(kv.Key, kv.Value, true);
    }

    void PaletteKey(KeyCode key, BlockType type)
    {
        if (!Input.GetKeyDown(key)) return;
        SelectFromUi(type);
    }

    // Shared by keys and the tappable palette rows in the HUD.
    public void SelectFromUi(BlockType type)
    {
        Selected = Selected.type == type
            ? new BlockDef(type, Selected.mk == 1 ? 2 : 1) // same choice again: toggle tier
            : new BlockDef(type);
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
        // ── Palette: number selects a block; same number (or Tab) toggles Mk II ──
        PaletteKey(KeyCode.Alpha1, BlockType.Hull);
        PaletteKey(KeyCode.Alpha2, BlockType.Thruster);
        PaletteKey(KeyCode.Alpha3, BlockType.Steering);
        PaletteKey(KeyCode.Alpha4, BlockType.Gun);
        PaletteKey(KeyCode.Alpha5, BlockType.Armor);
        if (Input.GetKeyDown(KeyCode.Tab))
            Selected = new BlockDef(Selected.type, Selected.mk == 1 ? 2 : 1);

        // Ship codes: C copies the design to the clipboard, V loads one from it.
        if (Input.GetKeyDown(KeyCode.C))
        {
            GUIUtility.systemCopyBuffer = NetCodec.Encode(Blueprint);
            SFX.Ui(SFX.Id.Confirm, 0.7f);
            if (GameManager.Instance != null)
                GameManager.Instance.BuilderToast("Ship code copied to clipboard");
        }
        if (Input.GetKeyDown(KeyCode.V))
        {
            var pasted = NetCodec.Decode(GUIUtility.systemCopyBuffer);
            if (pasted != null)
            {
                Blueprint.CopyFrom(pasted);
                ReloadVisuals();
                SFX.Ui(SFX.Id.Warp, 0.5f, 1.3f);
                if (GameManager.Instance != null)
                    GameManager.Instance.BuilderToast("Ship code loaded");
            }
            else if (GameManager.Instance != null)
            {
                SFX.Ui(SFX.Id.Click, 0.6f, 0.6f);
                GameManager.Instance.BuilderToast("Clipboard has no valid ship code");
            }
        }

        // ── Orbit camera (two-finger drag + pinch on touch) ──
        float ox = Input.GetAxis("Horizontal");
        float oy = Input.GetAxis("Vertical");
        if (Input.GetMouseButton(2)) { ox += Input.GetAxis("Mouse X") * 4f; oy += Input.GetAxis("Mouse Y") * 4f; }
        if (TouchControls.Enabled && Input.touchCount == 2)
        {
            Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
            Vector2 avg = (a.deltaPosition + b.deltaPosition) * 0.5f;
            yaw += avg.x * 0.25f;
            pitch = Mathf.Clamp(pitch - avg.y * 0.2f, -80f, 80f);
            float d0 = ((a.position - a.deltaPosition) - (b.position - b.deltaPosition)).magnitude;
            float d1 = (a.position - b.position).magnitude;
            dist = Mathf.Clamp(dist - (d1 - d0) * 0.02f, 4f, 30f);
        }
        yaw   += ox * 90f * Time.deltaTime;
        pitch  = Mathf.Clamp(pitch + oy * 70f * Time.deltaTime, -80f, 80f);
        dist   = Mathf.Clamp(dist - Input.GetAxis("Mouse ScrollWheel") * 5f, 4f, 30f);

        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.position = rot * new Vector3(0f, 0f, -dist);
        cam.transform.rotation = rot;

        UpdateMarkers();

        // ── Aim + place/remove ──
        ghost.SetActive(false);
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 100f) && hit.transform.parent == transform)
        {
            var aimed = Vector3Int.RoundToInt(hit.transform.localPosition);
            var target = aimed + Vector3Int.RoundToInt(
                transform.InverseTransformDirection(hit.normal));

            // Touch: one-finger tap acts (place, or remove in DEL mode);
            // two fingers are the camera, so never edit then.
            bool tap = Input.GetMouseButtonDown(0) &&
                       !(TouchControls.Enabled && Input.touchCount > 1);
            bool tapRemoves = TouchControls.Enabled && TouchControls.RemoveMode;
            bool wantRemove = Input.GetMouseButtonDown(1) || (tap && tapRemoves);
            bool wantPlace = tap && !tapRemoves;

            if (!Blueprint.Blocks.ContainsKey(target))
            {
                ghost.SetActive(true);
                ghost.transform.localPosition = (Vector3)target;
                pulseT += Time.deltaTime;
                var c = ghostMat.color;
                c.a = 0.22f + 0.16f * Mathf.Sin(pulseT * 6f);
                ghostMat.color = c;

                if (wantPlace && Blueprint.TryAdd(target, Selected))
                {
                    CreateObj(target, Selected, true);
                    FX.Impact(transform.TransformPoint((Vector3)target), new Color(0.4f, 0.9f, 1f));
                    SFX.Ui(SFX.Id.Place, 0.7f);
                }
            }

            if (wantRemove)
            {
                bool removedAny = false;
                foreach (var cell in Blueprint.Remove(aimed))
                {
                    if (objs.TryGetValue(cell, out var go))
                    {
                        FX.Impact(go.transform.position, new Color(1f, 0.6f, 0.3f));
                        Destroy(go);
                        objs.Remove(cell);
                        removedAny = true;
                    }
                }
                if (removedAny) SFX.Ui(SFX.Id.Clank, 0.5f, 1.4f);
            }
        }
    }
}
