using UnityEngine;
using System.Collections;

/// <summary>
/// Attach this script to the seedPrefab.
/// 1) Repeatedly checks the block below; if it's AIR or out-of-bounds, we keep waiting.
///    Once the seed is actually on a block, we verify it is GRASSTOP or GRASSSIDE.
///    If not valid, we destroy the seed. 
/// 2) If valid grass, we grow smoothly from initial localScale.y up to localScale.y * growthFactor
///    over the entire lifetimeMinutes duration.
/// 3) When growth finishes, we spawn an agent and immediately destroy the seed.
/// 4) If this seed collides with another seed AND this seed is physically above the other seed,
///    we destroy this seed.
/// </summary>
public class TreeGrow : MonoBehaviour
{
    [Header("Required References")]
    public World world;            // The same World your agent uses

    [Header("Growth & Lifetime Settings")]
    [Tooltip("How many real-world minutes the tree will take to grow. Then it spawns an agent and destroys itself.")]
    public float lifetimeMinutes = 15000f;

    [Tooltip("Multiply the tree’s localScale.y by this factor from start to end of growth.")]
    public float growthFactor;

    // We'll wait 0.1s between checks to avoid spamming
    private float settleWaitTime = 0.1f;
    private bool destroy = false;
    public bool isGrowing = false;  // if you want to track whether the seed is actively growing

    public Mesh treeMesh;
    public Material treeMaterial;

    private IEnumerator Start()
    {
        // If world is not assigned in Inspector, try a fallback (Unity 2023.1+)
        if (world == null)
        {
            // If you're on Unity 2023.1 or newer:
            world = Object.FindAnyObjectByType<World>();

            // Otherwise, if older Unity, revert to:
            // world = FindObjectOfType<World>();
        }

        // 1) Wait for valid soil or destroy if invalid
        yield return StartCoroutine(WaitForValidSoil());

        // If the script flagged "destroy", do so now.
        if (destroy)
        {
            Destroy(gameObject);
            yield break; // End coroutine to avoid continuing
        }

        if (this == null)
            yield break;

        // 2) Smoothly grow over the entire lifetime
        float totalSeconds = lifetimeMinutes * 60f;
        yield return StartCoroutine(GrowOverTime(totalSeconds));

        if (this == null)
            yield break;

        // 3) Spawn an agent at the top
        //SpawnAgent();

        // 4) Finally, destroy this seed/gameObject
        yield return new WaitForSecondsRealtime(600f);  // 600 seconds = 10 minutes
        Destroy(gameObject);
        yield break;

    }

    /// <summary>
    /// Repeatedly checks the block below until:
    ///  - It's NOT AIR/out-of-bounds
    ///  - If GRASSTOP/GRASSSIDE => valid => break
    ///  - Else => mark 'destroy' => break
    /// </summary>
    private IEnumerator WaitForValidSoil()
    {
        if (world == null)
        {
            Debug.LogWarning("[TreeGrow] No World found; destroying seed.");
            destroy = true;
            yield break;
        }

        while (true)
        {
            int bx = Mathf.FloorToInt(transform.position.x);
            int bz = Mathf.FloorToInt(transform.position.z);
            int by = Mathf.FloorToInt(transform.position.y - 1f);

            if (!world.InBounds(bx, by, bz))
            {
                yield return new WaitForSeconds(settleWaitTime);
                if (this == null)
                    yield break;
                continue;
            }

            MeshUtils.BlockType bType = world.GetBlockType(bx, by, bz);

            if (bType == MeshUtils.BlockType.AIR)
            {
                yield return new WaitForSeconds(settleWaitTime);
                if (this == null)
                    yield break;
                continue;
            }

            if (bType == MeshUtils.BlockType.GRASSTOP || bType == MeshUtils.BlockType.GRASSSIDE || bType == MeshUtils.BlockType.DIRT)
            {
                Debug.Log($"[TreeGrow] Valid grass soil: {bType}. Starting growth.");
                yield break;
            }
            else
            {
                Debug.Log($"[TreeGrow] Invalid soil: {bType}. Destroying seed.");
                destroy = true;
                yield break;
            }
        }
    }

    /// <summary>
    /// Grows from current localScale.y to localScale.y * growthFactor 
    /// over 'growSeconds' real-time seconds.
    /// </summary>
    private IEnumerator GrowOverTime(float growSeconds)
    {
        isGrowing = true;

        // Save the starting position and scale
        Vector3 startPos = transform.position;
        Vector3 initialScale = transform.localScale;
        Vector3 finalScale = initialScale * (growthFactor / 2f);

        float elapsed = 0f;
        while (elapsed < growSeconds)
        {
            if (this == null)
                yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / growSeconds);

            // Compute new scale
            Vector3 oldScale = transform.localScale;
            Vector3 newScale = Vector3.Lerp(initialScale, finalScale, t);
            transform.localScale = newScale;

            // Because pivot is in the center, half of the difference in height 
            // would go downward. We shift the object up by half of that difference.
            float oldY = oldScale.y;
            float newY = newScale.y;
            float diffY = newY - oldY;
            float upwardShift = diffY * 0.5f;  // half the difference

            // Adjust position to keep the bottom in place
            // (Assuming Y is your up-axis)
            transform.position += new Vector3(0, upwardShift/50, 0);

            yield return null;
        }

        // Optionally snap final scale:
        transform.localScale = finalScale;
        isGrowing = false;
    }


    /// <summary>
    /// If this seed collides with another seed that is below it, we destroy THIS seed.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        TreeGrow otherSeed = collision.gameObject.GetComponent<TreeGrow>();
        if (otherSeed != null && otherSeed != this)
        {
            // Check if THIS seed's Y is greater than OTHER seed's Y => this seed is "on top"
            if (transform.position.y > otherSeed.transform.position.y)
            {
                Debug.Log("[TreeGrow] This seed is on top of another seed => destroying this seed.");
                Destroy(gameObject);
            }
        }
    }
}
