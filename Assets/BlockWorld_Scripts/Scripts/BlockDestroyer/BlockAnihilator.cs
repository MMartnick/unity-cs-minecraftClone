using UnityEngine;
using System.Collections.Generic;
using static MeshUtils;
using System.Linq;

[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public class BlockAnnihilator : MonoBehaviour
{
    [Tooltip("Reference to your World script (auto-found if left null).")]
    public World world;

    [Tooltip("Which layers belong to chunk colliders (if needed).")]
    public LayerMask chunkLayerMask = ~0;

    [SerializeField]
    private float sphereRadius = 15f;

    // Directions for marking neighbor chunks
    private static readonly Vector3Int[] Neighbors =
    {
        Vector3Int.left,  Vector3Int.right,
        Vector3Int.up,    Vector3Int.down,
        new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
    };

    // Collect chunks that need rebuilding after block destruction
    private static List<Chunk> chunksNeedingRebuild = new List<Chunk>();

    // Optional: skip re-destruction of the same chunk for a cooldown
    private Dictionary<Chunk, float> chunkDestructionTimestamp = new Dictionary<Chunk, float>();
    [SerializeField] private float recheckCooldown = 2f;

    // Throttling for OnTriggerStay checks
    [SerializeField] private float stayCheckInterval = 0.5f;
    private float nextStayTime;

    // Partial-destruction queue: (chunk, startIndex, endIndex)
    private Queue<(Chunk chunk, int startIndex, int endIndex)> workQueue =
        new Queue<(Chunk, int, int)>();

    // How many slices to process each Update
    [SerializeField] private int slicesPerFrame = 3;

    // How many blocks in each slice
    [SerializeField] private int sliceSize = 500;

    private void Awake()
    {
        // Setup sphere-collider trigger
        SphereCollider sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = sphereRadius;

        // (Optional) match the transform's scale visually
        transform.localScale = Vector3.one * (sphereRadius * 2f);

        // Kinematic rigidbody (no physics forces)
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Attempt to find World if not assigned
        if (world == null)
            world = FindAnyObjectByType<World>();
        if (world == null)
            Debug.LogError("BlockAnnihilator: World not set or found.");
    }

    private void OnTriggerEnter(Collider other)
    {
        // If chunk just got destroyed recently, skip
        if (!other.TryGetComponent<Chunk>(out Chunk chunk)) return;
        if (IsRecentlyDestroyed(chunk)) return;

        EnqueueChunkDestruction(chunk);
        chunkDestructionTimestamp[chunk] = Time.time;
    }

    private void OnTriggerStay(Collider other)
    {
        // Throttle partial destruction checks
        if (Time.time < nextStayTime) return;
        nextStayTime = Time.time + stayCheckInterval;

        if (!other.TryGetComponent<Chunk>(out Chunk chunk)) return;
        if (IsRecentlyDestroyed(chunk)) return;

        EnqueueChunkDestruction(chunk);
        chunkDestructionTimestamp[chunk] = Time.time;
    }

    private void Update()
    {
        // If nothing to do, skip
        if (workQueue.Count == 0 && chunksNeedingRebuild.Count == 0)
            return;

        // Cache annihilator's position for partial distance checks
        Vector3 annihilatorPos = transform.position;

        // Process a few slices this frame
        for (int i = 0; i < slicesPerFrame; i++)
        {
            if (workQueue.Count == 0) break;

            var (chunk, start, end) = workQueue.Dequeue();
            if (start < 0)
            {
                // Negative start => chunk fully inside radius => destroy all blocks
                DestroyAllBlocksInChunk(chunk);
            }
            else
            {
                // Partial slice => do distance checks
                DestroyBlocksSlice(chunk, start, end, annihilatorPos);
            }
        }

        // Rebuild changed chunks
        if (chunksNeedingRebuild.Count > 0 && world != null)
        {
            foreach (var c in chunksNeedingRebuild)
                world.RedrawChunk(c);

            chunksNeedingRebuild.Clear();
        }
    }

    /// <summary>
    /// Checks if we destroyed this chunk too recently, skipping repeated destruction calls.
    /// </summary>
    private bool IsRecentlyDestroyed(Chunk chunk)
    {
        if (!chunkDestructionTimestamp.TryGetValue(chunk, out float lastTime))
            return false;
        return (Time.time - lastTime < recheckCooldown);
    }

    /// <summary>
    /// Runs a bounding-sphere check on the chunk. If fully inside => single pass,
    /// else break into slices for partial distance checks.
    /// </summary>
    private void EnqueueChunkDestruction(Chunk chunk)
    {
        if (!ChunkWithinSphereCheck(chunk, out bool fullyInside))
            return;

        int blockCount = chunk.width * chunk.height * chunk.depth;

        if (fullyInside)
        {
            // Use -1 to indicate "destroy entire chunk in one pass"
            workQueue.Enqueue((chunk, -1, -1));
        }
        else
        {
            // Partially inside => break chunk into slices
            for (int start = 0; start < blockCount; start += sliceSize)
            {
                int end = Mathf.Min(start + sliceSize, blockCount);
                workQueue.Enqueue((chunk, start, end));
            }
        }
    }

    /// <summary>
    /// Destroy **all** blocks in the chunk (including water). 
    /// Also triggers the block above to drop (if droppable).
    /// </summary>
    private void DestroyAllBlocksInChunk(Chunk chunk)
    {
        if (chunk == null) return;

        int w = chunk.width;
        int h = chunk.height;
        int d = chunk.depth;
        int total = w * h * d;

        bool destroyed = false;

        for (int i = 0; i < total; i++)
        {
            BlockType bType = chunk.chunkData[i];
            if (bType == BlockType.AIR)
                continue; // skip if already air

            // Convert to AIR
            chunk.chunkData[i] = BlockType.AIR;
            destroyed = true;

            // Possibly let block above fall if it's droppable (not water)
            int x = i % w;
            int yz = i / w;
            int y = yz % h;
            int z = yz / h;

            MaybeTriggerDropAbove(chunk, x, y, z);

            MarkNeighborsForRebuild(chunk, i);
        }

        if (destroyed && !chunksNeedingRebuild.Contains(chunk))
            chunksNeedingRebuild.Add(chunk);
    }

    /// <summary>
    /// For the slice [startIndex, endIndex), do a distance check vs. the annihilator.
    /// If within radius => destroy the block. We do *not* skip water, so water is destroyed as well.
    /// Then let the block above drop if it's a droppable type (not water).
    /// </summary>
    private void DestroyBlocksSlice(Chunk chunk, int startIndex, int endIndex, Vector3 annihilatorPos)
    {
        if (chunk == null) return;

        int w = chunk.width;
        int h = chunk.height;
        int d = chunk.depth;

        Vector3Int cOrigin = Vector3Int.FloorToInt(chunk.location);
        float sqRad = sphereRadius * sphereRadius;
        bool destroyed = false;

        for (int i = startIndex; i < endIndex; i++)
        {
            // Skip if already AIR
            BlockType oldType = chunk.chunkData[i];
            if (oldType == BlockType.AIR)
                continue;

            int x = i % w;
            int yz = i / w;
            int y = yz % h;
            int z = yz / h;

            float wx = cOrigin.x + x + 0.5f;
            float wy = cOrigin.y + y + 0.5f;
            float wz = cOrigin.z + z + 0.5f;

            Vector3 diff = annihilatorPos - new Vector3(wx, wy, wz);
            if (diff.sqrMagnitude <= sqRad)
            {
                chunk.chunkData[i] = BlockType.AIR;
                destroyed = true;

                // Possibly drop the block above
                MaybeTriggerDropAbove(chunk, x, y, z);

                MarkNeighborsForRebuild(chunk, i);
            }
        }

        if (destroyed && !chunksNeedingRebuild.Contains(chunk))
            chunksNeedingRebuild.Add(chunk);
    }

    /// <summary>
    /// Checks chunk bounding box vs. the annihilator sphere.
    /// If fully inside => no partial checks needed. 
    /// </summary>
    private bool ChunkWithinSphereCheck(Chunk chunk, out bool fullyInside)
    {
        fullyInside = false;
        if (chunk == null) return false;

        Vector3 cMin = chunk.location;
        Vector3 size = new Vector3(chunk.width, chunk.height, chunk.depth);
        Vector3 center = cMin + size * 0.5f;

        float chunkRadius = 0.5f * size.magnitude;
        float dist = Vector3.Distance(transform.position, center);

        if (dist > sphereRadius + chunkRadius)
            return false; // completely outside

        if (dist + chunkRadius < sphereRadius)
        {
            fullyInside = true; // fully inside
            return true;
        }

        return true; // partial
    }

    /// <summary>
    /// Marks the chunk + neighbors for rebuild after removing a block
    /// so the terrain geometry updates properly.
    /// </summary>
    private void MarkNeighborsForRebuild(Chunk chunk, int flatIndex)
    {
        int w = chunk.width;
        int h = chunk.height;
        int d = chunk.depth;

        // local coords from flatten index
        int x = flatIndex % w;
        int yz = flatIndex / w;
        int y = yz % h;
        int z = yz / h;

        // Mark the chunk
        if (!chunksNeedingRebuild.Contains(chunk))
            chunksNeedingRebuild.Add(chunk);

        // Mark neighbors if on chunk boundary
        Vector3Int chunkKey = Vector3Int.FloorToInt(chunk.location);
        foreach (var off in Neighbors)
        {
            int nx = x + off.x;
            int ny = y + off.y;
            int nz = z + off.z;

            var lookup = world.GetWorldNeighbour(new Vector3Int(nx, ny, nz), chunkKey);
            if (world.chunks.TryGetValue(lookup.Item2, out Chunk nbChunk))
            {
                if (!chunksNeedingRebuild.Contains(nbChunk))
                    chunksNeedingRebuild.Add(nbChunk);
            }
        }
    }

    /// <summary>
    /// If the block above is droppable (e.g. sand), call world.Drop(...) so it falls.
    /// Water is destroyed by the annihilator if inside the radius, so we do NOT drop water.
    /// </summary>
    private void MaybeTriggerDropAbove(Chunk chunk, int localX, int localY, int localZ)
    {
        // The block above => localY+1
        var aboveLookup = world.GetWorldNeighbour(
            new Vector3Int(localX, localY + 1, localZ),
            Vector3Int.CeilToInt(chunk.location)
        );

        if (!world.chunks.TryGetValue(aboveLookup.Item2, out Chunk aboveChunk))
            return; // no chunk or out of bounds above

        int aboveIndex = world.ToFlat(aboveLookup.Item1);
        BlockType aboveType = aboveChunk.chunkData[aboveIndex];

        // Only drop if it’s in canDrop => e.g. sand, gravel
        // Water is also destroyed by the annihilator, so we skip dropping water.
        if (MeshUtils.canDrop.Contains(aboveType))
        {
            StartCoroutine(world.Drop(aboveChunk, aboveIndex, 3));
        }
    }
}
