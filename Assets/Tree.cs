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
/// </summary>
public class Tree : MonoBehaviour
{
    [Header("Required References")]
    public World world;              // The same World your agent uses
    public GameObject agentPrefab;   // The agent to spawn at the end of growth

    [Header("Growth & Lifetime Settings")]
    [Tooltip("How many real-world minutes the tree will take to grow. Then it spawns an agent and destroys itself.")]
    public float lifetimeMinutes = 15f;

    [Tooltip("Multiply the tree’s localScale.y by this factor from start to end of growth.")]
    public float growthFactor = 15f;

    // If your seed has a Rigidbody and is physically falling, it might bounce a bit.
    // We'll wait for it to settle for a short time before the final check.
    private float settleWaitTime = 0.1f;

    private IEnumerator Start()
    {
        world = FindObjectOfType<World>();
        // 1) Repeatedly wait until the seed is actually on some block
        //    (i.e. the block below is not AIR/out-of-bounds).
        //    Then ensure that block is either GRASSTOP or GRASSSIDE.
        yield return StartCoroutine(WaitForValidSoil());

        // 2) Smoothly grow over the entire lifetime
        float totalSeconds = lifetimeMinutes * 60f;
        yield return StartCoroutine(GrowOverTime(totalSeconds));

        // 3) Spawn an agent at the top
        SpawnAgent();

        // 4) Destroy this seed/gameObject
        Destroy(gameObject);
    }

    /// <summary>
    /// Wait until the seed is resting on a non-AIR block.
    /// Once it's on a block, check if it's GRASSTOP or GRASSSIDE.
    /// If invalid => destroy. If valid => return.
    /// </summary>
    private IEnumerator WaitForValidSoil()
    {
        if (world == null)
        {
            Debug.LogWarning("[Tree] No World reference assigned; destroying seed.");
            Destroy(gameObject);
            yield break;
        }

        while (true)
        {
            // Check the block below the seed
            int bx = Mathf.FloorToInt(transform.position.x);
            int bz = Mathf.FloorToInt(transform.position.z);
            int by = Mathf.FloorToInt(transform.position.y - 1f);

            // If out of bounds or it's AIR => keep waiting
            if (!world.InBounds(bx, by, bz))
            {
                yield return new WaitForSeconds(settleWaitTime);
                continue;
            }

            MeshUtils.BlockType bType = world.GetBlockType(bx, by, bz);

            if (bType == MeshUtils.BlockType.AIR)
            {
                // seed not yet on the ground
                yield return new WaitForSeconds(settleWaitTime);
                continue;
            }

            // Now we have a non-AIR block. Check if it's GRASSTOP / GRASSSIDE
            if (bType == MeshUtils.BlockType.GRASSTOP || bType == MeshUtils.BlockType.GRASSSIDE)
            {
                // valid => we're done checking
                Debug.Log("[Tree] Valid grass soil found. Starting growth.");
                yield break;
            }
            else
            {
                Debug.Log($"[Tree] Invalid soil: {bType}. Destroying seed.");
                Destroy(gameObject);
                yield break;
            }
        }
    }

    /// <summary>
    /// Scales the tree from its initial localScale.y to initialScale.y * growthFactor
    /// linearly over 'growSeconds' real-time seconds.
    /// </summary>
    private IEnumerator GrowOverTime(float growSeconds)
    {
        Vector3 initialScale = transform.localScale;
        Vector3 targetScale = new Vector3(
            initialScale.x,
            initialScale.y * growthFactor,
            initialScale.z
        );

        float elapsed = 0f;
        while (elapsed < growSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / growSeconds);

            float newY = Mathf.Lerp(initialScale.y, targetScale.y, t);
            transform.localScale = new Vector3(initialScale.x, newY, initialScale.z);

            yield return null;
        }
    }

    /// <summary>
    /// Spawns a new agent near the "top" of the final grown tree.
    /// </summary>
    private void SpawnAgent()
    {
        if (agentPrefab == null)
        {
            Debug.LogWarning("[Tree] No agentPrefab assigned.");
            return;
        }

        float halfHeight = transform.localScale.y * 0.5f;
        float spawnY = transform.position.y + halfHeight + 1f; // +1 offset
        Vector3 spawnPos = new Vector3(transform.position.x, spawnY, transform.position.z);

        Instantiate(agentPrefab, spawnPos, Quaternion.identity);
        Debug.Log($"[Tree] Spawned agent at {spawnPos} after full growth.");
    }
}
