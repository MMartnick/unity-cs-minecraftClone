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
    public Material waterMaterial; // Transparent (fluid) material

    [Header("Chunk Dimensions")]
    public int width = 10;
    public int height = 10;
    public int depth = 10;

    public Vector3 location;

    // Flattened chunk data
    public MeshUtils.BlockType[] chunkData;
    public MeshUtils.BlockType[] healthData;

    public Block[,,] blocks;

    // New fields for dual-mesh support:
    public MeshRenderer meshRendererSolid;
    public MeshRenderer meshRendererFluid;
    public GameObject solidMesh;
    public GameObject fluidMesh;

    // Corner cache for smoothing/folding
    private Dictionary<(int x, int y, int z, int cornerIndex), Vector3> cornerCache =
        new Dictionary<(int, int, int, int), Vector3>();

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

            // Compute Perlin-based heights
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

            // 3D noise for caves.
            int digCave = (int)MeshUtils.fBM3D(
                x, y, z,
                World.caveSettings.octaves,
                World.caveSettings.scale,
                World.caveSettings.heightScale,
                World.caveSettings.heightOffset
            );

            // Initialize health/cracks info.
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

            // Top surface block
            if (surfaceHeight == y)
            {
                cData[i] = MeshUtils.BlockType.GRASSSIDE; // Alternatively, GRASSTOP for top appearance.
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
                // Water is considered fluid.
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

        NativeArray<MeshUtils.BlockType> blockTypes =
            new NativeArray<MeshUtils.BlockType>(chunkData, Allocator.Persistent);
        NativeArray<MeshUtils.BlockType> healthTypes =
            new NativeArray<MeshUtils.BlockType>(healthData, Allocator.Persistent);

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
    // 2) CREATE THE CHUNK IN MULTIPLE PASSES (SOLID & FLUID)
    ////////////////////////////////////////////////////////////////////////////////

    // The Passes array groups block types into passes.
    // (We assume WATER is the only fluid block processed in its own pass.)
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

        MeshFilter solidMF;
        MeshRenderer solidMR;
        MeshFilter fluidMF;
        MeshRenderer fluidMR;

        // Create or retrieve the child for the solid mesh.
        if (solidMesh == null)
        {
            solidMesh = new GameObject("Solid");
            solidMesh.transform.parent = this.transform;
            solidMF = solidMesh.AddComponent<MeshFilter>();
            solidMR = solidMesh.AddComponent<MeshRenderer>();
            meshRendererSolid = solidMR;
            solidMR.material = atlas; // Opaque material.
        }
        else
        {
            solidMF = solidMesh.GetComponent<MeshFilter>();
        }

        // Create or retrieve the child for the fluid mesh.
        if (fluidMesh == null)
        {
            fluidMesh = new GameObject("Fluid");
            fluidMesh.transform.parent = this.transform;
            fluidMF = fluidMesh.AddComponent<MeshFilter>();
            fluidMR = fluidMesh.AddComponent<MeshRenderer>();
            meshRendererFluid = fluidMR;
            fluidMR.material = waterMaterial; // Transparent fluid material.
        }
        else
        {
            fluidMF = fluidMesh.GetComponent<MeshFilter>();
        }

        // Build or rebuild the block data.
        blocks = new Block[width, height, depth];
        if (rebuildBlocks)
            BuildChunk();

        // Prepare separate lists for solid and fluid meshes.
        List<Mesh> solidMeshesList = new List<Mesh>();
        List<Mesh> fluidMeshesList = new List<Mesh>();

        // Two-pass loop:
        // Pass = 0 for non-fluid blocks, Pass = 1 for fluid blocks.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int passIndex = 0; passIndex < Passes.Length; passIndex++)
            {
                var passTypes = Passes[passIndex];
                // Determine if this pass group is fluid.
                bool isFluidType = (passTypes.Length == 1 && passTypes[0] == MeshUtils.BlockType.WATER);
                var passMeshes = BuildBlockMeshesForTypes(passTypes);
                if (pass == 0 && !isFluidType)
                {
                    solidMeshesList.AddRange(passMeshes);
                }
                else if (pass == 1 && isFluidType)
                {
                    fluidMeshesList.AddRange(passMeshes);
                }
            }
        }

        // Combine the solid meshes into one mesh.
        Mesh terrainMesh = CombineMeshes(solidMeshesList, $"Terrain_{location.x}_{location.y}_{location.z}");
        solidMF.mesh = terrainMesh;
        // Fix chunk boundary overlap for solid mesh.
        FixChunkBoundaryOverlap(terrainMesh, 0.001f);

        // Assign a collider only to the solid mesh (fluid does not get a collider).
        MeshCollider collider = GetComponent<MeshCollider>();
        if (collider == null)
            collider = gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = terrainMesh;

        // Combine the fluid meshes (if any) into a separate mesh.
        if (fluidMeshesList.Count > 0)
        {
            Mesh waterMesh = CombineMeshes(fluidMeshesList, $"Water_{location.x}_{location.y}_{location.z}");
            // Lower water by an additional 0.001f to help with depth fighting.
            AdjustWaterMeshZFighting(waterMesh, -0.001f);
            // Fix chunk boundary overlap for water as well.
            FixChunkBoundaryOverlap(waterMesh, 0.001f);
            fluidMF.mesh = waterMesh;
        }
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
        if (nx < 0 || nx >= width || ny < 0 || ny >= height || nz < 0 || nz >= depth)
            return false;

        var neighbor = chunkData[nx + width * (ny + depth * nz)];
        return (neighbor == MeshUtils.BlockType.AIR);
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

                    // Create the block (this builds its mesh).
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
    /// Combine a list of individual meshes into one mesh.
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

        // Tally vertex and index counts.
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

            // Read source mesh data.
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

            // Copy indices.
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
    // 3) CORNER FOLDING: Adjust corners based on open (AIR) sides.
    //    If a side is open, the corner is shifted inward.
    //    Additionally, we lower water corners by 0.2f.
    ////////////////////////////////////////////////////////////////////////////////

    // Returns true if the adjacent side is considered open for folding based on the current block type.
    private bool IsOpenForFolding(int bx, int by, int bz, Side side, MeshUtils.BlockType currentBlockType)
    {
        int nx = bx, ny = by, nz = bz;
        switch (side)
        {
            case Side.TOP: ny--; break;
            case Side.BOTTOM: ny++; break;
            case Side.LEFT: nx--; break;
            case Side.RIGHT: nx++; break;
            case Side.FRONT: nz++; break;
            case Side.BACK: nz--; break;
        }

        // If out-of-bounds, treat as open.
        if (nx < 0 || nx >= width || ny < 0 || ny >= height || nz < 0 || nz >= depth)
            return true;

        MeshUtils.BlockType neighbor = chunkData[nx + width * (ny + depth * nz)];

        // Solid blocks: open if neighbor is AIR or WATER.
        if (currentBlockType != MeshUtils.BlockType.WATER)
            return (neighbor == MeshUtils.BlockType.AIR || neighbor == MeshUtils.BlockType.WATER);
        else // Water blocks: open only if neighbor is AIR.
            return (neighbor == MeshUtils.BlockType.AIR);
    }

    // Revised GetOpenSides which takes current block type into account.
    private List<Vector3> GetOpenSides(int bx, int by, int bz, int cornerIndex, MeshUtils.BlockType currentBlockType)
    {
        List<Vector3> openDirs = new List<Vector3>();
        var sides = GetCornerSides(cornerIndex);
        foreach (Side s in sides)
        {
            if (IsOpenForFolding(bx, by, bz, s, currentBlockType))
                openDirs.Add(GetSideInwardVector(s));
        }
        return openDirs;
    }

    // Revised GetOrComputeCorner: For water blocks, simply lower the corner by 0.2f; then apply folding if any side is open.
    // Revised GetOrComputeCorner which now checks the corner vertex directly.
    public Vector3 GetOrComputeCorner(int bx, int by, int bz, int cornerIndex, MeshUtils.BlockType blockType)
    {
        var key = (bx, by, bz, cornerIndex);
        if (cornerCache.TryGetValue(key, out Vector3 cachedPos))
            return cachedPos;

        Vector3 baseCorner = GetBaseCorner(new Vector3(bx, by, bz), cornerIndex);

        // For water blocks, if this is a top corner (cornerIndex 4–7) and the corner vertex is exposed (touching air),
        // then lower the corner by 0.2f.
        if (blockType == MeshUtils.BlockType.WATER && cornerIndex >= 4)
        {
            if (IsCornerExposed(bx, by, bz, cornerIndex))
            {
                baseCorner.y -= 0.2f;
            }
        }

        // For both solid and water, if more than 4 sides are open, fold the corner inward.
        var openSides = GetOpenSides(bx, by, bz, cornerIndex, blockType);
        if (openSides.Count > 3)
        {
            Vector3 sumDir = Vector3.zero;
            foreach (var dir in openSides)
                sumDir += dir;
            if (sumDir.magnitude > -0.1f)
            {
                Vector3 fold = sumDir.normalized * 0.5f;
                baseCorner += fold;
            }
        }

        Vector3 worldCorner = baseCorner + location;
        cornerCache[key] = worldCorner;
        return worldCorner;
    }

    /// <summary>
    /// Returns the local corner position without folding.
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
    /// Returns which sides are relevant for this corner.
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

    private bool IsCornerExposed(int bx, int by, int bz, int cornerIndex)
    {
        // Determine the relative integer offsets for this corner.
        // We'll assume for a block at (bx,by,bz) the 8 corners are at:
        // (bx, by, bz), (bx+1, by, bz), (bx, by+1, bz), (bx+1, by+1, bz),
        // (bx, by, bz+1), (bx+1, by, bz+1), (bx, by+1, bz+1), (bx+1, by+1, bz+1).
        // We need to map our cornerIndex to the appropriate offsets.
        int dx = (cornerIndex == 1 || cornerIndex == 3 || cornerIndex == 5 || cornerIndex == 7) ? 1 : 0;
        int dy = (cornerIndex >= 4) ? 1 : 0;
        int dz = (cornerIndex >= 2 && cornerIndex <= 3) || (cornerIndex >= 6) ? 1 : 0;

        // The vertex of interest is at (bx+dx, by+dy, bz+dz).
        // Check the 8 blocks that share that vertex.
        // Those blocks have their integer coordinates in ranges:
        // X: (bx+dx - 1) to (bx+dx)
        // Y: (by+dy - 1) to (by+dy)
        // Z: (bz+dz - 1) to (bz+dz)
        for (int ox = 0; ox < 2; ox++)
        {
            for (int oy = 0; oy < 2; oy++)
            {
                for (int oz = 0; oz < 2; oz++)
                {
                    int checkX = (bx + dx - 1) + ox;
                    int checkY = (by + dy - 1) + oy;
                    int checkZ = (bz + dz - 1) + oz;
                    // If any of these coordinates are out-of-bounds, consider the vertex exposed.
                    if (checkX < 0 || checkX >= width ||
                        checkY < 0 || checkY >= height ||
                        checkZ < 0 || checkZ >= depth)
                    {
                        return true;
                    }
                    int index = checkX + width * (checkY + depth * checkZ);
                    if (chunkData[index] == MeshUtils.BlockType.AIR)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
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

    ////////////////////////////////////////////////////////////////////////////////
    // NEW HELPER METHODS FOR OVERLAP & Z-FIGHTING
    ////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Slightly adjusts vertices at the chunk boundaries (in X and Z) to prevent overlap.
    /// </summary>
    private void FixChunkBoundaryOverlap(Mesh mesh, float offset)
    {
        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            // If near the left or bottom edge (using a tolerance), nudge outward.
            if (Mathf.Abs(verts[i].x) < 0.001f)
                verts[i].x -= offset;
            else if (Mathf.Abs(verts[i].x - width) < 0.001f)
                verts[i].x += offset;

            if (Mathf.Abs(verts[i].z) < 0.001f)
                verts[i].z -= offset;
            else if (Mathf.Abs(verts[i].z - depth) < 0.001f)
                verts[i].z += offset;
        }
        mesh.vertices = verts;
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// Adjusts water mesh vertices in Y to alleviate z-fighting.
    /// </summary>
    private void AdjustWaterMeshZFighting(Mesh mesh, float yOffset)
    {
        Vector3[] verts = mesh.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            verts[i].y += yOffset;
        }
        mesh.vertices = verts;
        mesh.RecalculateBounds();
    }
}
