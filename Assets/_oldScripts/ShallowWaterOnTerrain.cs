// 2/17/2025 AI-Tag
// This was created with assistance from Muse, a Unity Artificial Intelligence product

using UnityEngine;

/// <summary>
/// Demonstrates a naive shallow-water-like simulation that references a Unity Terrain.
/// Water flows from higher (terrain + water) cells to lower ones.
/// </summary>
public class ShallowWaterOnTerrain : MonoBehaviour
{
    [Header("Terrain Reference")]
    public Terrain targetTerrain;  // Assign in Inspector
    private TerrainData terrainData;

    [Header("Simulation Settings")]
    [Tooltip("Flow rate controls how quickly water moves between cells.")]
    public float flowRate = 0.1f;

    [Tooltip("How much water to initially place on the terrain (simple uniform start).")]
    public float initialWaterAmount = 0.5f;

    // Arrays to hold the water height and the terrain height at each cell
    private float[,] waterHeight;
    private float[,] terrainHeights;

    // Temporary array for flow deltas
    private float[,] deltaWater;

    // We'll store the terrain resolution for clarity
    private int width;
    private int height;

    // Expose read-only properties so other scripts (e.g., WaterMeshRenderer) can access them safely
    public float[,] WaterHeight => waterHeight;
    public float[,] TerrainHeights => terrainHeights;
    public int Width => width;
    public int Height => height;

    void Start()
    {
        if (targetTerrain == null)
        {
            Debug.LogError("No Terrain assigned! Please assign a Terrain in the Inspector.");
            return;
        }

        terrainData = targetTerrain.terrainData;

        // Note: For many terrains, heightmapResolution might be 513, 1025, etc.
        // We store them in width & height for convenience.
        width = terrainData.heightmapResolution;
        height = terrainData.heightmapResolution;

        // Retrieve the normalized terrain heights in [0..1].
        // rawHeights is indexed as [row = y, column = x].
        float[,] rawHeights = terrainData.GetHeights(0, 0, width, height);

        // Create and initialize arrays
        terrainHeights = new float[width, height];
        waterHeight = new float[width, height];
        deltaWater = new float[width, height];

        // Convert normalized heights to actual world heights and init water.
        // We'll do a simple uniform water fill for demonstration.
        float terrainScaleY = terrainData.size.y;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Carefully note: rawHeights[y, x], i.e. y is the row, x is the column.
                terrainHeights[x, y] = rawHeights[y, x] * terrainScaleY;
                waterHeight[x, y] = initialWaterAmount;
            }
        }
    }

    void Update()
    {
        // If the terrain wasn't set or something is invalid, skip
        if (terrainData == null) return;

        // Run one step of the naive flow simulation
        UpdateWaterFlow();

        // TODO: Visualize water, e.g., via a mesh, debug texture, etc.
    }

    /// <summary>
    /// Updates water flow across the terrain grid in a naive manner.
    /// </summary>
    private void UpdateWaterFlow()
    {
        // Clear deltaWater
        System.Array.Clear(deltaWater, 0, deltaWater.Length);

        // Because we do x+1, x-1, y+1, y-1, we loop 1..width-2 to avoid out-of-bounds.
        for (int x = 1; x < width - 1; x++)
        {
            for (int y = 1; y < height - 1; y++)
            {
                float myWater = waterHeight[x, y];
                if (myWater <= 0f)
                    continue;

                // Flow to four neighbors
                ShareWater(x, y, x + 1, y);
                ShareWater(x, y, x - 1, y);
                ShareWater(x, y, x, y + 1);
                ShareWater(x, y, x, y - 1);
            }
        }

        // Apply flow changes
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                waterHeight[x, y] += deltaWater[x, y];
                if (waterHeight[x, y] < 0f)
                    waterHeight[x, y] = 0f;
            }
        }
    }

    /// <summary>
    /// Shares water from (x1,y1) to (x2,y2) if surface height is higher in (x1,y1).
    /// Includes a boundary check to avoid out-of-range indexing.
    /// </summary>
    private void ShareWater(int x1, int y1, int x2, int y2)
    {
        // Boundary check - skip if neighbor is out of range
        if (x2 < 0 || x2 >= width || y2 < 0 || y2 >= height)
            return;

        // Surface = terrain + water
        float surface1 = terrainHeights[x1, y1] + waterHeight[x1, y1];
        float surface2 = terrainHeights[x2, y2] + waterHeight[x2, y2];

        float diff = surface1 - surface2;
        if (diff > 0f)
        {
            float transfer = diff * flowRate * Time.deltaTime;
            // Clamp transfer to available water
            if (transfer > waterHeight[x1, y1])
                transfer = waterHeight[x1, y1];

            deltaWater[x1, y1] -= transfer;
            deltaWater[x2, y2] += transfer;
        }
    }
}
