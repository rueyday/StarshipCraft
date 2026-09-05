using System.Collections.Generic;
using UnityEngine;

// Scene bridge for NetSession: pumps the sockets each frame, streams out the
// player's flight state, and materializes every crew member as a friendly
// ghost-synced ship — but only while you're in the same zone (space, or the
// same planet's surface scene). Crew avatars are presence, not combat: they
// can't be damaged locally, and each machine keeps its own NPCs.
public class NetLink : MonoBehaviour
{
    public static NetLink Instance { get; private set; }
    public NetSession Session { get; } = new NetSession();

    public bool Active => Session.Active;
    public int CrewCount => Session.Active ? Session.Remotes.Count + 1 : 1;

    readonly Dictionary<int, Ship> avatars = new Dictionary<int, Ship>();
    readonly Dictionary<int, string> avatarCodes = new Dictionary<int, string>();
    readonly List<int> scratch = new List<int>();
    readonly float[] state = new float[10];

    void Awake() => Instance = this;
    void OnDestroy() => Session.Shutdown();

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || !Session.Active) return;

        float[] outState = null;
        var me = gm.PlayerShip;
        if (me != null && me.Body != null)
        {
            var t = me.transform;
            Vector3 v = me.Body.isKinematic ? Vector3.zero : me.Body.velocity;
            state[0] = t.position.x; state[1] = t.position.y; state[2] = t.position.z;
            state[3] = t.rotation.x; state[4] = t.rotation.y;
            state[5] = t.rotation.z; state[6] = t.rotation.w;
            state[7] = v.x; state[8] = v.y; state[9] = v.z;
            outState = state;
        }

        Session.Pump(Time.realtimeSinceStartupAsDouble, gm.ZoneName, outState);
        SyncAvatars(gm);
    }

    void SyncAvatars(GameManager gm)
    {
        // Drop avatars whose pilot left, changed ship, or is in another zone.
        scratch.Clear();
        foreach (var kv in avatars)
        {
            NetSession.Remote r;
            bool keep = kv.Value != null &&
                Session.Remotes.TryGetValue(kv.Key, out r) &&
                r.zone == gm.ZoneName && r.shipCode == avatarCodes[kv.Key];
            if (!keep) scratch.Add(kv.Key);
        }
        foreach (int id in scratch)
        {
            if (avatars[id] != null)
            {
                gm.Ships.Remove(avatars[id]);
                Destroy(avatars[id].gameObject);
            }
            avatars.Remove(id);
            avatarCodes.Remove(id);
        }

        // Spawn avatars for crew who just arrived in our zone.
        foreach (var kv in Session.Remotes)
        {
            var r = kv.Value;
            if (avatars.ContainsKey(r.id) || !r.hasState ||
                r.zone != gm.ZoneName || r.shipCode.Length == 0) continue;
            var bp = NetCodec.Decode(r.shipCode);
            if (bp == null) continue;

            var go = new GameObject("Crew" + (r.id + 1));
            go.transform.position = new Vector3(r.px, r.py, r.pz);
            var ship = go.AddComponent<Ship>();
            ship.RemoteAvatar = true;
            ship.Init(bp, Faction.Ally);
            ship.Body.isKinematic = true;
            go.AddComponent<RemotePilot>().Data = r;
            gm.Ships.Add(ship);
            avatars[r.id] = ship;
            avatarCodes[r.id] = r.shipCode;
        }
    }
}

// Interpolates a crew avatar toward its last reported state, with a little
// velocity extrapolation so 10 Hz updates still read as smooth flight.
public class RemotePilot : MonoBehaviour
{
    public NetSession.Remote Data;
    Ship ship;

    void Awake() => ship = GetComponent<Ship>();

    void Update()
    {
        if (Data == null) return;
        Vector3 vel = new Vector3(Data.vx, Data.vy, Data.vz);
        Vector3 target = new Vector3(Data.px, Data.py, Data.pz) + vel * 0.1f;
        var rot = new Quaternion(Data.rx, Data.ry, Data.rz, Data.rw);

        if ((transform.position - target).sqrMagnitude > 3600f)
            transform.SetPositionAndRotation(target, rot); // too far — snap
        else
        {
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 9f);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 9f);
        }

        if (ship != null) // engine plume mirrors their speed
            ship.ThrustInput = Mathf.Clamp01(vel.magnitude / 30f);
    }
}
