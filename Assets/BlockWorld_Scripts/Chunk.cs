using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Concurrent;

public class Chunk : MonoBehaviour
{
    [Header("Materials")]
    public Material atlas;         // Opaque terrain material
    public Material waterMaterial; // Transparent or water shader

    [Header("Chunk Dimensions")]
    public int width = 10;
    public int height = 10;
    public int depth = 10;

    public Vector3 location;

    // The flattened chunk data
    public MeshUtils.BlockType[] chunkData;
    public MeshUtils.BlockType[] healthData;

    public Block[,,] blocks;
    public MeshRenderer meshRenderer;

    // Corner cache for smoothing
    private Dictionary<(int x, int y, int z, int cornerIndex), Vector3> cornerCache
        = new Dictionary<(int, int, int, int), Vector3>();

    CalculateBlockTypes calculateBlockTypes;
    JobHandle jobHandle;
    public NativeArray<Unity.Mathematics.Random> RandomArray { get; private set; }

    ////////////////////////////////////////////////////////////////////////////////
    // 1) GENERATE BLOCK TYPES IN PARALLEL
    ////////////////////////////////////////////////////////////////////////////////
    [BurstCompile]
    struct CalculateBlockTypes : IJobParallelFor
    {
        public NativeArray<MeshUtils.BlockType> cData;
        public NativeArray<MeshUtils.BlockType> hData;
        public int width;
        public int height;
        public Vector3 location;
        public NativeArray<Unity.Mathematics.Random> randoms;

        public void Execute(int i)
        {
            int x = i % width + (int)location.x;
            int y = (i / width) % height + (int)location.y;
            int z = i / (width * height) + (int)location.z;

            var random = randoms[i];

            // Some perlin-based heights
            int surfaceHeight = (int)MeshUtils.fBM(
                x, z,
                World.surfaceSettings.octaves,
                World.surfaceSettings.scale,
                World.surfaceSettings.heightScale,
                World.surfaceSettings.heightOffset
            );

            int sandHeight = (int)MeshUtils.fBM(
                x, z,
                World.sandSettings.octaves,
                World.sandSettings.scale,
                World.sandSettings.heightScale,
                World.sandSettings.heightOffset
            );

            int stoneHeight = (int)MeshUtils.fBM(
                x, z,
                World.stoneSettings.octaves,
                World.stoneSettings.scale,
                World.stoneSettings.heightScale,
                World.stoneSettings.heightOffset
            );

            int diamondTHeight = (int)MeshUtils.fBM(
                x, z,
                World.diamondTSettings.octaves,
                World.diamondTSettings.scale,
                World.diamondTSettings.heightScale,
                World.diamondTSettings.heightOffset
            );

            int diamondBHeight = (int)MeshUtils.fBM(
                x, z,
                World.diamondBSettings.octaves,
                World.diamondBSettings.scale,
                World.diamondBSettings.heightScale,
                World.diamondBSettings.heightOffset
            );

            int digCave = (int)MeshUtils.fBM3D(
                x, y, z,
                World.caveSettings.octaves,
                World.caveSettings.scale,
                World.caveSettings.heightScale,
                World.caveSettings.heightOffset
            );

            // Initialize health/cracks
            hData[i] = MeshUtils.BlockType.NOCRACK;

            // Bedrock at y=0
            if (y == 0)
            {
                cData[i] = MeshUtils.BlockType.BEDROCK;
                return;
            }

            // Cave carve-out
            if (digCave < World.caveSettings.probability)
            {
                cData[i] = MeshUtils.BlockType.AIR;
                return;
            }

            // Grass top
            if (surfaceHeight == y)
            {
                cData[i] = MeshUtils.BlockType.GRASSSIDE; // or GRASSTOP if you prefer
            }
            else if (y < diamondTHeight && y > diamondBHeight &&
                     random.NextFloat(1) <= World.diamondTSettings.probability)
            {
                cData[i] = MeshUtils.BlockType.DIAMOND;
            }
            else if (y < stoneHeight && random.NextFloat(1) <= World.stoneSettings.probability)
            {
                cData[i] = MeshUtils.BlockType.STONE;
            }
            else if (y < sandHeight && random.NextFloat(1) <= World.sandSettings.probability)
            {
                cData[i] = MeshUtils.BlockType.SAND;
            }
            else if (y < surfaceHeight)
            {
                cData[i] = MeshUtils.BlockType.DIRT;
            }
            else if (y < 20)
            {
                cData[i] = MeshUtils.BlockType.WATER;
            }
            else
            {
                cData[i] = MeshUtils.BlockType.AIR;
            }
        }
    }

