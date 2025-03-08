using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Quad
{
    public Mesh mesh;

    public Quad(MeshUtils.BlockSide side, Vector3[] blockCorners,
                MeshUtils.BlockType bType, MeshUtils.BlockType hType)
    {
        mesh = new Mesh();
        mesh.name = "ScriptedQuad";

        Vector3[] vertices = new Vector3[4];
        Vector3[] normals = new Vector3[4];
        Vector2[] uvs = new Vector2[4];
        int[] triangles = new int[6] { 3, 1, 0, 3, 2, 1 };

        // Second UV set for "cracks" (damage stages)
        List<Vector2> suvs = new List<Vector2>();
        suvs.Add(MeshUtils.blockUVs[(int)hType, 3]);
        suvs.Add(MeshUtils.blockUVs[(int)hType, 2]);
        suvs.Add(MeshUtils.blockUVs[(int)hType, 0]);
        suvs.Add(MeshUtils.blockUVs[(int)hType, 1]);

        // Main texture UV
        Vector2 uv00 = MeshUtils.blockUVs[(int)bType, 0];
        Vector2 uv10 = MeshUtils.blockUVs[(int)bType, 1];
        Vector2 uv01 = MeshUtils.blockUVs[(int)bType, 2];
        Vector2 uv11 = MeshUtils.blockUVs[(int)bType, 3];

        switch (side)
        {
            case MeshUtils.BlockSide.BOTTOM:
                vertices = new Vector3[]
                {
                    blockCorners[0],
                    blockCorners[1],
                    blockCorners[2],
                    blockCorners[3]
                };
                normals = new Vector3[] { Vector3.down, Vector3.down, Vector3.down, Vector3.down };
                uvs = new Vector2[] { uv11, uv01, uv00, uv10 };
                break;

            case MeshUtils.BlockSide.TOP:
                vertices = new Vector3[]
                {
                    blockCorners[7],
                    blockCorners[6],
                    blockCorners[5],
                    blockCorners[4]
                };
                normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                uvs = new Vector2[] { uv11, uv01, uv00, uv10 };
                break;

            case MeshUtils.BlockSide.LEFT:
                vertices = new Vector3[]
                {
                    blockCorners[7],
                    blockCorners[4],
                    blockCorners[0],
                    blockCorners[3]
                };
                normals = new Vector3[] { Vector3.left, Vector3.left, Vector3.left, Vector3.left };
                uvs = new Vector2[] { uv11, uv01, uv00, uv10 };
                break;

            case MeshUtils.BlockSide.RIGHT:
                vertices = new Vector3[]
                {
                    blockCorners[5],
                    blockCorners[6],
                    blockCorners[2],
                    blockCorners[1]
                };
                normals = new Vector3[] { Vector3.right, Vector3.right, Vector3.right, Vector3.right };
                uvs = new Vector2[] { uv11, uv01, uv00, uv10 };
                break;

            case MeshUtils.BlockSide.FRONT:
                vertices = new Vector3[]
                {
                    blockCorners[4],
                    blockCorners[5],
                    blockCorners[1],
                    blockCorners[0]
                };
                normals = new Vector3[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
                uvs = new Vector2[] { uv11, uv01, uv00, uv10 };
                break;

            case MeshUtils.BlockSide.BACK:
                vertices = new Vector3[]
                {
                    blockCorners[6],
                    blockCorners[7],
                    blockCorners[3],
                    blockCorners[2]
                };
                normals = new Vector3[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                uvs = new Vector2[] { uv11, uv01, uv00, uv10 };
                break;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.SetUVs(1, suvs);
        mesh.RecalculateBounds();
    }
}
