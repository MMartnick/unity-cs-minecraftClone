using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;
using UnityEngine.Rendering;
using static MeshUtils;

/// <summary>
/// Agent that spawns on top of the highest solid block plus 1 in Y,
/// scans a 3×3×3 area around itself for observations & location score,
/// can attempt movement or planting, and collects rewards accordingly.
/// </summary>
public class ResourceAgent : Agent
{
    [Header("World Reference")]
    public World world;                // The voxel world to reference
    public GameObject seedPrefab;

    [Tooltip("If each block is 1 meter, set blockScale=1.")]
    public float blockScale = 1f;

    [Tooltip("Seconds between moves; reduce for faster training.")]
    public float moveInterval = 30f;   // Time between agent moves
    private float nextMoveTime = 0f;

    [Tooltip("Agent moves smoothly to the target position in Update().")]
    public float moveSpeed = 5f;
    private Vector3 _targetPosition;
    private bool _isMoving = false;

    // The agent’s block coordinates in the world. We treat these as float,
    // but you can round to int when needed (especially for scanning).
    private float gridX, gridY, gridZ;

    private (int x, int y, int z) lastPos;   // For stuck-check
    private int stepsSinceLastMove = 0;

    // Seeds / planting
    private HashSet<Vector2Int> plantedLocations = new HashSet<Vector2Int>();
    private float bestLocationValue = 0f;    // Tracks best discovered location
    public Material[] possibleMaterials;

    // For scanning + caching
    private List<Vector3> scannedPositions = new List<Vector3>();
    private List<MeshUtils.BlockType> cachedBlocks = new List<MeshUtils.BlockType>();
    private float cachedScore = 0f;

    // Extended to track visited columns for exploration reward
    private HashSet<Vector3Int> visitedPositions = new HashSet<Vector3Int>();

    // Static counters (for debug/monitoring)
    [SerializeField] private static int globalStepCount = 0;
    [SerializeField] private static int episodeCount = 0;
    [SerializeField] private static int totalSeedsPlanted = 0;

    [Header("Scoring Threshold")]
    public float scoreThreshold =20f;  // For planting

    [Header("Rewards & Penalties")]
    public float stepPenalty = -0.005f;
    public float idlePenalty = -0.001f;
    public float invalidMovePenalty = -0.01f;
    public float badPlantPenalty = -5f;
    public float exploreReward = 1f;
    public float bestLocationBonus = 5f;

    /// <summary>
    /// Called once at agent initialization; we do a spawn + position.
    /// </summary>
    public override void Initialize()
    {
        RandomizeSpawn();
        SetAgentAboveBlock(gridX, gridY, gridZ); // Position agent visually
        transform.position = _targetPosition;

        lastPos = ((int)gridX, (int)gridY, (int)gridZ);
        stepsSinceLastMove = 0;

        visitedPositions.Clear();
        visitedPositions.Add(new Vector3Int((int)gridX, (int)gridY, (int)gridZ));
    }

    /// <summary>
    /// Called when a new episode begins (reset logic).
    /// </summary>
    public override void OnEpisodeBegin()
    {
        world?.ResetEnvironment();

        // Re-place agent at same coords
        SetAgentAboveBlock(gridX, gridY, gridZ);
        transform.position = _targetPosition;

        // Reset counters
        plantedLocations.Clear();
        visitedPositions.Clear();
        visitedPositions.Add(new Vector3Int((int)gridX, (int)gridY, (int)gridZ));

        bestLocationValue = 0f;
        lastPos = ((int)gridX, (int)gridY, (int)gridZ);
        stepsSinceLastMove = 0;
        episodeCount++;
    }

    /// <summary>
    /// Randomly pick X,Z in the world, find the top surface,
    /// then set (gridY) to that top surface + 1 so agent is on top.
    /// If invalid, fallback to (25,25,25).
    /// </summary>
    private void RandomizeSpawn()
    {
        int totalX = (World.worldDimensions.x + World.extraWorldDimensions.x) * World.chunkDimensions.x;
        int totalZ = (World.worldDimensions.z + World.extraWorldDimensions.z) * World.chunkDimensions.z;

        int randX = UnityEngine.Random.Range(0, totalX);
        int randZ = UnityEngine.Random.Range(0, totalZ);

        int topY = FindTopSurface(randX, randZ);

        // If no valid surface found, fallback
        if (topY < 20)
        {
            int randStartX = UnityEngine.Random.Range(10, totalX);
            int randStartZ = UnityEngine.Random.Range(10, totalZ);

                gridX = randStartX;
                gridY = 25;  // ensure we are above block
                gridZ = randStartZ;
        }
        else
        {
            gridX = randX;
            gridY = topY + 1;  // stand on top
            gridZ = randZ;
        }
    }

