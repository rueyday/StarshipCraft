using System.Text;
using UnityEngine;

// Blueprint ↔ "ship code": a compact shareable string ("SC1:x.y.z.type.mk;…").
// Used for clipboard sharing (C/V in the shipyard), saving your design between
// sessions, and announcing your ship to the crew over the network — the same
// serialization everywhere, so multiplayer never needs a second format.
public static class NetCodec
{
    public const string Prefix = "SC1:";

    public static string Encode(ShipBlueprint bp)
    {
        var sb = new StringBuilder(Prefix);
        foreach (var kv in bp.Blocks)
        {
            if (kv.Key == Vector3Int.zero) continue; // the core is implicit
            sb.Append(kv.Key.x).Append('.').Append(kv.Key.y).Append('.').Append(kv.Key.z)
              .Append('.').Append((int)kv.Value.type).Append('.').Append(kv.Value.mk).Append(';');
        }
        return sb.ToString();
    }

    // Returns null when the string isn't a valid ship code. Blocks that don't
    // connect back to the core are pruned rather than rejected.
    public static ShipBlueprint Decode(string code)
    {
        if (string.IsNullOrEmpty(code) || !code.StartsWith(Prefix)) return null;
        var bp = new ShipBlueprint();
        string[] parts = code.Substring(Prefix.Length).Split(';');
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            string[] f = part.Split('.');
            if (f.Length != 5) return null;
            int x, y, z, t, m;
            if (!int.TryParse(f[0], out x) || !int.TryParse(f[1], out y) ||
                !int.TryParse(f[2], out z) || !int.TryParse(f[3], out t) ||
                !int.TryParse(f[4], out m)) return null;
            if (t <= (int)BlockType.Core || t > (int)BlockType.Gun || m < 1 || m > 2) return null;
            bp.AddRaw(new Vector3Int(x, y, z), new BlockDef((BlockType)t, m));
        }
        bp.PruneDisconnected();
        return bp;
    }
}
