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

    // Optional: skip repeated chunk destructions for a cooldown
    private Dictionary<Chunk, float> chunkDestructionTimestamp = new Dictionary<Chunk, float>();
    [SerializeField] private float recheckCooldown = 2f;

    // Throttling OnTriggerStay checks
    [SerializeField] private float stayCheckInterval = 0.5f;
    private float nextStayTime;

    // Work queue for partial destruction slices
    private Queue<(Chunk chunk, int startIndex, int endIndex)> workQueue = new Queue<(Chunk, int, int)>();

    // How many slices we process each frame
    [SerializeField] private int slicesPerFrame = 3;

    // Number of blocks in each slice
    [SerializeField] private int sliceSize = 500;

    private void Awake()
    {
        // Configure the sphere trigger
        SphereCollider sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = sphereRadius;

        // Optional: match transform scale visually
        transform.localScale = Vector3.one * (sphereRadius * 2f);

        // Kinematic rigidbody for trigger events
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Auto-find the World if not assigned
        if (world == null)
            world = FindAnyObjectByType<World>();
        if (world == null)
            Debug.LogError("BlockAnnihilator: World not set or found.");
    }

    private void OnTriggerEnter(Collider other)
    {
        // Check if collider is a Chunk
        if (!other.TryGetComponent<Chunk>(out Chunk chunk)) return;

        // Skip if recently destroyed
        if (IsRecentlyDestroyed(chunk)) return;

        EnqueueChunkDestruction(chunk);
        chunkDestructionTimestamp[chunk] = Time.time;
    }

    private void OnTriggerStay(Collider other)
    {
        // Throttle frequent checks
        if (Time.time < nextStayTime) return;
        nextStayTime = Time.time + stayCheckInterval;

        if (!other.TryGetComponent<Chunk>(out Chunk chunk)) return;
        if (IsRecentlyDestroyed(chunk)) return;

        EnqueueChunkDestruction(chunk);
        chunkDestructionTimestamp[chunk] = Time.time;
    }

    private void Update()
    {
        // If no work, skip
        if (workQueue.Count == 0 && chunksNeedingRebuild.Count == 0)
            return;

        // Cache position for partial checks
        Vector3 annihilatorPos = transform.position;

        // Process a few slices to avoid frame spikes
        for (int i = 0; i < slicesPerFrame; i++)
        {
            if (workQueue.Count == 0) break;

            var (chunk, start, end) = workQueue.Dequeue();
            if (start < 0)
            {
                // Negative start => fully inside => destroy all blocks
                DestroyAllBlocksInChunk(chunk);
            }
            else
            {
                // Partial => distance checks
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
    /// Check if a chunk was destroyed recently (within recheckCooldown).
    /// </summary>
    private bool IsRecentlyDestroyed(Chunk chunk)
    {
        if (!chunkDestructionTimestamp.TryGetValue(chunk, out float lastTime))
            return false;

        return (Time.time - lastTime < recheckCooldown);
    }

    /// <summary>
    /// Checks bounding-sphere intersection for the chunk.
    /// If fully inside => single pass, else break into partial slices.
    /// </summary>
    private void EnqueueChunkDestruction(Chunk chunk)
    {
        if (!ChunkWithinSphereCheck(chunk, out bool fullyInside))
            return;

        int blockCount = chunk.width * chunk.height * chunk.depth;

        if (fullyInside)
        {
            // -1 => destroy entire chunk in one pass
            workQueue.Enqueue((chunk, -1, -1));
        }
        else
        {
            // Partial => break into slices
            for (int start = 0; start < blockCount; start += sliceSize)
            {
                int end = Mathf.Min(start + sliceSize, blockCount);
                workQueue.Enqueue((chunk, start, end));
            }
        }
    }

    /// <summary>
    /// If chunk is fully inside => destroy ALL blocks (including water).
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
            // If not AIR => set to AIR
            if (chunk.chunkData[i] != BlockType.AIR)
            {
                chunk.chunkData[i] = BlockType.AIR;
                destroyed = true;
                MarkNeighborsForRebuild(chunk, i);
            }
        }

        if (destroyed && !chunksNeedingRebuild.Contains(chunk))
            chunksNeedingRebuild.Add(chunk);
    }

    /// <summary>
    /// For partial slices, do distance checks. If within radius => destroy block.
    /// No water skip => water is also destroyed.
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
            if (chunk.chunkData[i] == BlockType.AIR)
                continue;

            // Compute block center
            int x = i % w;
            int yz = i / w;
            int y = yz % h;
            int z = yz / h;

            float wx = cOrigin.x + x + 0.5f;
            float wy = cOrigin.y + y + 0.5f;
            float wz = cOrigin.z + z + 0.5f;

            // squared distance check
            Vector3 diff = annihilatorPos - new Vector3(wx, wy, wz);
            if (diff.sqrMagnitude <= sqRad)
            {
                chunk.chunkData[i] = BlockType.AIR;
                destroyed = true;
                MarkNeighborsForRebuild(chunk, i);
            }
        }

        if (destroyed && !chunksNeedingRebuild.Contains(chunk))
            chunksNeedingRebuild.Add(chunk);
    }

    /// <summary>
    /// Determine if chunk is fully, partially, or not at all inside sphereRadius.
    /// If fully inside => no partial checks.
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
            return false; // outside

        if (dist + chunkRadius < sphereRadius)
        {
            fullyInside = true; // entire chunk inside
            return true;
        }

        return true; // partial intersection
    }

    /// <summary>
    /// Marks the chunk + neighbors for rebuild so geometry updates
    /// after removing a block.
    /// </summary>
    private void MarkNeighborsForRebuild(Chunk chunk, int flatIndex)
    {
        int w = chunk.width;
        int h = chunk.height;
        int d = chunk.depth;

        // Convert flatten index -> local coords
        int x = flatIndex % w;
        int yz = flatIndex / w;
        int y = yz % h;
        int z = yz / h;

        // Mark chunk
        if (!chunksNeedingRebuild.Contains(chunk))
            chunksNeedingRebuild.Add(chunk);

        // Mark neighbor chunks if boundary blocks are removed
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
}
