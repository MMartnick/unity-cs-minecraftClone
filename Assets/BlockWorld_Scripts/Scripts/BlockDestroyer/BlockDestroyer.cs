using UnityEngine;
using System.Collections.Generic;
using static MeshUtils;

[RequireComponent(typeof(Rigidbody))]
public class BlockDestroyer : MonoBehaviour
{
    [Tooltip("World component (auto–found if left blank).")]
    public World world;

    [Tooltip("Which layers count as chunk colliders.")]
    public LayerMask chunkLayerMask = ~0;

    // Half-extents of the 30×30×30 box
    private static readonly Vector3 RangeExtents = new Vector3(30f, 30f, 30f);

    // Directions to check for neighbor-chunk redraw
    private static readonly Vector3Int[] Neighbors = {
        Vector3Int.left,  Vector3Int.right,
        Vector3Int.up,    Vector3Int.down,
        new Vector3Int(0,0,1),  new Vector3Int(0,0,-1)
    };

    // Keep track of chunks to “mark for rebuild”
    private HashSet<Chunk> chunksToRedraw = new HashSet<Chunk>();
    
    // NEW: Track centers of any blocks that took damage this frame
    private List<Vector3> damagedBlockCenters = new List<Vector3>();
    void Awake()
    {
        transform.localScale = RangeExtents;
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        if (world == null)
            world = FindAnyObjectByType<World>();
        if (world == null)
            Debug.LogError("BlockDestroyer: World not set or found.");
    }

    void Update()
    {
        // 1) OverlapBox to find chunk colliders in range
        var cols = Physics.OverlapBox(transform.position, RangeExtents,
                                      Quaternion.identity, chunkLayerMask);
        foreach (var col in cols)
            TryDigInCollider(col);

        // 2) After processing all collisions, schedule a rebuild for each changed chunk
        foreach (var chunk in chunksToRedraw)
            world.RedrawChunk(chunk);

        chunksToRedraw.Clear();
    }

    private void TryDigInCollider(Collider col)
    {
        if (!col) return;

        // Raycast from center toward the collider
        Vector3 origin = transform.position;
        Vector3 target = col.ClosestPoint(origin);
        Vector3 dir = (target - origin).normalized;
        if (dir == Vector3.zero) return;  // skip if inside

        if (!Physics.Raycast(origin, dir, out var hit, RangeExtents.magnitude, chunkLayerMask))
            return;

        // Check for a Chunk
        if (!hit.collider.TryGetComponent<Chunk>(out var hitChunk))
            return;

        // Convert hit into local coords
        Vector3 ws = hit.point - hit.normal * 0.5f;
        int bx = Mathf.FloorToInt(ws.x - hitChunk.location.x);
        int by = Mathf.FloorToInt(ws.y - hitChunk.location.y);
        int bz = Mathf.FloorToInt(ws.z - hitChunk.location.z);

        // Possibly cross-chunk boundary
        var lookup = world.GetWorldNeighbour(new Vector3Int(bx, by, bz),
                                                Vector3Int.CeilToInt(hitChunk.location));
        Vector3Int localKey = lookup.Item1;
        Vector3Int ownerKey = lookup.Item2;

        if (!world.chunks.TryGetValue(ownerKey, out var owner))
            return;

        // Bounds check
        if (localKey.x < 0 || localKey.x >= owner.width ||
            localKey.y < 0 || localKey.y >= owner.height ||
            localKey.z < 0 || localKey.z >= owner.depth)
            return;

        // Dig logic
        int idx = localKey.x + owner.width * (localKey.y + owner.depth * localKey.z);
        var type = owner.chunkData[idx];
        if (type == BlockType.AIR) return;  // nothing to destroy

        owner.healthData[idx]++;
        int dmg = (int)owner.healthData[idx] + 12;
        int maxHits = blockTypeHealth[(int)type];

        if (dmg == (int)BlockType.NOCRACK)
            StartCoroutine(world.HealBlock(owner, idx));

        // If fully destroyed => AIR
        if (maxHits != -1 && dmg >= (int)BlockType.NOCRACK + maxHits)
            owner.chunkData[idx] = BlockType.AIR;

        // Mark the owner chunk for an async rebuild
        chunksToRedraw.Add(owner);

        // Also mark neighbors if a boundary block was destroyed
        if (type != BlockType.AIR)  // i.e. we truly destroyed something
        {
            foreach (var off in Neighbors)
            {
                var nbLookup = world.GetWorldNeighbour(localKey + off, ownerKey);
                if (world.chunks.TryGetValue(nbLookup.Item2, out var nbChunk))
                    chunksToRedraw.Add(nbChunk);
            }
        }

        // NEW: Record the block's center for drawing a wire-cube
        // We'll compute the center in world-space
        float cx = owner.location.x + localKey.x + 0.5f;
        float cy = owner.location.y + localKey.y + 0.5f;
        float cz = owner.location.z + localKey.z + 0.5f;
        damagedBlockCenters.Add(new Vector3(cx, cy, cz));
    }

    // NEW: OnDrawGizmos => draw a wire cube for each damaged block this frame
    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;

        // Draw a wire cube to visualize the OverlapBox
        Gizmos.DrawWireCube(transform.position, RangeExtents * 2f);

        // Draw a small wire cube for each block we damaged
        Gizmos.color = Color.red;
        foreach (var center in damagedBlockCenters)
        {
            // e.g. a 1×1×1 wire cube around the block center
            Gizmos.DrawWireCube(center, Vector3.one);
        }
    }
}

