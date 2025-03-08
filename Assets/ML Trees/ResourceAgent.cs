using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

// If MeshUtils is in a separate namespace, ensure it's accessible
using static MeshUtils;

public class ResourceAgent : Agent
{
    [Header("World Reference")]
    public World world;

    [Header("Agent Grid Position")]
    public int gridX;
    public int gridY;
    public int gridZ;

    [Tooltip("If each block is 1 unit in world-space, set blockScale=1.")]
    public float blockScale = 1f;

    [Tooltip("Seconds between moves (for real-time). For ML-Agents training, set small or remove.")]
    public float moveInterval = 1f;
    private float nextMoveTime = 0f;

    [Tooltip("How quickly to move toward the next position (for smooth movement).")]
    public float moveSpeed = 5f;

    private Vector3 _targetPosition;
    private bool _isMoving = false;

    /// <summary>
    /// Called once when the agent is initialized.
    /// </summary>
    public override void Initialize()
    {
        // If out of bounds, clamp or set a default valid location
        if (!world.InBounds(gridX, gridY, gridZ))
        {
            gridX = 25;
            gridY = 25;
            gridZ = 25;
        }

        // Start at the correct position immediately
        SetTargetWorldPosition(gridX, gridY, gridZ);
        transform.position = _targetPosition;
    }

    private void Update()
    {
        // Smoothly move toward the target position each frame
        if (_isMoving)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                _targetPosition,
                moveSpeed * Time.deltaTime
            );

            // Check if we've arrived
            if (Vector3.Distance(transform.position, _targetPosition) < 0.001f)
            {
                transform.position = _targetPosition;
                _isMoving = false;
            }
        }
    }

    /// <summary>
    /// Here you collect observations that the ML policy can use
    /// to decide how to move.
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        // 1) The block at the agent's position
        BlockType currentBlock = world.GetBlockType(gridX, gridY, gridZ);
        sensor.AddObservation((int)currentBlock);

        // 2) Is this position lit?
        bool isLit = world.IsLit(gridX, gridY, gridZ);
        sensor.AddObservation(isLit ? 1 : 0);

        // 3) The agent's Y-level (could help it learn altitude)
        sensor.AddObservation(gridY);

        // Add more observations if needed...
    }

    /// <summary>
    /// This is called each decision step.
    /// The agent's discrete action decides how to move.
    /// </summary>
    public override void OnActionReceived(ActionBuffers actions)
    {
        // Small negative reward each step to encourage movement or exploration
        AddReward(-0.001f);

        // If using real-time gating, ensure enough time has passed for the next move
        if (Time.time < nextMoveTime)
            return;

        // *** Use the ML-Agents action to decide direction ***
        // 0=stay, 1=left, 2=right, 3=front, 4=back, 5=up, 6=down
        int move = actions.DiscreteActions[0];

        int nx = gridX;
        int ny = gridY;
        int nz = gridZ;

        switch (move)
        {
            case 1: nx -= 1; break; // left
            case 2: nx += 1; break; // right
            case 3: nz += 1; break; // forward
            case 4: nz -= 1; break; // backward
            case 5: ny += 1; break; // up
            case 6: ny -= 1; break; // down
                                    // case 0 => no movement
        }

        // Attempt to move to the chosen position
        AttemptMove(nx, ny, nz);

        // Example: Add a reward for the new location’s "environment score" 
        // (optional - your design choice)
        float locationScore = ComputeLocationScore(gridX, gridY, gridZ);
        AddReward(locationScore);

        // Possibly debug-log the agent’s position, reward, etc.
        // Debug.Log($"Agent at ({gridX},{gridY},{gridZ}) with reward {GetCumulativeReward()}");

        // Set the next time the agent can move (for real-time gating)
        nextMoveTime = Time.time + moveInterval;
    }

    /// <summary>
    /// Heuristic function for manual testing (e.g. arrow keys).
    /// </summary>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        discrete[0] = 0; // default stay

        if (Input.GetKey(KeyCode.LeftArrow)) discrete[0] = 1;
        if (Input.GetKey(KeyCode.RightArrow)) discrete[0] = 2;
        if (Input.GetKey(KeyCode.UpArrow)) discrete[0] = 3;
        if (Input.GetKey(KeyCode.DownArrow)) discrete[0] = 4;
        if (Input.GetKey(KeyCode.Q)) discrete[0] = 5; // up
        if (Input.GetKey(KeyCode.E)) discrete[0] = 6; // down
    }

    /// <summary>
    /// Handles attempting to move the agent to (nx, ny, nz),
    /// checking for bounds or water, etc.
    /// </summary>
    private void AttemptMove(int nx, int ny, int nz)
    {
        // Out of world bounds => negative reward and skip
        if (!world.InBounds(nx, ny, nz))
        {
            AddReward(-0.1f);
            return;
        }

        // Can't stand on WATER => negative reward
        BlockType targetBlock = world.GetBlockType(nx, ny, nz);
        if (targetBlock == BlockType.WATER)
        {
            AddReward(-0.2f);
            return;
        }

        // Valid space => update our grid position
        gridX = nx;
        gridY = ny;
        gridZ = nz;

        // Now smoothly move toward that new location
        SetTargetWorldPosition(gridX, gridY, gridZ);
    }

    /// <summary>
    /// Sets the agent's _targetPosition in world space,
    /// and triggers smooth movement in Update().
    /// </summary>
    private void SetTargetWorldPosition(int x, int y, int z)
    {
        _targetPosition = new Vector3(x * blockScale, y * blockScale, z * blockScale);
        _isMoving = true;
    }

    /// <summary>
    /// A sample "location score" function that sums up
    /// the block scoring in a 3x3x3 region around (x,y,z),
    /// plus a bonus if lit.
    /// </summary>
    private float ComputeLocationScore(int x, int y, int z)
    {
        float totalScore = 0f;

        // Check the 3x3x3 region around (x, y, z)
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    // skip the agent's current position
                    if (dx == 0 && dy == 0 && dz == 0)
                        continue;

                    // skip directly above the agent if (dx==0, dy==1, dz==0)
                    if (dx == 0 && dy == 1 && dz == 0)
                        continue;

                    int nx = x + dx;
                    int ny = y + dy;
                    int nz = z + dz;

                    BlockType bType = world.GetBlockType(nx, ny, nz);
                    totalScore += BlockScoring.GetBlockScore(bType);
                }
            }
        }

        // Include the agent's own block
        BlockType center = world.GetBlockType(x, y, z);
        totalScore += BlockScoring.GetBlockScore(center);

        // Bonus if lit
        if (world.IsLit(x, y, z))
        {
            totalScore += 2f;
        }

        return totalScore;
    }
}
