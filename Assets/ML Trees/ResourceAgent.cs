using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using static MeshUtils;

public class ResourceAgent : Agent
{
    [Header("World Reference")]
    public World world;

    // The agent’s position in block coordinates,
    // but note that we interpret (gridX, gridY, gridZ) 
    // as the block "underneath" the agent,
    // and physically the agent is in the air cell just above it.
    private int gridX, gridY, gridZ;

    [Tooltip("If each block is 1 meter, set blockScale=1.")]
    public float blockScale = 1f;

    [Tooltip("Seconds between moves; reduce for faster training.")]
    public float moveInterval = 1f;
    private float nextMoveTime = 0f;

    [Tooltip("Agent moves smoothly to the target position in Update().")]
    public float moveSpeed = 5f;
    private Vector3 _targetPosition;
    private bool _isMoving = false;

    private int stepsSinceLastMove = 0;
    private (int x, int y, int z) lastPos;

    public override void Initialize()
    {
        RandomizeSpawn();
        // Place agent physically above the block at (gridX, gridY, gridZ).
        // The agent is in the air cell => "ny+1" in local coords
        SetAgentAboveBlock(gridX, gridY, gridZ);
        transform.position = _targetPosition;

        lastPos = (gridX, gridY, gridZ);
        stepsSinceLastMove = 0;
    }

    private void RandomizeSpawn()
    {
        // measure total block extents
        int totalX = (World.worldDimensions.x + World.extraWorldDimensions.x)
                     * World.chunkDimensions.x;
        int totalZ = (World.worldDimensions.z + World.extraWorldDimensions.z)
                     * World.chunkDimensions.z;

        // pick random x,z, then pick y=some ground or 0
        gridX = Random.Range(0, totalX / 2);
        gridZ = Random.Range(0, totalZ / 2);
        gridY = 25; // or find actual ground

        // ensure in bounds
        if (!world.InBounds(gridX, gridY, gridZ))
        {
            gridX = 25;
            gridY = 25;
            gridZ = 25;
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
        // The block "under" the agent is at (gridX, gridY, gridZ).
        // The agent physically is in the cell above it => (gridY+1) in world coords 
        // but we store "below block coords" in gridY.

        BlockType belowBlock = world.GetBlockType(gridX, gridY, gridZ);
        sensor.AddObservation((int)belowBlock);

        // Light check in the cell the agent physically occupies => i.e. above the block
        // but we can approximate with "below" or do "y+1"
        bool isLit = world.IsLit(gridX, gridY + 1, gridZ);
        sensor.AddObservation(isLit ? 1 : 0);

        sensor.AddObservation(gridY);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // small step cost
        AddReward(-0.005f);

        if (Time.time < nextMoveTime) return;

        int move = actions.DiscreteActions[0];

        // penalty if agent chooses "stay" = 0
        if (move == 0)
        {
            AddReward(-0.01f);
        }

        // interpret the 6 possible moves => left,right,forward,back,up,down
        // Actually we want to choose an "adjacent block" (nx, ny, nz),
        // Then stand in the air cell above that block => (nx, ny+1, nz).
        int bx = gridX;
        int by = gridY;
        int bz = gridZ;

        switch (move)
        {
            case 1: bx -= 1; break; // left
            case 2: bx += 1; break; // right
            case 3: bz += 1; break; // forward
            case 4: bz -= 1; break; // backward
            case 5: by += 1; break; // up
            case 6: by -= 1; break; // down
        }

        // Attempt to stand "above" the block (bx, by, bz).
        AttemptMoveAboveBlock(bx, by, bz);

        float locationScore = ComputeLocationScore(gridX, gridY, gridZ);
        AddReward(locationScore);

        // stuck check
        if ((gridX, gridY, gridZ) == lastPos)
        {
            stepsSinceLastMove++;
            if (stepsSinceLastMove > 30)
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

        nextMoveTime = Time.time + moveInterval;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        discrete[0] = 0;
        if (Input.GetKey(KeyCode.LeftArrow)) discrete[0] = 1;
        if (Input.GetKey(KeyCode.RightArrow)) discrete[0] = 2;
        if (Input.GetKey(KeyCode.UpArrow)) discrete[0] = 3;
        if (Input.GetKey(KeyCode.DownArrow)) discrete[0] = 4;
        if (Input.GetKey(KeyCode.Q)) discrete[0] = 5;
        if (Input.GetKey(KeyCode.E)) discrete[0] = 6;
    }

    /// <summary>
    /// The agent wants to stand "in the air cell above" the block at (bx,by,bz).
    /// So we physically set agent coords => (bx, by+1, bz), if valid/in-bounds.
    /// If that block is water => penalty or skip. If out of bounds => skip. 
    /// </summary>
    private void AttemptMoveAboveBlock(int bx, int by, int bz)
    {
        // first check if (bx,by,bz) is in bounds
        if (!world.InBounds(bx, by, bz))
        {
            AddReward(-0.1f);
            return;
        }

        BlockType belowBlock = world.GetBlockType(bx, by, bz);

        // if it's water => penalty, but let agent stand in the air above it anyway, if we want
        // or skip if you want no water moves
        if (belowBlock == BlockType.WATER)
        {
            AddReward(-2f);
            // we can keep going or skip
        }

        // The cell the agent physically occupies => (bx, by+1, bz)
        int topY = by + 1;
        if (!world.InBounds(bx, topY, bz))
        {
            AddReward(-0.1f);
            return;
        }

        // finalize agent's new "below block coords"
        gridX = bx;
        gridY = by;
        gridZ = bz;

        // place agent physically at (bx, by+1, bz)
        Debug.Log($"Agent stands above block=({bx},{by},{bz}) => agentCoord=({bx},{by},{bz}) belowBlock={belowBlock}");
        SetAgentAboveBlock(bx, by, bz);
    }

    /// <summary>
    /// Sets the agent to the center of the cell "one above" the block at (bx,by,bz).
    /// That is physically (bx+0.5, (by+1)+0.5, bz+0.5).
    /// </summary>
    private void SetAgentAboveBlock(int bx, int by, int bz)
    {
        float centerX = (bx + 0.5f) * blockScale;
        float centerY = ((by + 1) + 0.5f) * blockScale;
        float centerZ = (bz + 0.5f) * blockScale;

        _targetPosition = new Vector3(centerX, centerY, centerZ);
        _isMoving = true;
    }

    private float ComputeLocationScore(int x, int y, int z)
    {
        // The agent is physically "above" the block (x,y,z). 
        // For adjacency scoring, we can consider the 3x3x3 around the block below us,
        // or the actual cell we occupy => up to you.

        float totalScore = 0f;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    if (dx == 0 && dy == 1 && dz == 0) continue;

                    int xx = x + dx;
                    int yy = y + dy;
                    int zz = z + dz;

                    BlockType bType = world.GetBlockType(xx, yy, zz);
                    totalScore += BlockScoring.GetBlockScore(bType);
                }
            }
        }

        // plus the block "under" us
        totalScore += BlockScoring.GetBlockScore(world.GetBlockType(x, y, z));

        // +2 if lit => either check "above" cell or "below" cell
        // Let's check above cell: (x,y+1,z)
       /* if (world.IsLit(x, y + 1, z))
        {
            totalScore += 2f;
        }*/

        return totalScore;
    }
}
