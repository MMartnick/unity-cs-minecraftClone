using UnityEngine;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering;

/// <summary>
/// Replaces the old block-based chunk with a 
/// voxel-based, Marching Cubes chunk.
/// </summary>
public class VChunk : MonoBehaviour
{
    public Material atlas;

    public int width = 10;
    public int height = 10;
    public int depth = 10;

    public Vector3 location;

    // Instead of chunkData[] and Block[,,], 
    // we store a 3D array of VoxelData:
    private VoxelData[,,] voxelMap;

    public MeshRenderer meshRenderer;

    /// <summary>
    /// Called by World to initialize chunk size/position.
    /// </summary>
    public void CreateChunk(Vector3 dimension, Vector3 position)
    {
        location = position;
        width = (int)dimension.x;
        height = (int)dimension.y;
        depth = (int)dimension.z;

        // Add mesh components if not present
        MeshFilter mf = gameObject.AddComponent<MeshFilter>();
        MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
        meshRenderer = mr;
        mr.material = atlas;

        // Optional: Add a MeshCollider for collisions
        gameObject.AddComponent<MeshCollider>();

        // Allocate our voxelMap
        voxelMap = new VoxelData[width, height, depth];

        // 1) Fill in voxelMap with density + blockType
        GenerateVoxelData();

        // 2) Build a smooth mesh via Marching Cubes
        BuildMarchingCubesMesh();
    }

    /// <summary>
    /// Generate the density & block type for each voxel 
    /// using your existing Perlin noise logic.
    /// </summary>
    private void GenerateVoxelData()
    {
        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Convert local chunk coords to world coords
                    int worldX = x + (int)location.x;
                    int worldY = y + (int)location.y;
                    int worldZ = z + (int)location.z;

                    // Sample your existing noise functions
                    float surfaceHeight = MeshUtils.fBM(worldX, worldZ,
                        World.surfaceSettings.octaves,
                        World.surfaceSettings.scale,
                        World.surfaceSettings.heightScale,
                        World.surfaceSettings.heightOffset);

                    float stoneHeight = MeshUtils.fBM(worldX, worldZ,
                        World.stoneSettings.octaves,
                        World.stoneSettings.scale,
                        World.stoneSettings.heightScale,
                        World.stoneSettings.heightOffset);

                    float diamondTHeight = MeshUtils.fBM(worldX, worldZ,
                        World.diamondTSettings.octaves,
                        World.diamondTSettings.scale,
                        World.diamondTSettings.heightScale,
                        World.diamondTSettings.heightOffset);

                    float diamondBHeight = MeshUtils.fBM(worldX, worldZ,
                        World.diamondBSettings.octaves,
                        World.diamondBSettings.scale,
                        World.diamondBSettings.heightScale,
                        World.diamondBSettings.heightOffset);

                    float caveNoise = MeshUtils.fBM3D(worldX, worldY, worldZ,
                        World.caveSettings.octaves,
                        World.caveSettings.scale,
                        World.caveSettings.heightScale,
                        World.caveSettings.heightOffset);

                    // We'll compute a "base" density: 
                    //  y < surfaceHeight => likely solid
                    //  if caveNoise < probability => carve out air
                    float density = surfaceHeight - worldY;

                    // Carve out caves if caveNoise < some threshold
                    if (caveNoise < World.caveSettings.probability)
                    {
                        // Force it to be air
                        density = -1f;
                    }

                    // For bedrock at y=0, let's force it to be solid
                    if (worldY == 0)
                    {
                        density = 1f;
                    }

                    // Then decide a block type for texturing
                    MeshUtils.BlockType bType = MeshUtils.BlockType.AIR;
                    if (density > 0f)
                    {
                        // Solid: figure out which type
                        if (worldY == 0)
                        {
                            bType = MeshUtils.BlockType.BEDROCK;
                        }
                        else if (Mathf.RoundToInt(surfaceHeight) == worldY)
                        {
                            bType = MeshUtils.BlockType.GRASSSIDE;
                        }
                        else if (worldY < stoneHeight &&
                                 UnityEngine.Random.value < World.stoneSettings.probability)
                        {
                            bType = MeshUtils.BlockType.STONE;
                        }
                        else if (worldY < diamondTHeight &&
                                 worldY > diamondBHeight &&
                                 UnityEngine.Random.value < World.stoneSettings.probability)
                        {
                            bType = MeshUtils.BlockType.DIAMOND;
                        }
                        else if (worldY < surfaceHeight)
                        {
                            bType = MeshUtils.BlockType.DIRT;
                        }
                    }
                    else
                    {
                        bType = MeshUtils.BlockType.AIR;
                    }

                    voxelMap[x, y, z] = new VoxelData(density, bType);
                }
            }
        }
    }

    /// <summary>
    /// Actually run the Marching Cubes algorithm to generate a smooth mesh 
    /// from voxelMap, then assign it to MeshFilter & MeshCollider.
    /// </summary>
    private void BuildMarchingCubesMesh()
    {
        // Use a helper class to do the isosurface extraction
        MarchingCubesBuilder builder = new MarchingCubesBuilder(
            voxelMap,
            width, height, depth,
            location
        );

        Mesh smoothMesh = builder.BuildSmoothMesh();

        // Assign to our MeshFilter & MeshCollider
        MeshFilter mf = GetComponent<MeshFilter>();
        mf.mesh = smoothMesh;

        MeshCollider mc = GetComponent<MeshCollider>();
        mc.sharedMesh = smoothMesh;
    }
}
