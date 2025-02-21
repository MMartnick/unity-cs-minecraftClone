using UnityEngine;

/// <summary>
/// A minimal "shallow-water-like" solver that simulates water flow on a 2D grid.
/// Renders water height to a texture for quick debugging.
/// </summary>
public class SimpleShallowWater2D : MonoBehaviour
{
    [Header("Grid Settings")]
    public int gridSize = 64;
    [Tooltip("How quickly water flows between cells.")]
    public float flowRate = 0.1f;

    [Header("Water Initialization")]
    [Tooltip("Initial water height added to the center region.")]
    public float initialCenterWater = 1.0f;

    [Header("Display")]
    [Tooltip("Material that will display the water texture.")]
    public Material waterMaterial;

    // The array storing water height at each grid cell
    private float[,] waterHeight;

    // A helper array to accumulate flow changes during each update step
    private float[,] deltaWater;

    // A texture to visualize water height
    private Texture2D waterTexture;

    private void Start()
    {
        // Initialize arrays
        waterHeight = new float[gridSize, gridSize];
        deltaWater = new float[gridSize, gridSize];

        // Optionally, initialize a "puddle" in the center
        int center = gridSize / 2;
        for (int x = center - 5; x <= center + 5; x++)
        {
            for (int y = center - 5; y <= center + 5; y++)
            {
                if (x >= 0 && x < gridSize && y >= 0 && y < gridSize)
                    waterHeight[x, y] = initialCenterWater;
            }
        }

        // Create texture for debugging
        waterTexture = new Texture2D(gridSize, gridSize, TextureFormat.RFloat, false);
        waterTexture.wrapMode = TextureWrapMode.Clamp;

        // Assign the texture to the material if provided
        if (waterMaterial != null)
        {
            waterMaterial.mainTexture = waterTexture;
        }
    }

    private void Update()
    {
        // 1. Update water flow
        UpdateWaterFlow();

        // 2. Update the water debug texture (optional)
        UpdateWaterTexture();
    }

    /// <summary>
    /// Naive water-flow update:
    /// Each cell tries to share water with its 4 neighbors based on height differences.
    /// </summary>
    private void UpdateWaterFlow()
    {
        // Clear the deltaWater array each frame
        System.Array.Clear(deltaWater, 0, deltaWater.Length);

        // For each cell, compute outflow to neighbors
        for (int x = 1; x < gridSize - 1; x++)
        {
            for (int y = 1; y < gridSize - 1; y++)
            {
                float currentWater = waterHeight[x, y];
                if (currentWater <= 0f)
                    continue; // no water to flow out

                // Compare with 4 neighbors (up, down, left, right)
                // Flow is proportional to difference in water height
                // (In real shallow-water equations, you'd also account for terrain height and velocities,
                //  but this is a simplified approach.)
                ShareWater(x, y, x + 1, y); // right neighbor
                ShareWater(x, y, x - 1, y); // left neighbor
                ShareWater(x, y, x, y + 1); // up neighbor
                ShareWater(x, y, x, y - 1); // down neighbor
            }
        }

        // Apply accumulated flow changes
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                waterHeight[x, y] += deltaWater[x, y];
                // Clamp to avoid negative water
                if (waterHeight[x, y] < 0f)
                    waterHeight[x, y] = 0f;
            }
        }
    }

    /// <summary>
    /// Shares water between (x1, y1) and (x2, y2) based on height difference.
    /// </summary>
    private void ShareWater(int x1, int y1, int x2, int y2)
    {
        float h1 = waterHeight[x1, y1];
        float h2 = waterHeight[x2, y2];
        float diff = h1 - h2;

        // If diff > 0, water can flow from cell1 to cell2
        if (diff > 0f)
        {
            // Amount of water to transfer is scaled by flowRate and the difference
            float flowAmount = diff * flowRate * Time.deltaTime;

            // If cell1 doesn't have enough water, clamp flow
            if (flowAmount > h1)
                flowAmount = h1;

            deltaWater[x1, y1] -= flowAmount;
            deltaWater[x2, y2] += flowAmount;
        }
    }

    /// <summary>
    /// Updates a debug texture to visualize water heights.
    /// </summary>
    private void UpdateWaterTexture()
    {
        // For each cell, map waterHeight to a grayscale color
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                float h = waterHeight[x, y];
                // Simple mapping: higher water => brighter color
                // You might want to adjust scaling or color ramp
                float shade = Mathf.Clamp01(h / 2f);
                waterTexture.SetPixel(x, y, new Color(shade, shade, shade, 1f));
            }
        }

        waterTexture.Apply();
    }
}
