using UnityEngine;
using static MeshUtils;

[RequireComponent(typeof(ParticleSystem))]
public class RainReplicator : MonoBehaviour
{
    [Tooltip("Reference to the voxel 'World' object.")]
    public World world;

    [Header("Rain Settings")]
    [Tooltip("Horizontal region (±) in x,z for random drops.")]
    public float rainArea = 50f;

    [Tooltip("The height above the terrain at which we spawn the 'drop ray'.")]
    public float spawnHeight = 60f;

    [Tooltip("Max angle from vertical. 0=straight down, 90=hemisphere, etc.")]
    public float maxAngle = 35f;

    [Tooltip("How frequently (sec) we do a drop check.")]
    public float checkInterval = 0.2f;

    // Next time we attempt a 'raindrop' check
    private float nextCheckTime;

    // For gizmo drawing: track the most recent ray
    private Vector3 lastRayStart;
    private Vector3 lastRayDir;
    private float lastRayLength;

    void Awake()
    {
        // If no World is assigned, try auto-finding one in the scene
        if (world == null)
        {
            world = FindObjectOfType<World>();
            if (world == null)
            {
                Debug.LogError("RainReplicator: No 'World' found in the scene.");
            }
        }
    }

    void Update()
    {
        // At intervals, do a single 'rain' raycast
        if (Time.time >= nextCheckTime)
        {
            nextCheckTime = Time.time + checkInterval;
            TrySpawnDropletAndCheck();
        }
    }

    /// <summary>
    /// 1) Pick a random (x,z) within rainArea.
    /// 2) Generate an angled ray (up to maxAngle from vertical).
    /// 3) Raycast. If we hit a chunk => replicate block adjacent to that face.
    /// </summary>
    private void TrySpawnDropletAndCheck()
    {
        float rx = Random.Range(-rainArea, rainArea);
        float rz = Random.Range(-rainArea, rainArea);

        // Start pos is offset from this transform + spawnHeight
        Vector3 startPos = new Vector3(rx, spawnHeight, rz) + transform.position;

        // (A) Random direction in a cone of maxAngle from vertical (down)
        float angle = Random.Range(0f, maxAngle);
        float rotation = Random.Range(0f, 360f);
        Quaternion tilt = Quaternion.Euler(angle, rotation, 0f);
        Vector3 castDir = tilt * Vector3.down;

        // We'll do a ray up to spawnHeight * 2f
        float castLength = spawnHeight * 2f;

        // Now do the angled ray
        if (Physics.Raycast(startPos, castDir, out RaycastHit hit, castLength))
        {
            if (hit.collider.TryGetComponent<Chunk>(out Chunk chunk))
            {
                Vector3 hitPoint = hit.point - hit.normal * 0.01f;
                ReplicateBlockAtHit(chunk, hit, hitPoint);
            }

            // If you want to show the exact distance to the hit
            lastRayLength = hit.distance;
        }
        else
        {
            // If we didn't hit anything, we can visualize the full cast
            lastRayLength = castLength;
        }

        // For gizmo drawing
        lastRayStart = startPos;
        lastRayDir = castDir;
    }

    /// <summary>
    /// Determine which block was hit, then replicate that block in the adjacent cell
    /// in the direction of the face normal (top, bottom, front, back, left, right).
    /// </summary>
    private void ReplicateBlockAtHit(Chunk chunk, RaycastHit hit, Vector3 hitPoint)
    {
        if (chunk == null || world == null) return;

        // The chunk's world-space "origin"
        Vector3Int chunkOrigin = Vector3Int.FloorToInt(chunk.location);

        // Convert hitPoint into local chunk coords
        int localX = Mathf.FloorToInt(hitPoint.x - chunkOrigin.x);
        int localY = Mathf.FloorToInt(hitPoint.y - chunkOrigin.y);
        int localZ = Mathf.FloorToInt(hitPoint.z - chunkOrigin.z);

        // If out of chunk bounds, handle cross-chunk if needed or skip
        if (localX < 0 || localX >= chunk.width ||
            localY < 0 || localY >= chunk.height ||
            localZ < 0 || localZ >= chunk.depth)
        {
            return;
        }

        int hitIndex = localX + chunk.width * (localY + chunk.depth * localZ);
        BlockType oldType = chunk.chunkData[hitIndex];

        // Skip if the hit block is AIR or WATER (replicate only solids, for example)
        if (oldType == BlockType.AIR || oldType == BlockType.WATER)
            return;

        // Round the normal to an integer offset
        Vector3 n = hit.normal;
        int nx = Mathf.RoundToInt(n.x);
        int ny = Mathf.RoundToInt(n.y);
        int nz = Mathf.RoundToInt(n.z);

        // The adjacent cell in direction of that face
        int targetX = localX + nx;
        int targetY = localY + ny;
        int targetZ = localZ + nz;

        // Check bounds again
        if (targetX < 0 || targetX >= chunk.width ||
            targetY < 0 || targetY >= chunk.height ||
            targetZ < 0 || targetZ >= chunk.depth)
        {
            // If your world supports cross-chunk boundaries, you'd do that here.
            return;
        }

        int replicateIndex = targetX + chunk.width * (targetY + chunk.depth * targetZ);

        // If the target cell is AIR => replicate
        if (chunk.chunkData[replicateIndex] == BlockType.AIR)
        {
            chunk.chunkData[replicateIndex] = oldType;
            chunk.healthData[replicateIndex] = BlockType.NOCRACK;

            // Rebuild chunk to see changes
            world.RedrawChunk(chunk);

            // If needed, mark neighbor chunks for rebuild if the cell is on a boundary
            // ...
        }
    }

    /// <summary>
    /// Visualize the last ray we fired in the Editor.
    /// </summary>
    private void OnDrawGizmos()
    {
        // Draw the region of random X,Z selection (rainArea)
        // A circle or wire disc for visualization at ground level
        Gizmos.color = Color.blue * 0.3f;
        Gizmos.DrawWireSphere(transform.position, rainArea);

        // If we haven't run in play mode yet, no ray
        if (Application.isPlaying)
        {
            // Show the last cast ray in cyan
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(lastRayStart, lastRayDir * lastRayLength);
        }
    }
}
