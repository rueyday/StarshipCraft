using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum BlockType { Core, Hull, Thruster, Steering, Gun }

// A ship design on an integer grid. (0,0,0) is always the Core; +Z is the nose.
// Every block must stay face-adjacent-connected to the Core.
public class ShipBlueprint
{
    public readonly Dictionary<Vector3Int, BlockType> Blocks = new Dictionary<Vector3Int, BlockType>();

    static readonly Vector3Int[] Faces =
    {
        Vector3Int.right, Vector3Int.left, Vector3Int.up,
        Vector3Int.down,  new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
    };

    public ShipBlueprint()
    {
        Blocks[Vector3Int.zero] = BlockType.Core;
    }

    public ShipBlueprint Clone()
    {
        var bp = new ShipBlueprint();
        foreach (var kv in Blocks) bp.Blocks[kv.Key] = kv.Value;
        return bp;
    }

    public static float MassOf(BlockType t)
    {
        switch (t)
        {
            case BlockType.Core:     return 2.0f;
            case BlockType.Thruster: return 1.4f;
            case BlockType.Steering: return 0.8f;
            case BlockType.Gun:      return 1.1f;
            default:                 return 1.0f;
        }
    }

    public bool TryAdd(Vector3Int pos, BlockType type)
    {
        if (type == BlockType.Core || Blocks.ContainsKey(pos)) return false;
        if (!Faces.Any(f => Blocks.ContainsKey(pos + f))) return false;
        Blocks[pos] = type;
        return true;
    }

    // Removes a block (never the Core). Returns every cell that actually went away,
    // including blocks orphaned from the Core by the removal.
    public List<Vector3Int> Remove(Vector3Int pos)
    {
        var removed = new List<Vector3Int>();
        if (pos == Vector3Int.zero || !Blocks.Remove(pos)) return removed;
        removed.Add(pos);
        removed.AddRange(PruneDisconnected());
        return removed;
    }

    // Flood-fills from the Core and drops anything unreachable.
    public List<Vector3Int> PruneDisconnected()
    {
        var seen  = new HashSet<Vector3Int> { Vector3Int.zero };
        var queue = new Queue<Vector3Int>();
        queue.Enqueue(Vector3Int.zero);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var f in Faces)
            {
                var n = p + f;
                if (Blocks.ContainsKey(n) && seen.Add(n)) queue.Enqueue(n);
            }
        }

        var orphans = Blocks.Keys.Where(p => !seen.Contains(p)).ToList();
        foreach (var p in orphans) Blocks.Remove(p);
        return orphans;
    }

    public int Count(BlockType t) => Blocks.Values.Count(v => v == t);
}