    private void BuildChunk()
    {
        int blockCount = width * depth * height;
        chunkData = new MeshUtils.BlockType[blockCount];
        healthData = new MeshUtils.BlockType[blockCount];

        NativeArray<MeshUtils.BlockType> blockTypes = new NativeArray<MeshUtils.BlockType>(chunkData, Allocator.Persistent);
        NativeArray<MeshUtils.BlockType> healthTypes = new NativeArray<MeshUtils.BlockType>(healthData, Allocator.Persistent);

        var randomArray = new Unity.Mathematics.Random[blockCount];
        var seed = new System.Random();

        for (int i = 0; i < blockCount; ++i)
            randomArray[i] = new Unity.Mathematics.Random((uint)seed.Next());

        RandomArray = new NativeArray<Unity.Mathematics.Random>(randomArray, Allocator.Persistent);

        calculateBlockTypes = new CalculateBlockTypes()
        {
            cData = blockTypes,
            hData = healthTypes,
            width = width,
            height = height,
            location = location,
            randoms = RandomArray
        };

        jobHandle = calculateBlockTypes.Schedule(blockCount, 64);
        jobHandle.Complete();

        calculateBlockTypes.cData.CopyTo(chunkData);
        calculateBlockTypes.hData.CopyTo(healthData);

        blockTypes.Dispose();
        healthTypes.Dispose();
        RandomArray.Dispose();
    }

