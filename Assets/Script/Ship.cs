using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum Faction { Player, Ally, Enemy }

// A flyable block-built ship. Physics model:
//  - Mass and center of mass are the mass-weighted sum of the blocks.
//  - The collider is the convex hull of all blocks (PhysX cooks the combined
//    cube mesh into a convex hull — dynamic MeshColliders must be convex).
//  - Each Thruster block pushes along ship-forward AT ITS OWN POSITION via
//    AddForceAtPosition, so engines mounted off the center-of-mass line
//    genuinely torque the ship. Balance your build or fly in circles.
//  - Steering torque comes from Steering (RCS) blocks; authority grows with
//    each block's lever arm from the center of mass.
public class Ship : MonoBehaviour
{
    public Faction faction;

    // Inputs, written every frame by PlayerController or NPCController.
    [HideInInspector] public float   ThrustInput;  // -1..1 (reverse at half power)
    [HideInInspector] public Vector3 TorqueInput;  // pitch, yaw, roll, each -1..1
    [HideInInspector] public bool    Boost;
    [HideInInspector] public bool    Brake;

    const float ThrustPerBlock = 220f;
    const float BoostMult      = 1.9f;
    const float TorqueScale    = 26f;
    const float MaxSpeed       = 55f;
    const float FireInterval   = 0.24f;
    const float BulletSpeed    = 90f;

    public Rigidbody Body { get; private set; }
    public int BlockCount => bp.Blocks.Count;

    ShipBlueprint bp;
    readonly Dictionary<Vector3Int, GameObject> blockObjs = new Dictionary<Vector3Int, GameObject>();
    readonly List<Vector3Int> thrusters = new List<Vector3Int>();
    readonly List<Vector3Int> guns      = new List<Vector3Int>();
    readonly List<ParticleSystem> flames  = new List<ParticleSystem>();
    readonly List<ParticleSystem> rcsJets = new List<ParticleSystem>();
    float steerAuthority;
    MeshCollider hullCol;
    Light engineLight;
    Renderer coreRend;
    float nextFire;
    int gunIndex;
    float pulseT;

    // ── Construction ─────────────────────────────────────────────────────────

    public void Init(ShipBlueprint blueprint, Faction f)
    {
        faction = f;
        bp = blueprint.Clone();

        Body = gameObject.AddComponent<Rigidbody>();
        Body.useGravity    = false;
        Body.drag          = 0.4f;
        Body.angularDrag   = 2.2f;
        Body.interpolation = RigidbodyInterpolation.Interpolate;

        hullCol = gameObject.AddComponent<MeshCollider>();
        hullCol.convex = true;

        foreach (var kv in bp.Blocks) CreateBlockObj(kv.Key, kv.Value);
        RebuildPhysics();

        var lgo = new GameObject("EngineLight");
        lgo.transform.SetParent(transform, false);
        lgo.transform.localPosition = Vector3.back * 1.5f;
        engineLight = lgo.AddComponent<Light>();
        engineLight.type = LightType.Point;
        engineLight.color = new Color(1f, 0.55f, 0.15f);
        engineLight.range = 9f;
        engineLight.intensity = 0f;

        StartCoroutine(WarpIn());
    }