    /// <summary>
    /// Called once per frame; we do smooth movement toward target.
    /// </summary>
    private void Update()
    {
        EnsureAboveGround();

        if (!_isMoving) return;

        transform.position = Vector3.MoveTowards(
            transform.position,
            _targetPosition,
            moveSpeed * Time.deltaTime
        );

        // Snap if close
        if (Vector3.Distance(transform.position, _targetPosition) < 0.001f)
        {
            transform.position = _targetPosition;
            _isMoving = false;
        }
    }

    /// <summary>
    /// CollectObservations is called by ML-Agents. We do one scan that yields:
    /// - The 27 blocks in a 3×3×3 around agent
    /// - A boolean for overhead light
    /// Then we store the result for OnActionReceived as well.
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        // Clear from previous step
        scannedPositions.Clear();
        // Single method call to do scanning + compute score
        cachedBlocks = ScanAndComputeScore(gridX, gridY, gridZ, out cachedScore);

        // Send block types (27 ints) to the sensor
        foreach (var block in cachedBlocks)
        {
            sensor.AddObservation((int)block);
        }

        // Also observe overhead light
        bool overheadLit = world.IsLit((int)gridX, (int)gridY + 1, (int)gridZ);
        sensor.AddObservation(overheadLit ? 1 : 0);

