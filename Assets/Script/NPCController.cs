using UnityEngine;

// Simple dogfight AI shared by allies and enemies.
//  Enemies hunt the player (and player's allies). Allies hunt enemies and
//  fall into formation near the player when nothing hostile is around.
//  GameSettings.npcSkill scales turn sharpness, throttle and trigger discipline.
public class NPCController : MonoBehaviour
{
    Ship ship;
    Ship target;         // hostile ship being hunted (may be null)
    float retarget;
    float wanderSeed;

    const float PreferredRange = 28f;
    const float FireRange      = 75f;
    const float FireCone       = 10f; // degrees

    void Awake()
    {
        ship = GetComponent<Ship>();
        wanderSeed = Random.value * 100f;
    }

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || ship.Body == null) return;

        retarget -= Time.deltaTime;
        if (retarget <= 0f) { AcquireTarget(gm); retarget = 1.5f; }

        Vector3 aimPoint;
        bool hostile = target != null;
        if (hostile)
        {
            // Lead the target by its velocity for believable gunnery.
            float eta = Vector3.Distance(target.transform.position, transform.position) / 90f;
            aimPoint = target.transform.position + target.Body.velocity * eta;
        }
        else if (ship.faction == Faction.Ally && gm.PlayerShip != null)
        {
            // Formation slot beside the player, drifting a little.
            var p = gm.PlayerShip.transform;
            aimPoint = p.position - p.forward * 10f +
                       p.right * Mathf.Sin(Time.time * 0.3f + wanderSeed) * 12f;
        }
        else
        {
            // Lazy patrol orbit.
            aimPoint = transform.position + Quaternion.Euler(0f,
                Mathf.PerlinNoise(Time.time * 0.05f, wanderSeed) * 360f, 0f) * Vector3.forward * 30f;
        }

        Steer(aimPoint, hostile);
    }

    void AcquireTarget(GameManager gm)
    {
        target = null;
        float best = float.MaxValue;
        foreach (var s in gm.Ships)
        {
            if (s == null || s == ship) continue;
            bool isHostile = ship.faction == Faction.Enemy
                ? s.faction != Faction.Enemy
                : s.faction == Faction.Enemy;
            if (!isHostile) continue;
            float d = (s.transform.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; target = s; }
        }
    }

    void Steer(Vector3 worldAim, bool hostile)
    {
        float skill = GameSettings.npcSkill;
        Vector3 local = transform.InverseTransformPoint(worldAim);
        float dist = local.magnitude;
        Vector3 dir = local / Mathf.Max(dist, 0.01f);

        // Torque toward the aim point; damp roll so NPCs stay level-ish.
        ship.TorqueInput = new Vector3(
            Mathf.Clamp(-dir.y * 2.5f * skill, -1f, 1f),
            Mathf.Clamp( dir.x * 2.5f * skill, -1f, 1f),
            Mathf.Clamp(-ship.Body.angularVelocity.z * 0.4f, -1f, 1f));

        bool facing = dir.z > 0.6f;
        float wantRange = hostile ? PreferredRange : 14f;
        ship.ThrustInput = facing && dist > wantRange ? Mathf.Clamp01(0.4f + 0.6f * skill) : 0f;
        ship.Brake = dist < wantRange * 0.5f;
        ship.Boost = hostile && dist > 120f;

        if (hostile && dist < FireRange && Vector3.Angle(Vector3.forward, dir) < FireCone)
            ship.TryFire();
    }
}