    // Scale-up "warp in" spawn animation with a slight overshoot.
    IEnumerator WarpIn()
    {
        FX.Flash(transform.position, FX.Accent(faction), 5f, 0.4f);
        float t = 0f;
        while (t < 0.45f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / 0.45f);
            transform.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(k * Mathf.PI)) * k;
            yield return null;
        }
        transform.localScale = Vector3.one;
    }

    void CreateBlockObj(Vector3Int pos, BlockType type)
    {
        var go = new GameObject(type.ToString());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = (Vector3)pos;
        blockObjs[pos] = go;

        var rend = BlockVisuals.Attach(go.transform, type, faction);
        switch (type)
        {
            case BlockType.Core:
                coreRend = rend;
                break;
            case BlockType.Thruster:
                flames.Add(FX.EngineFlame(go.transform, Vector3.back * 0.95f, FX.Accent(faction)));
                break;
            case BlockType.Steering:
                rcsJets.Add(FX.RcsPuff(go.transform, FX.Accent(faction)));
                break;
        }
    }

    void RebuildPhysics()
    {
        float mass = 0f;
        Vector3 com = Vector3.zero;
        thrusters.Clear(); guns.Clear();
        float steer = 0.35f; // tiny base authority so a ship with no RCS can limp around

        foreach (var kv in bp.Blocks)
        {
            float m = ShipBlueprint.MassOf(kv.Value);
            mass += m;
            com  += (Vector3)kv.Key * m;
            if (kv.Value == BlockType.Thruster) thrusters.Add(kv.Key);
            if (kv.Value == BlockType.Gun)      guns.Add(kv.Key);
        }
        com /= mass;

        foreach (var kv in bp.Blocks)
            if (kv.Value == BlockType.Steering)
                steer += 0.7f + 0.5f * ((Vector3)kv.Key - com).magnitude;

        Body.mass = mass;
        Body.centerOfMass = com;
        steerAuthority = steer;

        hullCol.sharedMesh = MeshFactory.BuildHullMesh(bp.Blocks.Keys);
    }

    // ── Flight physics ───────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (Body == null) return;

        float input = ThrustInput < 0f ? ThrustInput * 0.5f : ThrustInput;
        float scale = (Boost ? BoostMult : 1f) * ThrustPerBlock;
        foreach (var t in thrusters)
            Body.AddForceAtPosition(transform.forward * input * scale,
                                    transform.TransformPoint(t));

        if (Brake && Body.velocity.sqrMagnitude > 0.5f)
            Body.AddForce(-Body.velocity.normalized * thrusters.Count * ThrustPerBlock * 0.35f);

        if (Body.velocity.magnitude > MaxSpeed)
            Body.velocity = Body.velocity.normalized * MaxSpeed;

        Vector3 torque = Vector3.ClampMagnitude(TorqueInput, 1.5f) * TorqueScale * steerAuthority;
        Body.AddRelativeTorque(torque);
    }

    void Update()
    {
        // Engine plume + light track the throttle.
        float burn = Mathf.Clamp01(Mathf.Abs(ThrustInput)) * (Boost ? 1.6f : 1f);
        foreach (var f in flames)
        {
            if (f == null) continue;
            var em = f.emission;
            em.rateOverTime = burn * 70f;
        }
        if (engineLight != null)
            engineLight.intensity = Mathf.Lerp(engineLight.intensity, burn * 2.4f, Time.deltaTime * 8f);

        // RCS pods puff while the ship is turning.
        float turn = Mathf.Clamp01(TorqueInput.magnitude);
        foreach (var j in rcsJets)
        {
            if (j == null) continue;
            var em = j.emission;
            em.rateOverTime = turn * 35f;
        }

        // Core heartbeat pulse.
        if (coreRend != null)
        {
            pulseT += Time.deltaTime;
            float p = 1.6f + Mathf.Sin(pulseT * 3.5f) * 0.9f;
            coreRend.material.SetColor("_EmissionColor", FX.Accent(faction) * p);
        }
    }

    // ── Weapons ──────────────────────────────────────────────────────────────

    public void TryFire()
    {
        if (guns.Count == 0 || Time.time < nextFire) return;
        nextFire = Time.time + FireInterval / guns.Count;

        gunIndex = (gunIndex + 1) % guns.Count;
        Vector3 muzzle = transform.TransformPoint((Vector3)guns[gunIndex] + Vector3.forward * 1.55f);
        FX.MuzzleFlash(muzzle, FX.Accent(faction));
        Bullet.Spawn(muzzle, transform.forward * BulletSpeed + Body.velocity, faction, hullCol);
    }

    // ── Damage ───────────────────────────────────────────────────────────────

    // Destroys the block closest to the world-space hit point.
    public void TakeHit(Vector3 worldPoint)
    {
        if (bp == null || Body == null) return;

        Vector3 local = transform.InverseTransformPoint(worldPoint);
        Vector3Int nearest = Vector3Int.zero;
        float best = float.MaxValue;
        foreach (var p in bp.Blocks.Keys)
        {
            float d = (local - (Vector3)p).sqrMagnitude;
            if (d < best) { best = d; nearest = p; }
        }

        if (nearest == Vector3Int.zero) { Die(); return; } // core hit — ship destroyed

        foreach (var cell in bp.Remove(nearest)) // includes blocks orphaned from the core
        {
            if (blockObjs.TryGetValue(cell, out var go) && go != null)
            {
                FX.Impact(go.transform.position, FX.Accent(faction));
                FX.Debris(go.transform.position, go.transform.rotation,
                          FX.BlockMat(faction, BlockType.Hull), Body.velocity);
                Destroy(go);
            }
            blockObjs.Remove(cell);
        }
        RebuildPhysics();

        if (faction == Faction.Player && GameManager.Instance != null)
            GameManager.Instance.CameraShake(0.5f);
    }

    public void Die()
    {
        FX.Explosion(transform.position, FX.Accent(faction), 1.6f);
        int debris = 0;
        foreach (var go in blockObjs.Values)
        {
            if (go == null || ++debris > 14) break;
            FX.Debris(go.transform.position, go.transform.rotation,
                      FX.BlockMat(faction, BlockType.Hull),
                      Body.velocity + Random.insideUnitSphere * 6f);
        }
        if (GameManager.Instance != null) GameManager.Instance.OnShipDestroyed(this);
        Destroy(gameObject);
    }
}
