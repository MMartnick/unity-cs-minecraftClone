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

    // Flattened chunk data
    public MeshUtils.BlockType[] chunkData;
    public MeshUtils.BlockType[] healthData;

    public Block[,,] blocks;
    public MeshRenderer meshRenderer;

    // Corner cache for smoothing/folding
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
                cData[i] = MeshUtils.BlockType.GRASSSIDE; // or GRASSTOP
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

        NativeArray<MeshUtils.BlockType> blockTypes
            = new NativeArray<MeshUtils.BlockType>(chunkData, Allocator.Persistent);
        NativeArray<MeshUtils.BlockType> healthTypes
            = new NativeArray<MeshUtils.BlockType>(healthData, Allocator.Persistent);

        var randArray = new Unity.Mathematics.Random[blockCount];
        var seed = new System.Random();
        for (int i = 0; i < blockCount; i++)
            randArray[i] = new Unity.Mathematics.Random((uint)seed.Next());

        RandomArray = new NativeArray<Unity.Mathematics.Random>(randArray, Allocator.Persistent);

        calculateBlockTypes = new CalculateBlockTypes
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

    private static readonly MeshUtils.BlockType[][] Passes = new MeshUtils.BlockType[][]
    {
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.BEDROCK, MeshUtils.BlockType.STONE, MeshUtils.BlockType.DIAMOND },
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.DIRT },
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.SAND },
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.GRASSSIDE },
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.GRASSTOP },
        new MeshUtils.BlockType[]{ MeshUtils.BlockType.WATER }
    };

    public void CreateChunk(Vector3 dimensions, Vector3 position, bool rebuildBlocks = true)
    {
        location = position;
        width = (int)dimensions.x;
        height = (int)dimensions.y;
        depth = (int)dimensions.z;

        cornerCache.Clear();

        MeshFilter mf = gameObject.AddComponent<MeshFilter>();
        MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
        meshRenderer = mr;
        mr.material = atlas;

        blocks = new Block[width, height, depth];

        if (rebuildBlocks)
            BuildChunk();

        var solidMeshes = new List<Mesh>();
        var waterMeshes = new List<Mesh>();

        // Build passes in order
        for (int passIndex = 0; passIndex < Passes.Length; passIndex++)
        {
            var passTypes = Passes[passIndex];
            bool isWaterPass = (passTypes.Length == 1 && passTypes[0] == MeshUtils.BlockType.WATER);

            var passMeshes = BuildBlockMeshesForTypes(passTypes);
            if (!isWaterPass)
                solidMeshes.AddRange(passMeshes);
            else
                waterMeshes.AddRange(passMeshes);
        }

        // Combine solids
        Mesh terrainMesh = CombineMeshes(solidMeshes,
            $"Terrain_{location.x}_{location.y}_{location.z}");
        mf.mesh = terrainMesh;

        MeshCollider collider = gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mf.mesh;

        // Combine water
        if (waterMeshes.Count > 0)
        {
            Mesh waterMesh = CombineMeshes(waterMeshes,
                $"Water_{location.x}_{location.y}_{location.z}");

            GameObject waterGO = new GameObject("WaterGO");
            waterGO.transform.SetParent(this.transform, false);

            MeshFilter wmf = waterGO.AddComponent<MeshFilter>();
            MeshRenderer wmr = waterGO.AddComponent<MeshRenderer>();

            wmf.mesh = waterMesh;
            wmr.material = (waterMaterial != null) ? waterMaterial : atlas;
        }
    }

    private List<Mesh> BuildBlockMeshesForTypes(MeshUtils.BlockType[] typesWanted)
    {
        var passMeshes = new List<Mesh>();

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + width * (y + depth * z);
                    var bType = chunkData[index];
                    if (!IsBlockInList(bType, typesWanted))
                        continue;

                    // Create the block => builds a mesh
                    blocks[x, y, z] = new Block(
                        new Vector3(x, y, z) + location,
                        bType,
                        this,
                        healthData[index]
                    );

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
    /// Combine a list of individual meshes into a single mesh.
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

        // Tally vertex/index counts
        for (int m = 0; m < meshCount; m++)
        {
            var mesh = inputMeshes[m];
            int vCount = mesh.vertexCount;
            int iCount = (int)mesh.GetIndexCount(0);

            jobs.vertexStart[m] = vertexStart;
            jobs.triStart[m] = triStart;

            vertexStart += vCount;
            triStart += iCount;
        }

        jobs.meshData = Mesh.AcquireReadOnlyMeshData(inputMeshes);

        var outputMeshData = Mesh.AllocateWritableMeshData(1);
        jobs.outputMesh = outputMeshData[0];

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

            // read source mesh data
            var verts = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var norms = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var uvs = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var uvs2 = new NativeArray<float3>(vCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            data.GetVertices(verts.Reinterpret<Vector3>());
            data.GetNormals(norms.Reinterpret<Vector3>());
            data.GetUVs(0, uvs.Reinterpret<Vector3>());
            data.GetUVs(1, uvs2.Reinterpret<Vector3>());

            var dstVerts = outputMesh.GetVertexData<Vector3>(0);
            var dstNorms = outputMesh.GetVertexData<Vector3>(1);
            var dstUVs = outputMesh.GetVertexData<Vector3>(2);
            var dstUVs2 = outputMesh.GetVertexData<Vector3>(3);

            for (int i = 0; i < vCount; i++)
            {
                dstVerts[vStart + i] = verts[i];
                dstNorms[vStart + i] = norms[i];
                dstUVs[vStart + i] = uvs[i];
                dstUVs2[vStart + i] = uvs2[i];
            }

            verts.Dispose();
            norms.Dispose();
            uvs.Dispose();
            uvs2.Dispose();

            int tStart = triStart[index];
            int tCount = data.GetSubMesh(0).indexCount;
            var dstTris = outputMesh.GetIndexData<int>();

            // copy indices
            if (data.indexFormat == IndexFormat.UInt16)
            {
                var sTris = data.GetIndexData<ushort>();
                for (int t = 0; t < tCount; t++)
                    dstTris[tStart + t] = vStart + sTris[t];
            }
            else
            {
                var sTris = data.GetIndexData<int>();
                for (int t = 0; t < tCount; t++)
                    dstTris[tStart + t] = vStart + sTris[t];
            }
        }
    }

    ////////////////////////////////////////////////////////////////////////////////
    // 3) CORNER FOLDING: SHIFT CORNER FOR >=2 OPEN (AIR) SIDES,
    //    AVERAGING SHIFT SO TOTAL REMAINS ~0.5
    ////////////////////////////////////////////////////////////////////////////////
    public Vector3 GetOrComputeCorner(int bx, int by, int bz,
                                      int cornerIndex,
                                      MeshUtils.BlockType blockType)
    {
        var key = (bx, by, bz, cornerIndex);
        if (cornerCache.TryGetValue(key, out Vector3 cachedPos))
        {
            return cachedPos;
        }

        Vector3 baseCorner = GetBaseCorner(new Vector3(bx, by, bz), cornerIndex);

        // If it's water => small nudge, but do not treat it as open for folding
        if (blockType == MeshUtils.BlockType.WATER)
        {
            baseCorner.y -= 0.02f;
            baseCorner.x += 0.02f;
            baseCorner.z += 0.02f;
        }

        // (Optional) Stone offset removed or commented out:
        // if (blockType == MeshUtils.BlockType.STONE)
        // {
        //     baseCorner.x += 0.05f;
        //     baseCorner.y += 0.05f;
        //     baseCorner.z += 0.05f;
        // }

        // Count how many sides are open (AIR only)
        var openSides = GetOpenSides(bx, by, bz, cornerIndex);
        // If at least 2 sides are open => fold corner
        if (openSides.Count >= 3)
        {
            // We'll sum direction vectors, then normalize so total shift is ~0.5
            Vector3 sumDir = Vector3.zero;
            foreach (var dir in openSides)
            {
                sumDir += dir;
            }

            // Avoid zero magnitude
            float mag = sumDir.magnitude;
            if (mag > 0.0001f)
            {
                // We want a total shift of 0.5 in the *combined* direction
                Vector3 fold = sumDir.normalized * 0.5f;
                baseCorner += fold;
            }
        }

        Vector3 worldCorner = baseCorner + location;
        cornerCache[key] = worldCorner;
        return worldCorner;
    }

    /// <summary>
    /// Return the local corner position w/o folding
    /// </summary>
    private Vector3 GetBaseCorner(Vector3 blockPos, int cornerIndex)
    {
        float offset = 0.5f;
        float bx = blockPos.x, by = blockPos.y, bz = blockPos.z;
        switch (cornerIndex)
        {
            case 0: return new Vector3(bx - offset, by - offset, bz + offset);
            case 1: return new Vector3(bx + offset, by - offset, bz + offset);
            case 2: return new Vector3(bx + offset, by - offset, bz - offset);
            case 3: return new Vector3(bx - offset, by - offset, bz - offset);
            case 4: return new Vector3(bx - offset, by + offset, bz + offset);
            case 5: return new Vector3(bx + offset, by + offset, bz + offset);
            case 6: return new Vector3(bx + offset, by + offset, bz - offset);
            case 7: return new Vector3(bx - offset, by + offset, bz - offset);
        }
        return blockPos;
    }

    /// <summary>
    /// We check the 3 sides for this corner, and if the neighbor is AIR => we add 
    /// that side's inward direction to a List. We'll do an average shift from them.
    /// </summary>
    private List<Vector3> GetOpenSides(int bx, int by, int bz, int cornerIndex)
    {
        List<Vector3> openDirs = new List<Vector3>();
        var sides = GetCornerSides(cornerIndex);

        foreach (Side s in sides)
        {
            if (IsAirNeighbor(bx, by, bz, s))
            {
                // add the "inward" direction
                openDirs.Add(GetSideInwardVector(s));
            }
        }
        return openDirs;
    }

    /// <summary>
    /// Returns which sides matter for that corner: 
    /// e.g. corner=4 => top-left-front => sides={TOP,LEFT,FRONT}
    /// </summary>
    private Side[] GetCornerSides(int cIndex)
    {
        switch (cIndex)
        {
            case 0: return new[] { Side.BOTTOM, Side.LEFT, Side.FRONT };
            case 1: return new[] { Side.BOTTOM, Side.RIGHT, Side.FRONT };
            case 2: return new[] { Side.BOTTOM, Side.RIGHT, Side.BACK };
            case 3: return new[] { Side.BOTTOM, Side.LEFT, Side.BACK };
            case 4: return new[] { Side.TOP, Side.LEFT, Side.FRONT };
            case 5: return new[] { Side.TOP, Side.RIGHT, Side.FRONT };
            case 6: return new[] { Side.TOP, Side.RIGHT, Side.BACK };
            case 7: return new[] { Side.TOP, Side.LEFT, Side.BACK };
        }
        return new Side[0];
    }

    private bool IsAirNeighbor(int bx, int by, int bz, Side side)
    {
        int nx = bx, ny = by, nz = bz;
        switch (side)
        {
            case Side.TOP: ny++; break;
            case Side.BOTTOM: ny--; break;
            case Side.LEFT: nx--; break;
            case Side.RIGHT: nx++; break;
            case Side.FRONT: nz++; break;
            case Side.BACK: nz--; break;
        }

        // if OOB => not air
        if (nx < 0 || nx >= width || ny < 0 || ny >= height || nz < 0 || nz >= depth)
            return false;

        var neighbor = chunkData[nx + width * (ny + depth * nz)];
        // Now we treat only AIR as open => no water
        return (neighbor == MeshUtils.BlockType.AIR);
    }

    private Vector3 GetSideInwardVector(Side s)
    {
        switch (s)
        {
            case Side.TOP: return Vector3.down;
            case Side.BOTTOM: return Vector3.up;
            case Side.LEFT: return Vector3.right;
            case Side.RIGHT: return Vector3.left;
            case Side.FRONT: return Vector3.back;
            case Side.BACK: return Vector3.forward;
        }
        return Vector3.zero;
    }

    private enum Side
    {
        TOP,
        BOTTOM,
        LEFT,
        RIGHT,
        FRONT,
        BACK
    }
}
