using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class ResourceAgent : Agent
{
    [Header("World Reference")]
    public World world;
    public GameObject seedPrefab;

    [Tooltip("If each block is 1 meter, set blockScale=1.")]
    public float blockScale = 1f;

    [Tooltip("Seconds between moves; reduce for faster training.")]
    public float moveInterval = 30f;
    private float nextMoveTime = 0f;

    [Tooltip("Agent moves smoothly to the target position in Update().")]
    public float moveSpeed = 5f;
    private Vector3 _targetPosition;
    private bool _isMoving = false;

    private int gridX, gridY, gridZ;  // topmost solid block under agent
    private (int x, int y, int z) lastPos;
    private int stepsSinceLastMove = 0;

    // Seeds / planting
    private HashSet<Vector2Int> plantedLocations = new HashSet<Vector2Int>();
    private float bestLocationValue = 0f;
    public Material[] possibleMaterials;

    float localScore;

    // Extended to track visited columns for exploration reward
    private HashSet<Vector3Int> visitedPositions = new HashSet<Vector3Int>();

    // static counters
    [SerializeField] private static int globalStepCount = 0;
    [SerializeField] private static int episodeCount = 0;
    [SerializeField] private static int totalSeedsPlanted = 0;

    [Header("Scoring Threshold")]
    public float scoreThreshold = 50f;  // For planting

    [Header("Rewards & Penalties")]
    public float stepPenalty = -0.005f;
    public float idlePenalty = -0.001f;
    public float invalidMovePenalty = -0.01f;
    public float badPlantPenalty = -5f;
    public float exploreReward = 1f;
    public float bestLocationBonus = 5f;

    public override void Initialize()
    {
        RandomizeSpawn();
        SetAgentAboveBlock(gridX, gridY, gridZ);
        transform.position = _targetPosition;

        lastPos = (gridX, gridY, gridZ);
        stepsSinceLastMove = 0;

        visitedPositions.Clear();
        visitedPositions.Add(new Vector3Int(gridX, gridY, gridZ));
    }

    public override void OnEpisodeBegin()
    {
        if (world != null)
            world.ResetEnvironment();

        // Place agent at same coords
        SetAgentAboveBlock(gridX, gridY, gridZ);
        transform.position = _targetPosition;

        // Reset counters
        plantedLocations.Clear();
        visitedPositions.Clear();
        visitedPositions.Add(new Vector3Int(gridX, gridY, gridZ));

        bestLocationValue = 0f;
        lastPos = (gridX, gridY, gridZ);
        stepsSinceLastMove = 0;
        episodeCount++;
    }

    /// <summary>
    /// Randomly pick X,Z, then find the top surface.  
    /// If invalid, fallback to (25,25,25).
    /// </summary>
    private void RandomizeSpawn()
    {
        int totalX = (World.worldDimensions.x + World.extraWorldDimensions.x) * World.chunkDimensions.x;
        int totalZ = (World.worldDimensions.z + World.extraWorldDimensions.z) * World.chunkDimensions.z;

        int randX = Random.Range(0, totalX);
        int randZ = Random.Range(0, totalZ);

        int topY = FindTopSurface(randX, randZ);
        if (topY < 20)
        {
            // fallback
            gridX = 25;
            gridY = 25;
            gridZ = 25;
        }
        else
        {
            gridX = randX;
            gridY = topY +20;
            gridZ = randZ;
        }
    }

    private void Update()
    {
        if (_isMoving)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                _targetPosition,
                moveSpeed * Time.deltaTime
            );
            if (Vector3.Distance(transform.position, _targetPosition) < 0.001f)
            {
                transform.position = _targetPosition;
                _isMoving = false;
            }
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        MeshUtils.BlockType belowBlock = world.GetBlockType(gridX, gridY, gridZ);
        sensor.AddObservation((int)belowBlock);

        bool isLit = world.IsLit(gridX, gridY + 1, gridZ);
        sensor.AddObservation(isLit ? 1 : 0);

        sensor.AddObservation(gridY);

        float threshold = 20f;
        float upVal = world.GetValueAt(gridX, gridZ + 1);
        float downVal = world.GetValueAt(gridX, gridZ - 1);
        float leftVal = world.GetValueAt(gridX - 1, gridZ);
        float rightVal = world.GetValueAt(gridX + 1, gridZ);

        sensor.AddObservation(upVal >= threshold ? 1 : 0);
        sensor.AddObservation(downVal >= threshold ? 1 : 0);
        sensor.AddObservation(leftVal >= threshold ? 1 : 0);
        sensor.AddObservation(rightVal >= threshold ? 1 : 0);

        float currentY = transform.position.y;
        Vector3 aheadPos = transform.position + transform.forward;
        Vector3 rightPos = transform.position + transform.right;

        float aheadY = world.GetTerrainHeight(aheadPos.x, aheadPos.z);
        float rightY = world.GetTerrainHeight(rightPos.x, rightPos.z);

        sensor.AddObservation(aheadY - currentY);
        sensor.AddObservation(rightY - currentY);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int moveAction = actions.DiscreteActions[0];
        int plantAction = actions.DiscreteActions[1];

        // Step penalty
        AddReward(stepPenalty);

        if (Time.time < nextMoveTime)
        {
            HandlePlanting(plantAction);
            return;
        }

        if (moveAction == 0)
        {
            // idle
            AddReward(idlePenalty);
        }
        else
        {
            AttemptMovement(moveAction);
        }

        // Check location value improvements
        float currentValue = ComputeLocationScore(gridX, gridY, gridZ);
        if (currentValue > bestLocationValue)
        {
            bestLocationValue = currentValue;
            AddReward(bestLocationBonus);
        }

        // Planting
        HandlePlanting(plantAction);

        // Stuck check
        if ((gridX, gridY, gridZ) == lastPos)
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
            lastPos = (gridX, gridY, gridZ);
        }

        globalStepCount++;
        if (globalStepCount % 1000 == 0)
        {
            Debug.Log($"Step {globalStepCount} | Episode {episodeCount} | " +
                      $"CumulativeReward: {GetCumulativeReward():F2} | " +
                      $"Seeds Planted (this episode): {plantedLocations.Count} | " +
                      $"Total Seeds Planted: {totalSeedsPlanted}");
        }

        nextMoveTime = Time.time + moveInterval;
    }

    private void AttemptMovement(int moveAction)
    {
        int bx = gridX;
        int by = gridY;
        int bz = gridZ;

        switch (moveAction)
        {
            case 1: bx = gridX - 1; break;
            case 2: bx = gridX + 1; break;
            case 3: bz = gridZ + 1; break;
            case 4: bz = gridZ - 1; break;
            case 5: // Up
                if (CanClimbUp(gridX, gridY, gridZ))
                    by = gridY + 1;
                else
                {
                    AddReward(invalidMovePenalty);
                    return;
                }
                break;
            case 6: // Down
                if (CanDropDown(gridX, gridY, gridZ))
                    by = gridY - 1;
                else
                {
                    AddReward(invalidMovePenalty);
                    return;
                }
                break;
        }

        bool isHorizontal = (moveAction >= 1 && moveAction <= 4);
        if (isHorizontal)
        {
            int totalX = (World.worldDimensions.x + World.extraWorldDimensions.x) * World.chunkDimensions.x;
            int totalZ = (World.worldDimensions.z + World.extraWorldDimensions.z) * World.chunkDimensions.z;

            // Bounds check
            if (bx < 0 || bx >= totalX || bz < 0 || bz >= totalZ)
            {
                AddReward(invalidMovePenalty);
                return;
            }

            int surfaceY = FindTopSurface(bx, bz);
            if (surfaceY < 0)
            {
                // means no valid block
                AddReward(invalidMovePenalty);
                return;
            }

            // water check
            MeshUtils.BlockType blockType = world.GetBlockType(bx, surfaceY, bz);
            if (blockType == MeshUtils.BlockType.WATER)
            {
                AddReward(invalidMovePenalty);
                return;
            }
            // 1) Check the block at the new surface position
            if (world.GetBlockType(bx, surfaceY, bz) == MeshUtils.BlockType.WATER)
            {
                AddReward(invalidMovePenalty);
                return;
            }

            //2) Check if the agent is submersed in water
            //    (If you ONLY want direct adjacency to penalize, not diagonals, do it like this):
            if (world.GetBlockType(bx + 1, surfaceY, bz) == MeshUtils.BlockType.WATER ||
                world.GetBlockType(bx - 1, surfaceY, bz) == MeshUtils.BlockType.WATER ||
                world.GetBlockType(bx, surfaceY, bz + 1) == MeshUtils.BlockType.WATER ||
                world.GetBlockType(bx, surfaceY, bz - 1) == MeshUtils.BlockType.WATER||
                world.GetBlockType(bx + 1, surfaceY+1, bz) == MeshUtils.BlockType.WATER ||
                world.GetBlockType(bx - 1, surfaceY +1, bz) == MeshUtils.BlockType.WATER ||
                world.GetBlockType(bx, surfaceY +1, bz + 1) == MeshUtils.BlockType.WATER ||
                world.GetBlockType(bx, surfaceY +1, bz - 1) == MeshUtils.BlockType.WATER||
                world.GetBlockType(bx, surfaceY + 1, bz) == MeshUtils.BlockType.WATER)
            {
                AddReward(invalidMovePenalty);
                return;
            }

            // If you need to also penalize any diagonal water, you can add checks for:
            // (bx ±1, surfaceY, bz ±1), etc.

            // -------------------------------------------------------------------------
            // If we passed all these checks, we allow the movement.
            // -------------------------------------------------------------------------
            // Also check height difference logic, etc. as in your original code:
            if (surfaceY > gridY + 1)
            {
                AddReward(invalidMovePenalty);
                return;
            }

            // height difference check
            if (surfaceY > gridY + 1)
            {
                AddReward(invalidMovePenalty);
                return;
            }

            by = surfaceY;
        }

        // valid move
        gridX = bx;
        gridY = by;
        gridZ = bz;

        // Actually position agent
        SetAgentAboveBlock(gridX, gridY, gridZ);

        Vector3Int newPos = new Vector3Int(gridX, gridY, gridZ);
        if (!visitedPositions.Contains(newPos))
        {
            visitedPositions.Add(newPos);
            AddReward(exploreReward);
        }
    }

    private bool CanClimbUp(int x, int y, int z)
    {
        int maxY = (World.worldDimensions.y + World.extraWorldDimensions.y) * World.chunkDimensions.y - 1;
        if (y >= maxY) return false;

        MeshUtils.BlockType above = world.GetBlockType(x, y + 1, z);
        MeshUtils.BlockType headSpace = MeshUtils.BlockType.AIR;
        if (y + 2 <= maxY)
            headSpace = world.GetBlockType(x, y + 2, z);

        if (above != MeshUtils.BlockType.AIR && headSpace == MeshUtils.BlockType.AIR)
            return true;
        return false;
    }

    private bool CanDropDown(int x, int y, int z)
    {
        if (y <= 0) return false;

        MeshUtils.BlockType below = world.GetBlockType(x, y - 1, z);
        if (below != MeshUtils.BlockType.AIR) return false;

        MeshUtils.BlockType floor = MeshUtils.BlockType.AIR;
        if (y - 2 >= 0)
            floor = world.GetBlockType(x, y - 2, z);

        if (floor != MeshUtils.BlockType.AIR) return true;
        return false;
    }

    private void HandlePlanting(int plantAction)
    {
        if (plantAction == 1)
        {
            Vector2Int currentPos2D = new Vector2Int(gridX, gridZ);
            float locScore = ComputeLocationScore(gridX, gridY, gridZ);

            if (plantedLocations.Contains(currentPos2D))
            {
                AddReward(-0.1f);
                return;
            }

            if (locScore > scoreThreshold)
            {
                plantedLocations.Add(currentPos2D);
                totalSeedsPlanted++;
                float plantingReward = locScore / scoreThreshold;
                AddReward(plantingReward);

                Vector3 blockCenter = new Vector3(gridX + 0.5f, gridY + 0.25f, gridZ + 0.5f);

                GameObject newSeed = Instantiate(seedPrefab, blockCenter, Quaternion.identity);
                newSeed.GetComponent<TreeGrow>().growthFactor = localScore;

                // Get the Renderer component
                Renderer seedRenderer = newSeed.GetComponent<Renderer>();

                // Choose one of the three materials randomly
                int index = Random.Range(0, possibleMaterials.Length);
                seedRenderer.material = possibleMaterials[index];

                world.PlantSeedAt(currentPos2D);
            }
            else
            {
                AddReward(badPlantPenalty);
            }
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        discrete[0] = 0;
        discrete[1] = 0;

        if (Input.GetKey(KeyCode.LeftArrow)) discrete[0] = 1;
        if (Input.GetKey(KeyCode.RightArrow)) discrete[0] = 2;
        if (Input.GetKey(KeyCode.UpArrow)) discrete[0] = 3;
        if (Input.GetKey(KeyCode.DownArrow)) discrete[0] = 4;
        if (Input.GetKey(KeyCode.Q)) discrete[0] = 5;
        if (Input.GetKey(KeyCode.E)) discrete[0] = 6;

        if (Input.GetKey(KeyCode.Space))
            discrete[1] = 1;
    }

    /// <summary>
    /// Actually place the agent physically above (bx, by, bz).
    /// We add +1 so it's on top of the block, plus 0.5 for centering.
    /// We also ensure we never set an invalid or infinite position to avoid rigidbody errors.
    /// </summary>
    private void SetAgentAboveBlock(int bx, int by, int bz)
    {
        float centerX = (bx + 0.5f) * blockScale;
        float centerY = (by + 1.0f) * blockScale;
        float centerZ = (bz + 0.5f) * blockScale;

        // Final check for NaN or Infinity
        if (float.IsNaN(centerX) || float.IsNaN(centerY) || float.IsNaN(centerZ) ||
            float.IsInfinity(centerX) || float.IsInfinity(centerY) || float.IsInfinity(centerZ))
        {
            // If we detect an invalid coordinate, log a warning and skip movement
            Debug.LogWarning($"[SetAgentAboveBlock] Invalid coords => X:{centerX}, Y:{centerY}, Z:{centerZ}. Movement skipped.");
            return;
        }

        _targetPosition = new Vector3(centerX, centerY, centerZ);
        _isMoving = true;
    }

    private int FindTopSurface(int x, int z)
    {
        int maxY = (World.worldDimensions.y + World.extraWorldDimensions.y) * World.chunkDimensions.y +1;
        for (int checkY = maxY; checkY >= 0; checkY--)
        {
            if (!world.InBounds(x, checkY, z))
                continue;

            MeshUtils.BlockType b = world.GetBlockType(x, checkY, z);
            if (b != MeshUtils.BlockType.AIR && b != MeshUtils.BlockType.WATER)
            {
                return checkY;
            }
        }
        return 0;
    }

    private float ComputeLocationScore(int x, int y, int z)
    {
         localScore = 0f;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    int nz = z + dz;
                    if (!world.InBounds(nx, ny, nz)) continue;

                    MeshUtils.BlockType bType = world.GetBlockType(nx, ny, nz);
                    localScore += BlockScoring.GetBlockScore(bType);
                }
            }
        }

        // plus the block at (x,y,z)
        MeshUtils.BlockType below = world.GetBlockType(x, y, z);
        localScore += BlockScoring.GetBlockScore(below);

        // bonus for sunlight
        if (world.IsLit(x, y + 1, z))
            localScore += 2f;

        return localScore;
    }

}
