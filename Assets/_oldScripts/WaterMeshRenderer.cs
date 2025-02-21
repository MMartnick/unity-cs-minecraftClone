// 2/17/2025 AI-Tag
// This was created with assistance from Muse, a Unity Artificial Intelligence product

using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterMeshRenderer : MonoBehaviour
{
    public ShallowWaterOnTerrain waterSim; // Assign in Inspector

    [Tooltip("Recalculate mesh normals each frame for smoother shading. Can be expensive.")]
    public bool recalcNormals = true;

    private MeshFilter meshFilter;
    private Mesh mesh;

    private Vector3[] vertices;
    private int[] triangles;

    // We'll copy these from waterSim once we know they're valid
    private int width;
    private int height;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();

        if (waterSim == null)
        {
            Debug.LogError($"{name}: No reference to ShallowWaterOnTerrain! Please assign in Inspector.");
            return;
        }

        // Get the simulation dimensions
        width = waterSim.Width;
        height = waterSim.Height;

        // NEW: Check if the simulation is valid
        if (width < 2 || height < 2)
        {
            Debug.LogError($"{name}: Invalid waterSim resolution ({width}x{height}). Must be at least 2x2.");
            return;
        }

        // Build a new mesh to represent water surface
        mesh = new Mesh
        {
            name = "WaterMesh"
        };

        BuildMeshGeometry();
    }

    void LateUpdate()
    {
        if (waterSim == null || mesh == null) return;

        // Check again if the sim changed or is invalid
        if (waterSim.Width < 2 || waterSim.Height < 2)
        {
            // We won't rebuild the mesh here, but we do a safety return
            Debug.LogWarning($"{name}: WaterSim dimension changed or invalid. Skipping mesh update.");
            return;
        }

        UpdateMeshVertices();
        mesh.vertices = vertices;

        if (recalcNormals)
            mesh.RecalculateNormals();

        meshFilter.sharedMesh = mesh;
    }

    /// <summary>
    /// Builds the initial mesh (width * height vertices, with (width-1)*(height-1)*2 triangles).
    /// </summary>
    private void BuildMeshGeometry()
    {
        vertices = new Vector3[width * height];
        triangles = new int[(width - 1) * (height - 1) * 6];

        int vIndex = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Initialize each vertex at (x, 0, y). We'll set Y-value later.
                vertices[vIndex] = new Vector3(x, 0f, y);
                vIndex++;
            }
        }

        int tIndex = 0;
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                int bottomLeft = y * width + x;
                int bottomRight = y * width + x + 1;
                int topLeft = (y + 1) * width + x;
                int topRight = (y + 1) * width + x + 1;

                // Triangle 1
                triangles[tIndex++] = bottomLeft;
                triangles[tIndex++] = topLeft;
                triangles[tIndex++] = bottomRight;

                // Triangle 2
                triangles[tIndex++] = bottomRight;
                triangles[tIndex++] = topLeft;
                triangles[tIndex++] = topRight;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        meshFilter.sharedMesh = mesh;
    }

    /// <summary>
    /// Updates each vertex.y to match terrainHeight + waterHeight from the simulation.
    /// </summary>
    private void UpdateMeshVertices()
    {
        float[,] terrainH = waterSim.TerrainHeights;
        float[,] waterH = waterSim.WaterHeight;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;

                Vector3 v = vertices[index];
                // Keep x,z the same, update y
                v.y = terrainH[x, y] + waterH[x, y];
                vertices[index] = v;
            }
        }
    }
}
