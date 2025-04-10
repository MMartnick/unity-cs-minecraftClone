using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static MeshUtils;
using UnityEngine.SocialPlatforms;

public class Block
{
    public Mesh mesh;
    private Chunk parentChunk;

    public Block(Vector3 offset, MeshUtils.BlockType type, Chunk chunk, MeshUtils.BlockType htype)
    {
        parentChunk = chunk;
        Vector3 blockLocalPos = offset - chunk.location;

        // If this block is AIR, no mesh
        if (type == MeshUtils.BlockType.AIR)
        {
            mesh = null;
            return;
        }

        // If block is fully enclosed by solids, no faces => skip
        if (IsFullyEnclosed((int)blockLocalPos.x, (int)blockLocalPos.y, (int)blockLocalPos.z))
        {
            mesh = null;
            return;
        }

        // 1) Retrieve the 8 final corner positions (smoothing, partial diagonal weighting),
        //    but let the chunk handle caching so corners line up with adjacent blocks.
        Vector3[] corners = new Vector3[8];
        for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            corners[cornerIndex] = parentChunk.GetOrComputeCorner(
                (int)blockLocalPos.x,
                (int)blockLocalPos.y,
                (int)blockLocalPos.z,
                cornerIndex,
                type
            );
        }

        // 2) Build quads for any visible face
        List<Quad> quads = new List<Quad>();

        if (type == MeshUtils.BlockType.WATER)
        {
            // Only add a bottom face if the block below is AIR.
            // Here we convert the local coordinate by subtracting 1 from the localY.
            if (GetNeighbourBlockType((int)blockLocalPos.x, (int)blockLocalPos.y + 1, (int)blockLocalPos.z) == MeshUtils.BlockType.AIR)
            {
                quads.Add(new Quad(MeshUtils.BlockSide.TOP, corners, type, htype));
            }
        }
        else
        {
            // bottom
            if (!HasSolidNeighbour((int)blockLocalPos.x, (int)blockLocalPos.y - 1, (int)blockLocalPos.z))
            {
                if (type == MeshUtils.BlockType.GRASSSIDE)
                    quads.Add(new Quad(MeshUtils.BlockSide.BOTTOM, corners, MeshUtils.BlockType.DIRT, htype));
                else
                    quads.Add(new Quad(MeshUtils.BlockSide.BOTTOM, corners, type, htype));
            }

            // top
            if (!HasSolidNeighbour((int)blockLocalPos.x, (int)blockLocalPos.y + 1, (int)blockLocalPos.z))
            {
                if (type == MeshUtils.BlockType.GRASSSIDE)
                    quads.Add(new Quad(MeshUtils.BlockSide.TOP, corners, MeshUtils.BlockType.GRASSTOP, htype));
                else
                    quads.Add(new Quad(MeshUtils.BlockSide.TOP, corners, type, htype));
            }

            // left
            if (!HasSolidNeighbour((int)blockLocalPos.x - 1, (int)blockLocalPos.y, (int)blockLocalPos.z))
                quads.Add(new Quad(MeshUtils.BlockSide.LEFT, corners, type, htype));
            // right
            if (!HasSolidNeighbour((int)blockLocalPos.x + 1, (int)blockLocalPos.y, (int)blockLocalPos.z))
                quads.Add(new Quad(MeshUtils.BlockSide.RIGHT, corners, type, htype));
            // front
            if (!HasSolidNeighbour((int)blockLocalPos.x, (int)blockLocalPos.y, (int)blockLocalPos.z + 1))
                quads.Add(new Quad(MeshUtils.BlockSide.FRONT, corners, type, htype));
            // back
            if (!HasSolidNeighbour((int)blockLocalPos.x, (int)blockLocalPos.y, (int)blockLocalPos.z - 1))
                quads.Add(new Quad(MeshUtils.BlockSide.BACK, corners, type, htype));
        }
        if (quads.Count == 0)
        {
            mesh = null;
            return;
        }

        // Merge quads into final mesh
        Mesh[] sideMeshes = new Mesh[quads.Count];
        for (int i = 0; i < quads.Count; i++)
        {
            sideMeshes[i] = quads[i].mesh;
        }
        mesh = MeshUtils.MergeMeshes(sideMeshes);
        mesh.name = "Cube_Smoothed";
    }

    /// <summary>
    /// Returns the block type of the neighbor at the given local coordinates (relative to the parent chunk).
    /// If the coordinates are out of bounds, we assume the neighbor is AIR.
    /// </summary>
    private MeshUtils.BlockType GetNeighbourBlockType(int localX, int localY, int localZ)
    {
        int w = parentChunk.width;
        int h = parentChunk.height;
        int d = parentChunk.depth;
        // If out of bounds, assume AIR.
        if (localX < 0 || localX >= w || localY < 0 || localY >= h || localZ < 0 || localZ >= d)
            return MeshUtils.BlockType.AIR;
        int index = localX + w * (localY + d * localZ);
        return parentChunk.chunkData[index];
    }



    private bool IsFullyEnclosed(int x, int y, int z)
    {
        if (!IsNeighborSolid(x, y - 1, z)) return false;
        if (!IsNeighborSolid(x, y + 1, z)) return false;
        if (!IsNeighborSolid(x - 1, y, z)) return false;
        if (!IsNeighborSolid(x + 1, y, z)) return false;
        if (!IsNeighborSolid(x, y, z + 1)) return false;
        if (!IsNeighborSolid(x, y, z - 1)) return false;
        return true;
    }

    private bool IsNeighborSolid(int nx, int ny, int nz)
    {
        if (nx < 0 || nx >= parentChunk.width ||
            ny < 0 || ny >= parentChunk.height ||
            nz < 0 || nz >= parentChunk.depth)
        {
            return false;
        }

        var neighborType = parentChunk.chunkData[nx + parentChunk.width * (ny + parentChunk.depth * nz)];
        // If neighbor is AIR or WATER => not solid
        if (neighborType == MeshUtils.BlockType.AIR || neighborType == MeshUtils.BlockType.WATER)
            return false;
        return true;
    }

    public bool HasSolidNeighbour(int x, int y, int z)
    {
        if (x < 0 || x >= parentChunk.width ||
            y < 0 || y >= parentChunk.height ||
            z < 0 || z >= parentChunk.depth)
        {
            // out-of-bounds => not solid => face is visible
            return false;
        }

        var neighborType = parentChunk.chunkData[x + parentChunk.width * (y + parentChunk.depth * z)];
        if (neighborType == MeshUtils.BlockType.AIR || neighborType == MeshUtils.BlockType.WATER)
            return false;
        return true;
    }
}