        // 3) NEW: Observe if we've already planted here => boolean => 1 or 0
        bool alreadyPlanted = AlreadyPlantedHere();
        sensor.AddObservation(alreadyPlanted ? 1 : 0);
    }

    /// <summary>
    /// OnActionReceived is called after CollectObservations. We use the
    /// cached data from scanning to compute rewards, check planting, etc.
    /// </summary>
    public override void OnActionReceived(ActionBuffers actions)
    {
        int moveAction = actions.DiscreteActions[0];
        int plantAction = actions.DiscreteActions[1];

        // Step penalty
        AddReward(stepPenalty);

        // If it's not time to move, handle planting only
        if (Time.time < nextMoveTime)
        {
            HandlePlanting(plantAction);
            return;
        }

        // Movement or idle
        if (moveAction == 0) AddReward(idlePenalty);
        else AttemptMovement(moveAction);

        // Compare location value to best discovered
        if (cachedScore > bestLocationValue)
        {
            bestLocationValue = cachedScore;
            AddReward(bestLocationBonus);
        }

        // Plant
        HandlePlanting(plantAction);

        // Stuck check
        var currentPos = ((int)gridX, (int)gridY, (int)gridZ);
        if (currentPos == lastPos)
        {
            stepsSinceLastMove++;
            if (stepsSinceLastMove > 200)
            {
                AddReward(-1f);
                EndEpisode();
            }
        }
        else
        {
            stepsSinceLastMove = 0;
            lastPos = currentPos;
        }

        // Debug counters
        globalStepCount++;
        if (globalStepCount % 1000 == 0)
        {
            Debug.Log($"Step {globalStepCount} | Ep {episodeCount} | " +
                      $"CumulReward: {GetCumulativeReward():F2} | " +
                      $"Seeds Planted (this ep): {plantedLocations.Count} | " +
                      $"Total Seeds: {totalSeedsPlanted}");
        }

        // Next move time
        nextMoveTime = Time.time + moveInterval;
    }

    /// <summary>
    /// Attempt to move the agent in block coords. Then call SetAgentAboveBlock
    /// to finalize the position. 
    /// </summary>
    private void AttemptMovement(int moveAction)
    {
        // Copy the agent's current coords (float)
        float bx = gridX;
        float by = gridY;
        float bz = gridZ;

        // 1) Determine the new coordinate based on the move action
        switch (moveAction)
        {
            case 1: bx -= 1; break;  // Left
            case 2: bx += 1; break;  // Right
            case 3: bz += 1; break;  // Forward
            case 4: bz -= 1; break;  // Back
            case 5: // Up
                if (CanMoveUp(gridX, gridY, gridZ)) by += 1;
                else { AddReward(invalidMovePenalty); return; }
                break;
            case 6: // Down
                if (CanMoveDown(gridX, gridY, gridZ)) by -= 1;
                else { AddReward(invalidMovePenalty); return; }
                break;
        }


        // 2) If it’s a horizontal move, do a simple “solid vs. not solid” check
        //    instead of doing the "FindTopSurface" approach.
        bool isHorizontal = (moveAction >= 1 && moveAction <= 4);
        if (isHorizontal)
        {
            // Round the new coords to int
            int nx = Mathf.RoundToInt(bx);
            int ny = Mathf.RoundToInt(by);
            int nz = Mathf.RoundToInt(bz);

            // Bounds check
            int totalX = (World.worldDimensions.x + World.extraWorldDimensions.x) * World.chunkDimensions.x;
            int totalZ = (World.worldDimensions.z + World.extraWorldDimensions.z) * World.chunkDimensions.z;
            if (nx < 0 || nx >= totalX || nz < 0 || nz >= totalZ)
            {
                AddReward(invalidMovePenalty);
                return;
            }

           

            // Make sure the block at (nx, ny, nz) is NOT a solid block
            // e.g., agent can move if it's air or water
            MeshUtils.BlockType bType = world.GetBlockType(nx, ny, nz);
            if (IsSolidBlock(bType))
            {
                // We disallow moving inside a solid block
                AddReward(invalidMovePenalty);
                return;
            }

            if(bType == MeshUtils.BlockType.BEDROCK || bType == MeshUtils.BlockType.STONE || ny <= 10)
            {
                ny = 30;
            }

            // If we pass the check, the agent is allowed to go there
            bx = nx;
            by = ny;
            bz = nz;

            int nextSurfaceY = FindTopSurface(nx, nz);
            if (by <= nextSurfaceY)
            {
                by = nextSurfaceY +1;
            }

            if (CanMoveDown(bx, by, bz))
            {
                if (!IsSolidBlock(bType) && by > FindTopSurface((int)bx, (int)bz)) { 
                by -= 1; // forcibly drop one block
                }
         
            }
        }

        // 3) Update agent coords (float), place the agent visually
        gridX = bx;
        gridY = by;
        gridZ = bz;
        SetAgentAboveBlock(gridX, gridY, gridZ);

        // 4) Exploration reward if new block hasn't been visited
        var newPos = new Vector3Int(Mathf.RoundToInt(gridX),
                                    Mathf.RoundToInt(gridY),
                                    Mathf.RoundToInt(gridZ));
        if (!visitedPositions.Contains(newPos))
        {
            visitedPositions.Add(newPos);
            AddReward(exploreReward);
        }
    }

    // ------------------------------------------------------------------------
    // VERTICAL CHECKS
    // ------------------------------------------------------------------------

    private bool CanMoveUp(float x, float y, float z)
    {
        // Next block up => (y+1). Make sure it’s in-bounds and not solid
        int nx = Mathf.RoundToInt(x);
        int ny = Mathf.RoundToInt(y + 1);
        int nz = Mathf.RoundToInt(z);

        // 1) Bounds
        if (!world.InBounds(nx, ny, nz)) return false;

        // 2) Solid check => disallow if it's e.g. stone, sand, etc.
        MeshUtils.BlockType bType = world.GetBlockType(nx, ny, nz);
        if (IsSolidBlock(bType)) return false;
        if (IsSolidBlock(bType) && transform.position.y < 15) return true;


        return true; // air or water is OK
    }

    private bool CanMoveDown(float x, float y, float z)
    {
        // Next block down => (y-1). Make sure it’s in-bounds and not solid
        int nx = Mathf.RoundToInt(x);
        int ny = Mathf.RoundToInt(y - 1);
        int nz = Mathf.RoundToInt(z);

        if (!world.InBounds(nx, ny, nz)) return false;

        // If that block is solid => no
        MeshUtils.BlockType bType = world.GetBlockType(nx, ny, nz);
        if (IsSolidBlock(bType)) return false;

        return true; // air or water is OK
    }

    /// <summary>
    /// Keep the agent “riding the terrain”.  
    /// If it is **inside any solid block, touching BEDROCK, or sitting at/below
    /// the highest solid voxel for the current X-Z column**, we pop it up to
    ///   (topSurface + 1).
    /// </summary>
    private void EnsureAboveGround()
    {
        int ix = Mathf.RoundToInt(gridX);
        int iz = Mathf.RoundToInt(gridZ);

        // Bail if the X-Z column is outside the generated world.
        if (!world.InBounds(ix, 0, iz))
            return;

        // Highest solid block in this column (BEDROCK counts as solid).
        int topSurface = FindTopSurface(ix, iz);   // returns 0 if only bedrock

        // Where the agent *should* stand.
        int desiredY = topSurface + 1;

        // What block is the agent currently in?
        int iy = Mathf.RoundToInt(gridY);
        MeshUtils.BlockType currentBlock = world.GetBlockType(ix, iy, iz);
        bool insideSolid = IsSolidBlock(currentBlock);   // true for BEDROCK too

        //–––  Teleport up when any of the following are true ––––––––––––––––––––
        //   • agent is intersecting a solid voxel
        //   • agent is at/below the terrain surface
        //   • agent’s Y fell below zero for any reason
        //------------------------------------------------------------------------
        if (insideSolid || iy <= topSurface || iy <= 10)
        {
            gridY = desiredY;
            SetAgentAboveBlock(gridX, gridY, gridZ);   // updates _targetPosition
            _isMoving = false;                         // cancel any smooth-move
        }
    }



    // ------------------------------------------------------------------------
    // HELPER: Decide if a block is "solid" => agent can’t move into it
    // ------------------------------------------------------------------------
    private bool IsSolidBlock(MeshUtils.BlockType blockType)
    {
        // If your definition of 'solid' excludes water or air => 
        // return true for any block that is not air or water
        // e.g. stone, grass, dirt, etc.
        switch (blockType)
        {
            case MeshUtils.BlockType.AIR:
            case MeshUtils.BlockType.WATER:
                return false;
            default:
                return true;
        }
    }


    /// <summary>
    /// Handle planting if the agent chooses to. We compare locScore to threshold,
    /// check if block below is valid, check if already planted, then spawn a seed.
    /// </summary>
    private void HandlePlanting(int plantAction)
    {
        if (plantAction != 1) return;

        float locScore = cachedScore;
        if (locScore < scoreThreshold)
        {
            AddReward(badPlantPenalty);
            return;
        }

        // 1) Check if the agent's current block is water
        int ix = Mathf.RoundToInt(gridX);
        int iy = Mathf.RoundToInt(gridY);
        int iz = Mathf.RoundToInt(gridZ);

        MeshUtils.BlockType currentBlock = world.GetBlockType(ix, iy, iz);
        if (currentBlock == MeshUtils.BlockType.WATER)
        {
            // Penalize heavily for trying to plant while submersed
            AddReward(-100f);
            return;
        }

        // 2) Check block below
        if (!world.InBounds(ix, iy - 1, iz))
        {
            // Out-of-bounds => penalize
            AddReward(badPlantPenalty);
            return;
        }

        MeshUtils.BlockType belowBlock = world.GetBlockType(ix, iy - 1, iz);
        if (belowBlock == MeshUtils.BlockType.WATER)
        {
            // Also penalize for water below
            AddReward(-20f);
            return;
        }
        else if (IsSolidBlock(belowBlock) == false)
        {
            // If the block below is air/water => not a valid soil
            // (But here, we already returned if water, so must be air => also penalize?)
            AddReward(badPlantPenalty);
            return;
        }

        // 3) Check if we already planted here
        Vector2Int currentPos2D = new Vector2Int(ix, iz);
        if (plantedLocations.Contains(currentPos2D))
        {
            AddReward(-50f);
            return;
        }

        // If we get here, it's a valid planting
        plantedLocations.Add(currentPos2D);
        totalSeedsPlanted++;

        float plantingReward = locScore / scoreThreshold;
        AddReward(plantingReward);
        AddReward(100);
        // Place seed physically
        // Example spawn pos: (gridX, gridY-0.5f, gridZ)*blockScale
        Vector3 spawnPos = new Vector3(
            (gridX) * blockScale,
            (gridY - 0.5f) * blockScale,
            (gridZ) * blockScale
        );

        GameObject newSeed = Instantiate(seedPrefab, spawnPos, Quaternion.identity);
        var grow = newSeed.GetComponent<TreeGrow>();
        if (grow) grow.growthFactor = locScore;

        // Random material
        if (possibleMaterials != null && possibleMaterials.Length > 0)
        {
            Renderer r = newSeed.GetComponent<Renderer>();
            if (r)
            {
                int index = UnityEngine.Random.Range(0, possibleMaterials.Length);
                r.material = possibleMaterials[index];
            }
        }

        world.PlantSeedAt(currentPos2D);
    }

    /// <summary>
    /// Check if the block below is invalid for planting (stone, water, etc.)
    /// </summary>
    private bool IsInvalidBelow(int x, int y, int z)
    {
        if (!world.InBounds(x, y, z)) return true;
        MeshUtils.BlockType b = world.GetBlockType(x, y, z);
        // Example: stone or water or whatever is disallowed
        return (b == MeshUtils.BlockType.STONE || b == MeshUtils.BlockType.WATER);
    }

    /// <summary>
    /// Place the agent physically in the scene. (gridX, gridY, gridZ) are block coords,
    /// so multiply by blockScale and add +0.5 offset for x/z if you want exact center.
    /// </summary>
    private void SetAgentAboveBlock(float bx, float by, float bz)
    {
        float xWorld = (bx + 0.5f) * blockScale;
        float yWorld = (by) * blockScale;  // 'by' is already topSurface+1
        float zWorld = (bz + 0.5f) * blockScale;

        // Final check
        if (float.IsNaN(xWorld) || float.IsNaN(yWorld) || float.IsNaN(zWorld) ||
            float.IsInfinity(xWorld) || float.IsInfinity(yWorld) || float.IsInfinity(zWorld))
        {
            Debug.LogWarning($"[SetAgentAboveBlock] Invalid coords => {xWorld}, {yWorld}, {zWorld}. Movement skipped.");
            return;
        }

        _targetPosition = new Vector3(xWorld, yWorld, zWorld);
        _isMoving = true;
    }

    /// <summary>
    /// Find top surface (highest solid block) at (x, z). If none, return 0 or fallback.
    /// </summary>
    private int FindTopSurface(int x, int z)
    {
        int maxY = (World.worldDimensions.y + World.extraWorldDimensions.y) * World.chunkDimensions.y;
        for (int checkY = maxY; checkY >= 0; checkY--)
        {
            if (!world.InBounds(x, checkY, z)) continue;

            var blockType = world.GetBlockType(x, checkY, z);
            if (blockType != MeshUtils.BlockType.AIR && blockType != MeshUtils.BlockType.WATER)
            {
                return checkY;
            }
        }
        return 0; // or 1
    }

    /// <summary>
    /// Returns true if we already planted at the current X,Z block.
    /// </summary>
    private bool AlreadyPlantedHere()
    {
        // Round to int or floor, whichever you are using for block coords
        int ix = Mathf.RoundToInt(gridX);
        int iz = Mathf.RoundToInt(gridZ);

        // Return whether our plantedLocations set contains that coordinate
        return plantedLocations.Contains(new Vector2Int(ix, iz));
    }


    /// <summary>
    /// Single method that scans the 3×3×3 region around (cx,cy,cz),
    /// populates scannedPositions for Gizmos, sums up the block scores,
    /// plus a bonus if lit overhead. Returns the block list + out float score.
    /// </summary>
    private List<MeshUtils.BlockType> ScanAndComputeScore(float cx, float cy, float cz, out float score)
    {
        // We'll store the blocks we find
        var blockList = new List<MeshUtils.BlockType>();
        float sum = 0f;

        // Clear scannedPositions for Gizmo usage
        scannedPositions.Clear();

        // Round to int once, if you'd like
        int icx = Mathf.RoundToInt(cx);
        int icy = Mathf.RoundToInt(cy);
        int icz = Mathf.RoundToInt(cz);

        // 3×3×3 loop
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = icx + dx;
                    int ny = icy + dy;
                    int nz = icz + dz;

                    if (!world.InBounds(nx, ny, nz)) continue;

                    MeshUtils.BlockType b = world.GetBlockType(nx, ny, nz);
                    blockList.Add(b);

                    // Accumulate block score
                    sum += BlockScoring.GetBlockScore(b);

                    // For debug drawing
                    scannedPositions.Add(new Vector3(nx, ny, nz));
                }
            }
        }

        // Bonus if lit overhead
        if (world.IsLit(icx, icy + 1, icz))
        {
            sum += 2f;
        }

        score = sum;
        return blockList;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        // We draw a small wire cube for each scanned position
        foreach (Vector3 pos in scannedPositions)
        {
            Gizmos.DrawWireCube(pos, new Vector3(0.5f, 0.5f, 0.5f));
        }
    }
}
