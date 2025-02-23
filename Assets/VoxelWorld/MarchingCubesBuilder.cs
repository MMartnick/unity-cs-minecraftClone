using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Performs the core Marching Cubes isosurface extraction
/// on a chunk's voxelMap.
/// </summary>
public class MarchingCubesBuilder
{
    private VoxelData[,,] voxelMap;
    private int width, height, depth;
    private Vector3 chunkLocation;

    public MarchingCubesBuilder(VoxelData[,,] data, int w, int h, int d, Vector3 loc)
    {
        voxelMap = data;
        width = w;
        height = h;
        depth = d;
        chunkLocation = loc;
    }

    public Mesh BuildSmoothMesh()
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> indices = new List<int>();

        // Iterate each "cube" cell in our voxel map (width-1, etc., 
        // because each cell has 8 corners)
        for (int x = 0; x < width - 1; x++)
        {
            for (int y = 0; y < height - 1; y++)
            {
                for (int z = 0; z < depth - 1; z++)
                {
                    // Gather the 8 corner densities
                    float[] cornerDensities = new float[8];
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 cornerPos = new Vector3(x, y, z)
                                          + MarchingCubesTable.CornerOffsets[i];
                        int cx = (int)cornerPos.x;
                        int cy = (int)cornerPos.y;
                        int cz = (int)cornerPos.z;

                        cornerDensities[i] = voxelMap[cx, cy, cz].density;
                    }

                    // Determine which corners are "inside" vs. "outside"
                    // Let's say inside if density > 0
                    int cubeIndex = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (cornerDensities[i] > 0f)
                            cubeIndex |= 1 << i;
                    }

                    // If cube is entirely empty or entirely solid, skip
                    int edges = MarchingCubesTable.edgeTable[cubeIndex];
                    if (edges == 0)
                        continue;

                    // Calculate the edge vertices
                    Vector3[] edgeVerts = new Vector3[12];
                    for (int e = 0; e < 12; e++)
                    {
                        // If this edge is not intersected, skip
                        if ((edges & (1 << e)) == 0) continue;

                        // Determine the two corners that define this edge
                        int cA = MarchingCubesTable.EdgeToCornerIndex[e, 0];
                        int cB = MarchingCubesTable.EdgeToCornerIndex[e, 1];

                        float dA = cornerDensities[cA];
                        float dB = cornerDensities[cB];

                        // positions relative to the cell
                        Vector3 posA = MarchingCubesTable.CornerOffsets[cA];
                        Vector3 posB = MarchingCubesTable.CornerOffsets[cB];

                        // Interpolate "t" where the surface crosses zero
                        float t = (0f - dA) / (dB - dA);
                        Vector3 p = posA + t * (posB - posA);

                        // Convert cell-local coords to chunk/world coords
                        Vector3 worldPos = new Vector3(x, y, z) + p;
                        edgeVerts[e] = worldPos;
                    }

                    // Create triangles from the triTable
                    for (int i = 0; i < 16; i += 3)
                    {
                        int triIndex = MarchingCubesTable.triTable[cubeIndex, i];
                        if (triIndex == -1) break;

                        int i1 = MarchingCubesTable.triTable[cubeIndex, i + 0];
                        int i2 = MarchingCubesTable.triTable[cubeIndex, i + 1];
                        int i3 = MarchingCubesTable.triTable[cubeIndex, i + 2];

                        Vector3 v1 = edgeVerts[i1];
                        Vector3 v2 = edgeVerts[i2];
                        Vector3 v3 = edgeVerts[i3];

                        // Add these 3 vertices
                        int baseIndex = vertices.Count;
                        vertices.Add(v1);
                        vertices.Add(v2);
                        vertices.Add(v3);

                        // Calculate a flat normal via cross product
                        Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1).normalized;
                        normals.Add(normal);
                        normals.Add(normal);
                        normals.Add(normal);

                        // Indices
                        indices.Add(baseIndex + 0);
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 2);
                    }
                }
            }
        }

        // Build the mesh
        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();

        // Optionally call mesh.RecalculateNormals() if you want smooth shading
        // But we already assigned flat normals manually.

        // SHIFT to chunkLocation if you want the mesh origin at (0,0,0)
        // Usually, you do the shifting in final world transforms or 
        // you can add chunkLocation to each vertex. 
        // For now, we do no shift because we used absolute coords. 
        // If you see offset issues, consider adding chunkLocation to each vertex.

        return mesh;
    }
}
