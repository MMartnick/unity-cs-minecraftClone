// Assets/BlockWorld_Scripts/Quad.cs
using UnityEngine;
using System.Collections.Generic;
using static MeshUtils;

public class Quad
{
    public Mesh mesh;

    // Precompute the maximum valid row index for blockUVs
    private static readonly int MaxUVRow = blockUVs.GetLength(0) - 1;

    public Quad(BlockSide side, Vector3[] blockCorners, BlockType bType, BlockType hType)
    {
        mesh = new Mesh { name = "ScriptedQuad" };

        // Safely clamp UV‐lookup indices
        int bt = Mathf.Clamp((int)bType, 0, MaxUVRow);
        int ht = Mathf.Clamp((int)hType, 0, MaxUVRow);

        // Build the crack‐stage UV list
        var suvs = new List<Vector2>
        {
            blockUVs[ht, 3],
            blockUVs[ht, 2],
            blockUVs[ht, 0],
            blockUVs[ht, 1]
        };

        // Main texture UVs
        Vector2 uv00 = blockUVs[bt, 0];
        Vector2 uv10 = blockUVs[bt, 1];
        Vector2 uv01 = blockUVs[bt, 2];
        Vector2 uv11 = blockUVs[bt, 3];

        Vector3[] vertices = new Vector3[4];
        Vector3[] normals = new Vector3[4];
        Vector2[] uvs = new Vector2[4];
        int[] tris = { 3, 1, 0, 3, 2, 1 };

        // Select the correct four corners & normals per face
        switch (side)
        {
            case BlockSide.BOTTOM:
                vertices = new[] { blockCorners[0], blockCorners[1], blockCorners[2], blockCorners[3] };
                normals = new[] { Vector3.down, Vector3.down, Vector3.down, Vector3.down };
                uvs = new[] { uv11, uv01, uv00, uv10 };
                break;
            case BlockSide.TOP:
                vertices = new[] { blockCorners[7], blockCorners[6], blockCorners[5], blockCorners[4] };
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                uvs = new[] { uv11, uv01, uv00, uv10 };
                break;
            case BlockSide.LEFT:
                vertices = new[] { blockCorners[7], blockCorners[4], blockCorners[0], blockCorners[3] };
                normals = new[] { Vector3.left, Vector3.left, Vector3.left, Vector3.left };
                uvs = new[] { uv11, uv01, uv00, uv10 };
                break;
            case BlockSide.RIGHT:
                vertices = new[] { blockCorners[5], blockCorners[6], blockCorners[2], blockCorners[1] };
                normals = new[] { Vector3.right, Vector3.right, Vector3.right, Vector3.right };
                uvs = new[] { uv11, uv01, uv00, uv10 };
                break;
            case BlockSide.FRONT:
                vertices = new[] { blockCorners[4], blockCorners[5], blockCorners[1], blockCorners[0] };
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
                uvs = new[] { uv11, uv01, uv00, uv10 };
                break;
            case BlockSide.BACK:
                vertices = new[] { blockCorners[6], blockCorners[7], blockCorners[3], blockCorners[2] };
                normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                uvs = new[] { uv11, uv01, uv00, uv10 };
                break;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.SetUVs(1, suvs);
        mesh.RecalculateBounds();
    }
}