    ////////////////////////////////////////////////////////////////////////////////
    // 2) CREATE THE CHUNK IN MULTIPLE PASSES
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// The order in which we want to build each block type,
    /// so each pass conforms to the corners from the previous passes.
    /// 
    ///  - pass #1: Stone-like blocks (Stone, Bedrock, Diamond)
    ///  - pass #2: Dirt
    ///  - pass #3: Sand
    ///  - pass #4: GrassSide
    ///  - pass #5: GrassTop
    ///  - pass #6: Water
    /// </summary>
    private static readonly MeshUtils.BlockType[][] Passes = new MeshUtils.BlockType[][]
    {
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.BEDROCK, MeshUtils.BlockType.STONE, MeshUtils.BlockType.DIAMOND },
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.DIRT },
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.SAND },
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.GRASSSIDE },
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.GRASSTOP }, // if you have a distinct GRASSTOP
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.WATER }
    };

    public void CreateChunk(Vector3 dimensions, Vector3 position, bool rebuildBlocks = true)
    {
        location = position;
        width = (int)dimensions.x;
        height = (int)dimensions.y;
        depth = (int)dimensions.z;

        // Clear the corner cache each time we rebuild
        cornerCache.Clear();

        MeshFilter mf = this.gameObject.AddComponent<MeshFilter>();
        MeshRenderer mr = this.gameObject.AddComponent<MeshRenderer>();
        meshRenderer = mr;
        mr.material = atlas;
        blocks = new Block[width, height, depth];

        if (rebuildBlocks)
            BuildChunk();

        // We'll store combined meshes for "solid" blocks and for "water" blocks separately
        var solidMeshes = new List<Mesh>();
        var waterMeshes = new List<Mesh>();

        // We do multiple passes in a specific order
        for (int passIndex = 0; passIndex < Passes.Length; passIndex++)
        {
            MeshUtils.BlockType[] passTypes = Passes[passIndex];
            bool isWaterPass = (passTypes.Length == 1 && passTypes[0] == MeshUtils.BlockType.WATER);

            // Gather block meshes for this pass
            var passMeshes = BuildBlockMeshesForTypes(passTypes);

            // Add them to either solid list or water list
            if (!isWaterPass)
            {
                solidMeshes.AddRange(passMeshes);
            }
            else
            {
                waterMeshes.AddRange(passMeshes);
            }
        }

        // Combine SOLID passes
        Mesh terrainMesh = CombineMeshes(solidMeshes,
            "Terrain_" + location.x + "_" + location.y + "_" + location.z);
        mf.mesh = terrainMesh;

        MeshCollider collider = this.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mf.mesh;

        // Combine WATER pass (if any)
        if (waterMeshes.Count > 0)
        {
            Mesh waterMesh = CombineMeshes(waterMeshes,
                "Water_" + location.x + "_" + location.y + "_" + location.z);

            // Create child object for water
            GameObject waterGO = new GameObject("WaterGO");
            waterGO.transform.SetParent(this.transform, false);

            MeshFilter wmf = waterGO.AddComponent<MeshFilter>();
            MeshRenderer wmr = waterGO.AddComponent<MeshRenderer>();

            wmf.mesh = waterMesh;
            wmr.material = (waterMaterial != null) ? waterMaterial : atlas;
        }
    }

    /// <summary>
    /// Loop over all blocks, and if the block's type is in `typesWanted`,
    /// create the block, build its mesh, and store it in a temporary list.
    /// Return all resulting meshes for that pass.
    /// </summary>
    private List<Mesh> BuildBlockMeshesForTypes(MeshUtils.BlockType[] typesWanted)
    {
        var passMeshes = new List<Mesh>();

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var index = x + width * (y + depth * z);
                    var bType = chunkData[index];
                    if (!IsBlockInList(bType, typesWanted))
                        continue; // skip if block not in this pass

                    // Create the block => automatically calls smoothing => builds a mesh
                    blocks[x, y, z] = new Block(
                        new Vector3(x, y, z) + location,
                        bType,
                        this,
                        healthData[index]
                    );

                    // If there's a valid mesh, add to passMeshes
                    if (blocks[x, y, z].mesh != null)
                    {
                        passMeshes.Add(blocks[x, y, z].mesh);
                    }
                }
            }
        }
        return passMeshes;
    }

    private bool IsBlockInList(MeshUtils.BlockType blockType, MeshUtils.BlockType[] listTypes)
    {
        for (int i = 0; i < listTypes.Length; i++)
        {
            if (blockType == listTypes[i]) return true;
        }
        return false;
    }

    /// <summary>
    /// Uses your existing job-based approach to combine a list of individual block meshes
    /// into a single combined Mesh. Returns that merged Mesh.
    /// </summary>
    private Mesh CombineMeshes(List<Mesh> inputMeshes, string newMeshName)
    {
        if (inputMeshes.Count == 0)
        {
            var empty = new Mesh { name = newMeshName };
            return empty;
        }

        int vertexStart = 0;
        int triStart = 0;
        int meshCount = inputMeshes.Count;

        var jobs = new ProcessMeshDataJob
        {
            vertexStart = new NativeArray<int>(meshCount, Allocator.TempJob),
            triStart = new NativeArray<int>(meshCount, Allocator.TempJob)
        };

        // Calculate total vertex/index counts
        int m = 0;
        foreach (var mesh in inputMeshes)
        {
            int vCount = mesh.vertexCount;
            int iCount = (int)mesh.GetIndexCount(0);

            jobs.vertexStart[m] = vertexStart;
            jobs.triStart[m] = triStart;

            vertexStart += vCount;
            triStart += iCount;
            m++;
        }

        jobs.meshData = Mesh.AcquireReadOnlyMeshData(inputMeshes);

        // Create a single output mesh data
        var outputMeshData = Mesh.AllocateWritableMeshData(1);
        jobs.outputMesh = outputMeshData[0];

        // Setup the vertex/index buffer sizes
        jobs.outputMesh.SetIndexBufferParams(triStart, IndexFormat.UInt32);
        jobs.outputMesh.SetVertexBufferParams(vertexStart,
            new VertexAttributeDescriptor(VertexAttribute.Position),
            new VertexAttributeDescriptor(VertexAttribute.Normal, stream: 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, stream: 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, stream: 3)
        );

        var handle = jobs.Schedule(meshCount, 4);

        var combined = new Mesh { name = newMeshName };
        var subMesh = new SubMeshDescriptor(0, triStart, MeshTopology.Triangles)
        {
            firstVertex = 0,
            vertexCount = vertexStart
        };

        handle.Complete();

        jobs.outputMesh.subMeshCount = 1;
        jobs.outputMesh.SetSubMesh(0, subMesh);
        Mesh.ApplyAndDisposeWritableMeshData(outputMeshData, new[] { combined });

        jobs.meshData.Dispose();
        jobs.vertexStart.Dispose();
        jobs.triStart.Dispose();

        combined.RecalculateBounds();
        return combined;
    }

    [BurstCompile]
    struct ProcessMeshDataJob : IJobParallelFor
    {
        [ReadOnly] public Mesh.MeshDataArray meshData;
        public Mesh.MeshData outputMesh;
        public NativeArray<int> vertexStart;
        public NativeArray<int> triStart;

        public void Execute(int index)
        {
            var data = meshData[index];
            int vCount = data.vertexCount;
            int vStart = vertexStart[index];

            // Read source mesh data
            var verts = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var normals = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var uvs = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var uvs2 = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            data.GetVertices(verts.Reinterpret<Vector3>());
            data.GetNormals(normals.Reinterpret<Vector3>());
            data.GetUVs(0, uvs.Reinterpret<Vector3>());
            data.GetUVs(1, uvs2.Reinterpret<Vector3>());

            var outputVerts = outputMesh.GetVertexData<Vector3>(0);
            var outputNormals = outputMesh.GetVertexData<Vector3>(1);
            var outputUVs = outputMesh.GetVertexData<Vector3>(2);
            var outputUVs2 = outputMesh.GetVertexData<Vector3>(3);

            for (int i = 0; i < vCount; i++)
            {
                outputVerts[vStart + i] = verts[i];
                outputNormals[vStart + i] = normals[i];
                outputUVs[vStart + i] = uvs[i];
                outputUVs2[vStart + i] = uvs2[i];
            }

            verts.Dispose();
            normals.Dispose();
            uvs.Dispose();
            uvs2.Dispose();

            int tStart = triStart[index];
            int tCount = data.GetSubMesh(0).indexCount;
            var outputTris = outputMesh.GetIndexData<int>();

            if (data.indexFormat == IndexFormat.UInt16)
            {
                var tris = data.GetIndexData<ushort>();
                for (int i = 0; i < tCount; i++)
                {
                    outputTris[tStart + i] = vStart + tris[i];
                }
            }
            else
            {
                var tris = data.GetIndexData<int>();
                for (int i = 0; i < tCount; i++)
                {
                    outputTris[tStart + i] = vStart + tris[i];
                }
            }
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    // 3) CORNER SMOOTHING, NO RANDOM SHIFT, WATER NUDGE ONLY
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// We compute the corner with smoothing if needed. Solids have NO random shift,
    /// so each block lines up seamlessly. Water is nudged down a bit (-0.01f) to avoid
    /// z-fighting.
    /// </summary>
    public Vector3 GetOrComputeCorner(int bx, int by, int bz,
                                      int cornerIndex,
                                      MeshUtils.BlockType blockType)
    {
        var key = (bx, by, bz, cornerIndex);

        if (cornerCache.TryGetValue(key, out Vector3 cachedPos))
        {
            return cachedPos;
        }

        // If not found, compute the smoothed corner
        Vector3 blockOrigin = new Vector3(bx + location.x, by + location.y, bz + location.z);
        Vector3 cornerPos = ComputeCornerPosition(blockOrigin, blockType, bx, by, bz, cornerIndex);

        // Only water gets a small downward offset (for z-fight safety).
        if (blockType == MeshUtils.BlockType.WATER)
        {
            cornerPos.y -= 0.05f;
        }
        if (blockType == MeshUtils.BlockType.STONE)
        {
            cornerPos.y -= 0.01f;
            cornerPos.x += 0.05f;
            cornerPos.z += 0.05f;
        }
        if (blockType == MeshUtils.BlockType.DIRT)
        {
            cornerPos.y -= 0.03f;
            cornerPos.x += 0.04f;
            cornerPos.z += 0.04f;
        }
        if (blockType == MeshUtils.BlockType.GRASSSIDE)
        {
            cornerPos.y -= 0.02f;
            cornerPos.x += 0.03f;
            cornerPos.z += 0.03f;
        }
        if (blockType == MeshUtils.BlockType.GRASSTOP)
        {
            cornerPos.y -= 0.02f;
            cornerPos.x += 0.02f;
            cornerPos.z += 0.02f;
        }
        if (blockType == MeshUtils.BlockType.SAND)
        {
            cornerPos.y -= 0.04f;
        }

        cornerCache[key] = cornerPos;
        return cornerPos;
    }

    private Vector3 ComputeCornerPosition(Vector3 blockOrigin,
                                          MeshUtils.BlockType blockType,
                                          int bx, int by, int bz,
                                          int cornerIndex)
    {
        // Start with the base corner
        Vector3 baseCorner = GetBaseCorner(blockOrigin, cornerIndex);

        // Gather open directions
        List<Vector3> shifts = new List<Vector3>();

        var axisOffsets = GetAxisOffsets(bx, by, bz, cornerIndex);
        var diagOffsets = GetPlaneDiagonalOffset(bx, by, bz, cornerIndex);

        // With cornerShift=0, the block corners remain exactly in place
        // unless it's water offset. That yields a truly "seamless" terrain.
        float cornerShift = -0.75f;
        float diagScale = 0.25f; // not used if cornerShift=0, but left for clarity

        // Axis neighbors
        foreach (var (nOffset, shiftDir) in axisOffsets)
        {
            if (IsNeighborOpen(blockType, nOffset.x, nOffset.y, nOffset.z))
            {
                // If water => skip vertical shift
                // But cornerShift=0 means there's no shift anyway, so it won't matter
                shifts.Add(shiftDir * cornerShift);
            }
        }

        // Diagonal neighbor
        (Vector3Int dOff, Vector3 diagVec) = diagOffsets;
        if (IsNeighborOpen(blockType, dOff.x, dOff.y, dOff.z))
        {
            Vector3 scaledDiag = diagVec * cornerShift * diagScale;
            shifts.Add(scaledDiag);
        }

        if (shifts.Count == 0)
            return baseCorner;

        // Average all shift vectors
        Vector3 sum = Vector3.zero;
        foreach (var s in shifts) sum += s;
        Vector3 avg = sum / shifts.Count;

        return baseCorner + avg;
    }

    private Vector3 GetBaseCorner(Vector3 blockOrigin, int cornerIndex)
    {
        switch (cornerIndex)
        {
            case 0: return blockOrigin + new Vector3(-0.5f, -0.5f, 0.5f);
            case 1: return blockOrigin + new Vector3(0.5f, -0.5f, 0.5f);
            case 2: return blockOrigin + new Vector3(0.5f, -0.5f, -0.5f);
            case 3: return blockOrigin + new Vector3(-0.5f, -0.5f, -0.5f);
            case 4: return blockOrigin + new Vector3(-0.5f, 0.5f, 0.5f);
            case 5: return blockOrigin + new Vector3(0.5f, 0.5f, 0.5f);
            case 6: return blockOrigin + new Vector3(0.5f, 0.5f, -0.5f);
            case 7: return blockOrigin + new Vector3(-0.5f, 0.5f, -0.5f);
        }
        return blockOrigin;
    }

    private (Vector3Int offset, Vector3 shiftDir)[] GetAxisOffsets(int bx, int by, int bz, int cornerIndex)
    {
        switch (cornerIndex)
        {
            case 0: // bottom-left-front
                return new (Vector3Int, Vector3)[]
                {
                    (new Vector3Int(bx-1, by,   bz),   new Vector3(+1, 0, 0)),
                    (new Vector3Int(bx,   by-1, bz),   new Vector3(0, +1, 0)),
                    (new Vector3Int(bx,   by,   bz+1), new Vector3(0, 0, -1))
                };
            case 1: // bottom-right-front
                return new (Vector3Int, Vector3)[]
                {
                    (new Vector3Int(bx+1, by,   bz),   new Vector3(-1, 0, 0)),
                    (new Vector3Int(bx,   by-1, bz),   new Vector3(0, +1, 0)),
                    (new Vector3Int(bx,   by,   bz+1), new Vector3(0, 0, -1))
                };
            case 2: // bottom-right-back
                return new (Vector3Int, Vector3)[]
                {
                    (new Vector3Int(bx+1, by,   bz),   new Vector3(-1, 0, 0)),
                    (new Vector3Int(bx,   by-1, bz),   new Vector3(0, +1, 0)),
                    (new Vector3Int(bx,   by,   bz-1), new Vector3(0, 0, +1))
                };
            case 3: // bottom-left-back
                return new (Vector3Int, Vector3)[]
                {
                    (new Vector3Int(bx-1, by,   bz),   new Vector3(+1, 0, 0)),
                    (new Vector3Int(bx,   by-1, bz),   new Vector3(0, +1, 0)),
                    (new Vector3Int(bx,   by,   bz-1), new Vector3(0, 0, +1))
                };
            case 4: // top-left-front
                return new (Vector3Int, Vector3)[]
                {
                    (new Vector3Int(bx-1, by,   bz),   new Vector3(+1, 0, 0)),
                    (new Vector3Int(bx,   by+1, bz),   new Vector3(0, -1, 0)),
                    (new Vector3Int(bx,   by,   bz+1), new Vector3(0, 0, -1))
                };
            case 5: // top-right-front
                return new (Vector3Int, Vector3)[]
                {
                    (new Vector3Int(bx+1, by,   bz),   new Vector3(-1, 0, 0)),
                    (new Vector3Int(bx,   by+1, bz),   new Vector3(0, -1, 0)),
                    (new Vector3Int(bx,   by,   bz+1), new Vector3(0, 0, -1))
                };
            case 6: // top-right-back
                return new (Vector3Int, Vector3)[]
                {
                    (new Vector3Int(bx+1, by,   bz),   new Vector3(-1, 0, 0)),
                    (new Vector3Int(bx,   by+1, bz),   new Vector3(0, -1, 0)),
                    (new Vector3Int(bx,   by,   bz-1), new Vector3(0, 0, +1))
                };
            case 7: // top-left-back
                return new (Vector3Int, Vector3)[]
                {
                    (new Vector3Int(bx-1, by,   bz),   new Vector3(+1, 0, 0)),
                    (new Vector3Int(bx,   by+1, bz),   new Vector3(0, -1, 0)),
                    (new Vector3Int(bx,   by,   bz-1), new Vector3(0, 0, +1))
                };
        }
        return new (Vector3Int, Vector3)[0];
    }

    private (Vector3Int, Vector3) GetPlaneDiagonalOffset(int bx, int by, int bz, int cornerIndex)
    {
        switch (cornerIndex)
        {
            case 0: return (new Vector3Int(bx - 1, by, bz + 1), new Vector3(+1, 0, -1));
            case 1: return (new Vector3Int(bx + 1, by, bz + 1), new Vector3(-1, 0, -1));
            case 2: return (new Vector3Int(bx + 1, by, bz - 1), new Vector3(-1, 0, +1));
            case 3: return (new Vector3Int(bx - 1, by, bz - 1), new Vector3(+1, 0, +1));
            case 4: return (new Vector3Int(bx - 1, by, bz + 1), new Vector3(+1, 0, -1));
            case 5: return (new Vector3Int(bx + 1, by, bz + 1), new Vector3(-1, 0, -1));
            case 6: return (new Vector3Int(bx + 1, by, bz - 1), new Vector3(-1, 0, +1));
            case 7: return (new Vector3Int(bx - 1, by, bz - 1), new Vector3(+1, 0, +1));
        }
        return (new Vector3Int(bx, by, bz), Vector3.zero);
    }

    /// <summary>
    /// "Open" means we should shift the corner in that direction if cornerShift != 0.
    /// With cornerShift=0, solids won't shift anyway, but water uses a separate offset.
    /// - If water, open if neighbor != WATER
    /// - If a solid, open if neighbor != the same blockType
    /// </summary>
    private bool IsNeighborOpen(MeshUtils.BlockType myType, int nx, int ny, int nz)
    {
        if (nx < 0 || nx >= width ||
            ny < 0 || ny >= height ||
            nz < 0 || nz >= depth)
        {
            return false;
        }

        var neighborType = chunkData[nx + width * (ny + depth * nz)];

        if (myType == MeshUtils.BlockType.WATER)
        {
            return (neighborType != MeshUtils.BlockType.WATER);
        }
        else
        {
            // No random offset, but we keep the logic
            // in case cornerShift were > 0 or < 0 in the future
            return (neighborType != myType);
        }
    }
}
