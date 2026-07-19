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
//    genuinely torque the ship — and more thrusters mean more total force.
//  - Steering torque comes from RCS blocks; authority grows with every pod
//    and with its lever arm from the center of mass.
//  - Blocks have hit points (Armor soaks several hits). The Core is
//    indestructible: the player never dies, but a ship with no thrusters
//    left is stranded.
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
    public const float MaxSpeed = 55f; // Planet tunes its gravity against this
    const float FireInterval   = 0.24f;

    public Rigidbody Body { get; private set; }
    public int BlockCount => bp.Blocks.Count;
    public int ThrusterCount => thrusters.Count;

    struct Mount
    {
        public Vector3Int pos;
        public float power; // thrust multiplier, or gun mark
        public Mount(Vector3Int p, float pw) { pos = p; power = pw; }
    }

    ShipBlueprint bp;
    readonly Dictionary<Vector3Int, GameObject> blockObjs = new Dictionary<Vector3Int, GameObject>();
    readonly Dictionary<Vector3Int, Renderer> bodyRends = new Dictionary<Vector3Int, Renderer>();
    readonly Dictionary<Vector3Int, int> hp = new Dictionary<Vector3Int, int>();
    readonly List<Mount> thrusters = new List<Mount>();
    readonly List<Mount> guns      = new List<Mount>();
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

        foreach (var kv in bp.Blocks)
        {
            CreateBlockObj(kv.Key, kv.Value);
            hp[kv.Key] = ShipBlueprint.HpOf(kv.Value);
        }
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

    void CreateBlockObj(Vector3Int pos, BlockDef def)
    {
        var go = new GameObject(def.type.ToString());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = (Vector3)pos;
        blockObjs[pos] = go;

        var rend = BlockVisuals.Attach(go.transform, def, faction);
        bodyRends[pos] = rend;
        switch (def.type)
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
            if (kv.Value.type == BlockType.Thruster)
                thrusters.Add(new Mount(kv.Key, ShipBlueprint.ThrustMult(kv.Value)));
            if (kv.Value.type == BlockType.Gun)
                guns.Add(new Mount(kv.Key, kv.Value.mk));
        }
        com /= mass;

        foreach (var kv in bp.Blocks)
            if (kv.Value.type == BlockType.Steering)
                steer += (0.7f + 0.5f * ((Vector3)kv.Key - com).magnitude)
                       * ShipBlueprint.SteerMult(kv.Value);

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
            Body.AddForceAtPosition(transform.forward * input * scale * t.power,
                                    transform.TransformPoint(t.pos));

        if (Brake && Body.velocity.sqrMagnitude > 0.5f)
            Body.AddForce(-Body.velocity.normalized * thrusters.Count * ThrustPerBlock * 0.35f);

        if (Body.velocity.magnitude > MaxSpeed)
            Body.velocity = Body.velocity.normalized * MaxSpeed;

        Vector3 torque = Vector3.ClampMagnitude(TorqueInput, 1.5f) * TorqueScale * steerAuthority;
        Body.AddRelativeTorque(torque);

        // Planet gravity well — mass cancels out, weak engines don't.
        if (Planet.Instance != null)
            Body.AddForce(Planet.Instance.GravityAccel(transform.position), ForceMode.Acceleration);
    }

    // Crash physics: hard impacts smash blocks off; gentle contact (landing,
    // scraping along the surface) just kicks up dust. Asteroids run their own
    // collision damage, so they are skipped here.
    void OnCollisionEnter(Collision col)
    {
        if (col.collider.GetComponent<Asteroid>() != null) return;

        float speed = col.relativeVelocity.magnitude;
        Vector3 point = col.GetContact(0).point;
        if (speed > 14f)
        {
            TakeHit(point);
            if (speed > 30f) TakeHit(point); // brutal impacts cost two blocks
            FX.Explosion(point, new Color(1f, 0.7f, 0.3f), 0.6f);
        }
        else if (speed > 3f && col.collider.GetComponent<Planet>() != null)
        {
            FX.Impact(point, new Color(0.65f, 0.55f, 0.4f)); // touchdown dust
        }
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

        float rate = 0f;
        foreach (var g in guns) rate += g.power >= 2f ? 1.7f : 1f;
        nextFire = Time.time + FireInterval / rate;

        gunIndex = (gunIndex + 1) % guns.Count;
        var gun = guns[gunIndex];
        float speed = gun.power >= 2f ? 125f : 90f;
        Vector3 muzzle = transform.TransformPoint((Vector3)gun.pos + Vector3.forward * 1.55f);
        FX.MuzzleFlash(muzzle, FX.Accent(faction));
        Bullet.Spawn(muzzle, transform.forward * speed + Body.velocity, faction, hullCol);
    }

    // ── Damage ───────────────────────────────────────────────────────────────

    // Damages the block closest to the world-space hit point. Armor soaks
    // several hits; the Core shrugs everything off (the hard rule: no death).
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

        if (nearest == Vector3Int.zero)
        {
            // Core shield flare — takes the hit, never breaks.
            FX.Impact(worldPoint, FX.Accent(faction));
            FX.Flash(worldPoint, FX.Accent(faction), 3.5f, 0.25f);
            return;
        }

        var def = bp.Blocks[nearest];
        hp[nearest]--;
        if (hp[nearest] > 0)
        {
            // Damaged but holding: sparks + progressively scorched body.
            FX.Impact(worldPoint, new Color(1f, 0.75f, 0.4f));
            if (bodyRends.TryGetValue(nearest, out var rend) && rend != null)
            {
                float frac = (float)hp[nearest] / ShipBlueprint.HpOf(def);
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_Color", rend.sharedMaterial.color * Mathf.Lerp(0.3f, 1f, frac));
                rend.SetPropertyBlock(mpb);
            }
            if (faction == Faction.Player && GameManager.Instance != null)
                GameManager.Instance.CameraShake(0.25f);
            return;
        }

        foreach (var cell in bp.Remove(nearest)) // includes blocks orphaned from the core
        {
            if (blockObjs.TryGetValue(cell, out var go) && go != null)
            {
                FX.Impact(go.transform.position, FX.Accent(faction));
                FX.Debris(go.transform.position, go.transform.rotation,
                          FX.BlockMat(faction, new BlockDef(BlockType.Hull)), Body.velocity);
                Destroy(go);
            }
            blockObjs.Remove(cell);
            bodyRends.Remove(cell);
            hp.Remove(cell);
        }
        RebuildPhysics();

        if (faction == Faction.Player && GameManager.Instance != null)
            GameManager.Instance.CameraShake(0.5f);

        // Out of engines: the player is stranded (no death — maybe they get
        // rescued by a clever plan); NPC hulks just blow up.
        if (thrusters.Count == 0)
        {
            if (faction == Faction.Player)
            {
                if (GameManager.Instance != null) GameManager.Instance.OnPlayerStranded();
            }
            else Die();
        }
    }

    public void Die()
    {
        FX.Explosion(transform.position, FX.Accent(faction), 1.6f);
        int debris = 0;
        foreach (var go in blockObjs.Values)
        {
            if (go == null || ++debris > 14) break;
            FX.Debris(go.transform.position, go.transform.rotation,
                      FX.BlockMat(faction, new BlockDef(BlockType.Hull)),
                      Body.velocity + Random.insideUnitSphere * 6f);
        }
        if (GameManager.Instance != null) GameManager.Instance.OnShipDestroyed(this);
        Destroy(gameObject);
    }
}
