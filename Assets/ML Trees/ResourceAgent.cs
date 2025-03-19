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

    // static counters
    [SerializeField] private static int globalStepCount = 0;
    [SerializeField] private static int episodeCount = 0;
    [SerializeField] private static int totalSeedsPlanted = 0;

    // Lower threshold from 20 -> 15 for easier planting
    private float plantingThreshold = 15f;

    public override void Initialize()
    {
        // Optionally: randomize spawn once here
        RandomizeSpawn();
        SetAgentAboveBlock(gridX, gridY, gridZ);
        transform.position = _targetPosition;

        lastPos = (gridX, gridY, gridZ);
        stepsSinceLastMove = 0;
    }

    public override void OnEpisodeBegin()
    {
        // Re-randomize spawn each episode now
        if (world != null)
            world.ResetEnvironment();

        // Place agent at same coords
        SetAgentAboveBlock(gridX, gridY, gridZ);
        transform.position = _targetPosition;

        // Reset counters
        plantedLocations.Clear();
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

        int randX = Random.Range(0, totalX / 2);
        int randZ = Random.Range(0, totalZ / 2);

        int topY = FindTopSurface(randX, randZ);
        if (topY < 0)
        {
            // fallback
            gridX = 25;
            gridY = 25;
            gridZ = 25;
        }
        else
        {
            gridX = randX;
            gridY = topY;
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

    /// <summary>
    /// Observations about the "topmost" block at (gridX, gridY, gridZ)
    /// plus neighbors, lighting, slopes, etc.
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        MeshUtils.BlockType belowBlock = world.GetBlockType(gridX, gridY, gridZ);
        sensor.AddObservation((int)belowBlock);

        bool isLit = world.IsLit(gridX, gridY + 1, gridZ);
        sensor.AddObservation(isLit ? 1 : 0);

        // The agent's "surface" Y
        sensor.AddObservation(gridY);

        float threshold = 20f; // This is just for observations, not the planting threshold
        float upVal = world.GetValueAt(gridX, gridZ + 1);
        float downVal = world.GetValueAt(gridX, gridZ - 1);
        float leftVal = world.GetValueAt(gridX - 1, gridZ);
        float rightVal = world.GetValueAt(gridX + 1, gridZ);

        sensor.AddObservation(upVal >= threshold ? 1 : 0);
        sensor.AddObservation(downVal >= threshold ? 1 : 0);
        sensor.AddObservation(leftVal >= threshold ? 1 : 0);
        sensor.AddObservation(rightVal >= threshold ? 1 : 0);

        float currentY = transform.position.y;
        Vector3 aheadPos = transform.position + transform.forward * 1f;
        Vector3 rightPos = transform.position + transform.right * 1f;

        float aheadY = world.GetTerrainHeight(aheadPos.x, aheadPos.z);
        float rightY = world.GetTerrainHeight(rightPos.x, rightPos.z);

        sensor.AddObservation(aheadY - currentY);
        sensor.AddObservation(rightY - currentY);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int moveAction = actions.DiscreteActions[0]; // 0..6
        int plantAction = actions.DiscreteActions[1]; // 0..1

        // small time penalty
        AddReward(-0.005f);

        // If not time to move, we still allow the agent to plant
        if (Time.time < nextMoveTime)
        {
            HandlePlanting(plantAction);
            return;
        }

        // Movement
        if (moveAction == 0)
        {
            // idle
            AddReward(-0.001f);
        }

        // We'll interpret agent's requested move in XZ, ignoring Y 
        int bx = gridX, by = gridY, bz = gridZ;
        switch (moveAction)
        {
            case 1: bx -= 1; break; // left
            case 2: bx += 1; break; // right
            case 3: bz += 1; break; // forward
            case 4: bz -= 1; break; // backward
            case 5: by += 1; break; // up   (though we'll override with top surface)
            case 6: by -= 1; break; // down (though we'll override with top surface)
        }

        AttemptMoveAboveBlock(bx, bz);  // ignoring 'by'

        // Check if this new location is the best so far
        float currentValue = ComputeLocationScore(gridX, gridY, gridZ);
        if (currentValue > bestLocationValue)
        {
            bestLocationValue = currentValue;
            AddReward(0.1f);
        }

        // Now handle planting
        HandlePlanting(plantAction);

        // Extended stuck check from 30 -> 200
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

    /// <summary>
    /// The only way seeds are planted. Must have plantAction == 1 AND locationScore> plantingThreshold (15 by default).
    /// Otherwise => penalty.
    /// </summary>
    private void HandlePlanting(int plantAction)
    {
        if (plantAction == 1)
        {
            Vector2Int currentPos2D = new Vector2Int(gridX, gridZ);

            // Let's log for debugging
            float locationScore = ComputeLocationScore(gridX, gridY, gridZ);
            Debug.Log($"[PLANT ATTEMPT] Pos=({gridX},{gridZ}), Score={locationScore}");

            // Check if already planted
            if (plantedLocations.Contains(currentPos2D))
            {
                // penalty: already planted here
                AddReward(-0.1f);
                Debug.Log($"[PLANT ATTEMPT] Already planted here => penalty");
                return;
            }

            // Evaluate the location score
            if (locationScore > plantingThreshold)
            {
                // Good planting
                plantedLocations.Add(currentPos2D);
                totalSeedsPlanted++;

                float plantingReward = locationScore / plantingThreshold;
                AddReward(plantingReward);

                // Actually spawn the seed Prefab
                Vector3 blockCenter = new Vector3(gridX + 0.5f, gridY + 10f, gridZ + 0.5f);
                Instantiate(seedPrefab, blockCenter, Quaternion.identity);

                // Mark in the World, if needed
                world.PlantSeedAt(currentPos2D);
                this.enabled = false;

                Debug.Log($"[PLANT ATTEMPT] SUCCESS => Score={locationScore}, Reward={plantingReward}");
            }
            else
            {
                // Bad planting => penalty
                AddReward(-5f);
                Debug.Log($"[PLANT ATTEMPT] BAD => Score={locationScore} <= {plantingThreshold}, penalty");
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
    /// Places the agent "above" the top surface at (bx, bz).
    /// We ignore "by" from the agent's action and do a new surface lookup.
    /// </summary>
    private void AttemptMoveAboveBlock(int bx, int bz)
    {
        // 1) Find the top surface for that column
        int topY = FindTopSurface(bx, bz);
        if (topY < 0)
        {
            AddReward(-0.1f);
            return;
        }

        // 2) If that block is water => penalty
        MeshUtils.BlockType blockType = world.GetBlockType(bx, topY, bz);
        if (blockType == MeshUtils.BlockType.WATER)
        {
            AddReward(-30f);
        }

        // 3) Update agent coords
        gridX = bx;
        gridY = topY;
        gridZ = bz;

        Debug.Log($"Agent stands above block=({bx},{topY},{bz}) => blockType={blockType}");
        SetAgentAboveBlock(bx, topY, bz);
    }

    /// <summary>
    /// Finds the topmost (highest Y) solid block at (x, z).
    /// If none found, returns -1.
    /// </summary>
    private int FindTopSurface(int x, int z)
    {
        int maxY = (World.worldDimensions.y + World.extraWorldDimensions.y) * World.chunkDimensions.y - 1;

        for (int checkY = maxY; checkY >= 0; checkY--)
        {
            if (!world.InBounds(x, checkY, z))
                continue;

            MeshUtils.BlockType b = world.GetBlockType(x, checkY, z);
            if (b != MeshUtils.BlockType.AIR && b != MeshUtils.BlockType.WATER)
            {
                return checkY; // Found topmost solid
            }
        }
        return -1; // no block found
    }

    /// <summary>
    /// Actually place the agent physically above (bx, by, bz).
    /// We add +10 so it's well above, then +1 to stand in the air cell, plus 0.5 centering.
    /// </summary>
    private void SetAgentAboveBlock(int bx, int by, int bz)
    {
        float offset = 10f;
        float centerX = (bx + 0.5f) * blockScale;
        float centerY = (by + 1 + offset + 0.5f) * blockScale;
        float centerZ = (bz + 0.5f) * blockScale;

        _targetPosition = new Vector3(centerX, centerY, centerZ);
        _isMoving = true;
    }

    /// <summary>
    /// Computes a location score for the top block at (x, y, z).
    /// We sum block scores in a 3x3x3 around it, plus the block itself, plus +2 if lit.
    /// </summary>
    private float ComputeLocationScore(int x, int y, int z)
    {
        float totalScore = 0f;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    if (!world.InBounds(x + dx, y + dy, z + dz)) continue;

                    MeshUtils.BlockType bType = world.GetBlockType(x + dx, y + dy, z + dz);
                    totalScore += BlockScoring.GetBlockScore(bType);
                }
            }
        }

        // plus the block under us
        MeshUtils.BlockType below = world.GetBlockType(x, y, z);
        totalScore += BlockScoring.GetBlockScore(below);

        if (world.IsLit(x, y + 1, z))
            totalScore += 2f;

        return totalScore;
    }
}
