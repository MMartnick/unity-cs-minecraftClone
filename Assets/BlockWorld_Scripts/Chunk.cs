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
    public Material atlas;

    public int width = 10;
    public int height = 10;
    public int depth = 10;

    public Vector3 location;

    public Block[,,] blocks;
    //Flat[x + WIDTH * (y + DEPTH * z)] = Original[x, y, z]
    //x = i % WIDTH
    //y = (i / WIDTH) % HEIGHT
    //z = i / (WIDTH * HEIGHT )
    public MeshUtils.BlockType[] chunkData;
    public MeshUtils.BlockType[] healthData;
    public MeshRenderer meshRenderer;

    private Dictionary<(int, int, int, int), Vector3> cornerCache =
    new Dictionary<(int, int, int, int), Vector3>();

    CalculateBlockTypes calculateBlockTypes;
    JobHandle jobHandle;
    public NativeArray<Unity.Mathematics.Random> RandomArray { get; private set; }

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

            int surfaceHeight = (int)MeshUtils.fBM(x, z, World.surfaceSettings.octaves,
                                                   World.surfaceSettings.scale, World.surfaceSettings.heightScale,
                                                   World.surfaceSettings.heightOffset);

            int sandHeight = (int)MeshUtils.fBM(x, z, World.sandSettings.octaves,
                                       World.sandSettings.scale, World.sandSettings.heightScale,
                                       World.sandSettings.heightOffset);

            int stoneHeight = (int)MeshUtils.fBM(x, z, World.stoneSettings.octaves,
                                                   World.stoneSettings.scale, World.stoneSettings.heightScale,
                                                   World.stoneSettings.heightOffset);

            int diamondTHeight = (int)MeshUtils.fBM(x, z, World.diamondTSettings.octaves,
                                       World.diamondTSettings.scale, World.diamondTSettings.heightScale,
                                       World.diamondTSettings.heightOffset);

            int diamondBHeight = (int)MeshUtils.fBM(x, z, World.diamondBSettings.octaves,
                           World.diamondBSettings.scale, World.diamondBSettings.heightScale,
                           World.diamondBSettings.heightOffset);

            int digCave = (int)MeshUtils.fBM3D(x, y, z, World.caveSettings.octaves,
                           World.caveSettings.scale, World.caveSettings.heightScale,
                           World.caveSettings.heightOffset);

            //int plantTree = (int)MeshUtils.fBM3D(x, y, z, World.treeSettings.octaves,
              // World.treeSettings.scale, World.treeSettings.heightScale,
             //  World.treeSettings.heightOffset);

            hData[i] = MeshUtils.BlockType.NOCRACK;

            if (y == 0)
            {
                cData[i] = MeshUtils.BlockType.BEDROCK;
                return;
            }

            if (digCave < World.caveSettings.probability)
            {
                cData[i] = MeshUtils.BlockType.AIR;
                return;
            }

            if (surfaceHeight == y)
            {
                //if (plantTree < World.treeSettings.probability && random.NextFloat(1) <= 0.1)
                //{
                   // cData[i] = MeshUtils.BlockType.WOODBASE;
                //}
                //else
                    cData[i] = MeshUtils.BlockType.GRASSSIDE;
            }
            else if (y < diamondTHeight && y > diamondBHeight && random.NextFloat(1) <= World.diamondTSettings.probability)
                cData[i] = MeshUtils.BlockType.DIAMOND;
            else if (y < stoneHeight && random.NextFloat(1) <= World.stoneSettings.probability)
                cData[i] = MeshUtils.BlockType.STONE;
            else if (y < sandHeight && random.NextFloat(1) <= World.sandSettings.probability)
                cData[i] = MeshUtils.BlockType.SAND;
            else if (y < surfaceHeight)
                cData[i] = MeshUtils.BlockType.DIRT;
            else if (y < 20)
            {
                cData[i] = MeshUtils.BlockType.WATER;
            }
            else
                cData[i] = MeshUtils.BlockType.AIR;
        }
    }

    void BuildChunk()
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

        jobHandle = calculateBlockTypes.Schedule(chunkData.Length, 64);
        jobHandle.Complete();
        calculateBlockTypes.cData.CopyTo(chunkData);
        calculateBlockTypes.hData.CopyTo(healthData);
        blockTypes.Dispose();
        healthTypes.Dispose();
        RandomArray.Dispose();
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    public void CreateChunk(Vector3 dimensions, Vector3 position, bool rebuildBlocks = true)
    {
        location = position;
        width = (int)dimensions.x;
        height = (int)dimensions.y;
        depth = (int)dimensions.z;

        MeshFilter mf = this.gameObject.AddComponent<MeshFilter>();
        MeshRenderer mr = this.gameObject.AddComponent<MeshRenderer>();
        meshRenderer = mr;
        mr.material = atlas;
        blocks = new Block[width, height, depth];
        if (rebuildBlocks)
            BuildChunk();

        var inputMeshes = new List<Mesh>();
        int vertexStart = 0;
        int triStart = 0;
        int meshCount = width * height * depth;
        int m = 0;
        var jobs = new ProcessMeshDataJob();
        jobs.vertexStart = new NativeArray<int>(meshCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        jobs.triStart = new NativeArray<int>(meshCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);


        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    blocks[x, y, z] = new Block(new Vector3(x, y, z) + location,
                    chunkData[x + width * (y + depth * z)], this,
                    healthData[x + width * (y + depth * z)]);
                    if (blocks[x, y, z].mesh != null)
                    {
                        inputMeshes.Add(blocks[x, y, z].mesh);
                        var vcount = blocks[x, y, z].mesh.vertexCount;
                        var icount = (int)blocks[x, y, z].mesh.GetIndexCount(0);
                        jobs.vertexStart[m] = vertexStart;
                        jobs.triStart[m] = triStart;
                        vertexStart += vcount;
                        triStart += icount;
                        m++;
                    }
                }
            }
        }

        jobs.meshData = Mesh.AcquireReadOnlyMeshData(inputMeshes);
        var outputMeshData = Mesh.AllocateWritableMeshData(1);
        jobs.outputMesh = outputMeshData[0];
        jobs.outputMesh.SetIndexBufferParams(triStart, IndexFormat.UInt32);
        jobs.outputMesh.SetVertexBufferParams(vertexStart,
            new VertexAttributeDescriptor(VertexAttribute.Position),
            new VertexAttributeDescriptor(VertexAttribute.Normal, stream: 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, stream: 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, stream: 3));

        var handle = jobs.Schedule(inputMeshes.Count, 4);
        var newMesh = new Mesh();
        newMesh.name = "Chunk_" + location.x + "_" + location.y + "_" + location.z;
        var sm = new SubMeshDescriptor(0, triStart, MeshTopology.Triangles);
        sm.firstVertex = 0;
        sm.vertexCount = vertexStart;

        handle.Complete();

        jobs.outputMesh.subMeshCount = 1;
        jobs.outputMesh.SetSubMesh(0, sm);
        Mesh.ApplyAndDisposeWritableMeshData(outputMeshData, new[] { newMesh });
        jobs.meshData.Dispose();
        jobs.vertexStart.Dispose();
        jobs.triStart.Dispose();
        newMesh.RecalculateBounds();

        mf.mesh = newMesh;
        MeshCollider collider = this.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mf.mesh;
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
            var vCount = data.vertexCount;
            var vStart = vertexStart[index];

            var verts = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            data.GetVertices(verts.Reinterpret<Vector3>());

            var normals = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            data.GetNormals(normals.Reinterpret<Vector3>());

            var uvs = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            data.GetUVs(0, uvs.Reinterpret<Vector3>());

            var uvs2 = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            data.GetUVs(1, uvs2.Reinterpret<Vector3>());

            var outputVerts = outputMesh.GetVertexData<Vector3>();
            var outputNormals = outputMesh.GetVertexData<Vector3>(stream: 1);
            var outputUVs = outputMesh.GetVertexData<Vector3>(stream: 2);
            var outputUVs2 = outputMesh.GetVertexData<Vector3>(stream: 3);

            for (int i = 0; i < vCount; i++)
            {
                outputVerts[i + vStart] = verts[i];
                outputNormals[i + vStart] = normals[i];
                outputUVs[i + vStart] = uvs[i];
                outputUVs2[i + vStart] = uvs2[i];
            }

            verts.Dispose();
            normals.Dispose();
            uvs.Dispose();
            uvs2.Dispose();

            var tStart = triStart[index];
            var tCount = data.GetSubMesh(0).indexCount;
            var outputTris = outputMesh.GetIndexData<int>();
            if (data.indexFormat == IndexFormat.UInt16)
            {
                var tris = data.GetIndexData<ushort>();
                for (int i = 0; i < tCount; ++i)
                {
                    int idx = tris[i];
                    outputTris[i + tStart] = vStart + idx;
                }
            }
            else
            {
                var tris = data.GetIndexData<int>();
                for (int i = 0; i < tCount; ++i)
                {
                    int idx = tris[i];
                    outputTris[i + tStart] = vStart + idx;
                }
            }

        }
    }

    /// <summary>
    /// Returns the final (smoothed) corner position for (bx,by,bz, cornerIndex)
    /// If not in cornerCache, compute it with smoothing logic, store, then return.
    /// This ensures consistent corner positions across adjacent blocks.
    /// </summary>
    public Vector3 GetOrComputeCorner(int bx, int by, int bz,
                                      int cornerIndex,
                                      MeshUtils.BlockType blockType)
    {
        var key = (bx, by, bz, cornerIndex);
        if (cornerCache.TryGetValue(key, out Vector3 cachedPos))
        {
            // Already computed
            return cachedPos;
        }

        // If not found, compute it:
        Vector3 blockOrigin = new Vector3(bx + location.x,
                                         by + location.y,
                                         bz + location.z);
        Vector3 cornerPos = ComputeCornerPosition(blockOrigin,
                                                  blockType,
                                                  bx, by, bz,
                                                  cornerIndex);

        // Store in dictionary
        cornerCache[key] = cornerPos;
        return cornerPos;
    }

    /// <summary>
    /// The actual smoothing logic (with partial diagonal weighting).
    /// cornerIndex indicates which of the 8 corners (0..7).
    /// We'll do the same axis + planeDiag approach, but only for
    /// a single corner.
    /// </summary>
    private Vector3 ComputeCornerPosition(Vector3 blockOrigin,
                                          MeshUtils.BlockType blockType,
                                          int bx, int by, int bz,
                                          int cornerIndex)
    {
        // 8 base corners
        Vector3 baseCorner = GetBaseCorner(blockOrigin, cornerIndex);

        // We'll gather open directions in a list, average them once
        List<Vector3> shifts = new List<Vector3>();

        // Axis neighbors for this corner:
        var axisOffsets = GetAxisOffsets(bx, by, bz, cornerIndex);

        // Diagonal neighbors for this corner:
        var diagOffsets = GetPlaneDiagonalOffset(bx, by, bz, cornerIndex);

        // Check each offset => if open, add shift
        // We'll scale diagonals to avoid over-folding
        float cornerShift = -2f;    // or -4f if you prefer
        float diagScale = 0.6f;   // partial weighting to reduce over-fold

        foreach (var (nOffset, shiftDir) in axisOffsets)
        {
            if (IsNeighborOpen(blockType, nOffset.x, nOffset.y, nOffset.z))
            {
                // If water => skip Y shift
                if (blockType == MeshUtils.BlockType.WATER &&
                    Mathf.Abs(shiftDir.y) > 0.001f)
                {
                    continue;
                }
                shifts.Add(shiftDir * cornerShift);
            }
        }

        // Check diagonal
        (Vector3Int dOff, Vector3 diagVec) = diagOffsets;
        if (IsNeighborOpen(blockType, dOff.x, dOff.y, dOff.z))
        {
            // Scale the diagonal shift to avoid big folds
            Vector3 scaledDiag = diagVec * cornerShift * diagScale;

            if (blockType == MeshUtils.BlockType.WATER &&
                Mathf.Abs(scaledDiag.y) > 0.001f)
            {
                // skip vertical for water if any
            }
            else
            {
                shifts.Add(scaledDiag);
            }
        }

        if (shifts.Count == 0)
            return baseCorner;

        // average them
        Vector3 sum = Vector3.zero;
        foreach (var s in shifts) sum += s;
        Vector3 avg = sum / shifts.Count;

        return baseCorner + avg;
    }

    /// <summary>
    /// Return the base corner position in world space 
    /// for cornerIndex [0..7].
    /// </summary>
    private Vector3 GetBaseCorner(Vector3 blockOrigin, int cornerIndex)
    {
        // local offsets for corners 0..7
        // same as your existing logic, but for a single corner
        switch (cornerIndex)
        {
            case 0: return new Vector3(-0.5f, -0.5f, 0.5f) + blockOrigin;
            case 1: return new Vector3(0.5f, -0.5f, 0.5f) + blockOrigin;
            case 2: return new Vector3(0.5f, -0.5f, -0.5f) + blockOrigin;
            case 3: return new Vector3(-0.5f, -0.5f, -0.5f) + blockOrigin;
            case 4: return new Vector3(-0.5f, 0.5f, 0.5f) + blockOrigin;
            case 5: return new Vector3(0.5f, 0.5f, 0.5f) + blockOrigin;
            case 6: return new Vector3(0.5f, 0.5f, -0.5f) + blockOrigin;
            case 7: return new Vector3(-0.5f, 0.5f, -0.5f) + blockOrigin;
        }
        return blockOrigin; // fallback
    }

    /// <summary>
    /// Return the axis neighbor offsets for that corner.
    /// Each corner has up to 3 axis neighbors: ±x, ±y, ±z.
    /// But we store them as (nOffset, shiftDir=1).
    /// We'll multiply shiftDir by cornerShift in code.
    /// </summary>
    private (Vector3Int offset, Vector3 shiftDir)[] GetAxisOffsets(int bx, int by, int bz, int cornerIndex)
    {
        // We'll define them per corner:
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

    /// <summary>
    /// Return a single plane diagonal offset for each corner in XZ-plane.
    /// We'll scale it in code. 
    /// </summary>
    private (Vector3Int, Vector3) GetPlaneDiagonalOffset(int bx, int by, int bz, int cornerIndex)
    {
        // We'll define it similarly to your previous approach, 
        // but only one diagonal offset per corner. 
        // For instance:
        switch (cornerIndex)
        {
            case 0: // bottom-left-front => x-1,z+1 => shift +X, -Z
                return (new Vector3Int(bx - 1, by, bz + 1), new Vector3(+1, 0, -1));
            case 1: // bottom-right-front => x+1,z+1 => shift -X, -Z
                return (new Vector3Int(bx + 1, by, bz + 1), new Vector3(-1, 0, -1));
            case 2: // bottom-right-back => x+1,z-1 => shift -X, +Z
                return (new Vector3Int(bx + 1, by, bz - 1), new Vector3(-1, 0, +1));
            case 3: // bottom-left-back => x-1,z-1 => shift +X, +Z
                return (new Vector3Int(bx - 1, by, bz - 1), new Vector3(+1, 0, +1));
            case 4: // top-left-front => same as corner0
                return (new Vector3Int(bx - 1, by, bz + 1), new Vector3(+1, 0, -1));
            case 5: // top-right-front => same as corner1
                return (new Vector3Int(bx + 1, by, bz + 1), new Vector3(-1, 0, -1));
            case 6: // top-right-back => same as corner2
                return (new Vector3Int(bx + 1, by, bz - 1), new Vector3(-1, 0, +1));
            case 7: // top-left-back => same as corner3
                return (new Vector3Int(bx - 1, by, bz - 1), new Vector3(+1, 0, +1));
        }
        return (new Vector3Int(bx, by, bz), Vector3.zero);
    }

    /// <summary>
    /// If block is WATER => open if neighbor != WATER
    /// else => open if neighbor == AIR
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
            return (neighborType == MeshUtils.BlockType.AIR);
        }
    }
}
