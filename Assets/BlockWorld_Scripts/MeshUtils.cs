using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using VertexData = System.Tuple<UnityEngine.Vector3, UnityEngine.Vector3, UnityEngine.Vector2, UnityEngine.Vector2>;

public static class MeshUtils
{
    public enum BlockType
    {
        GRASSTOP, GRASSSIDE, DIRT, WATER, STONE, LEAVES, WOOD, WOODBASE, SAND, GOLD, BEDROCK, REDSTONE, DIAMOND, NOCRACK,
        CRACK1, CRACK2, CRACK3, CRACK4, AIR
    };

    public static int[] blockTypeHealth =
    { 2, 2, 1, 1, 4, 2, 4, 4, 3, 4, -1, 3, 4, -1, -1, -1, -1, -1, -1
    };

    public static HashSet<BlockType> canDrop = new HashSet<BlockType> {  BlockType.WATER };
    public static HashSet<BlockType> canFlow = new HashSet<BlockType> { BlockType.WATER };

    public enum BlockSide { BOTTOM, TOP, LEFT, RIGHT, FRONT, BACK };

    public static Vector2[,] blockUVs = {
        /*GRASSTOP*/ {  new Vector2(0.125f, 0.375f), new Vector2(0.1875f,0.375f),
                        new Vector2(0.125f, 0.4375f), new Vector2(0.1875f,0.4375f) },
        /*GRASSSIDE*/ { new Vector2( 0.1875f, 0.9375f ), new Vector2( 0.25f, 0.9375f),
                        new Vector2( 0.1875f, 1.0f ),new Vector2( 0.25f, 1.0f )},
        /*DIRT*/	  { new Vector2( 0.125f, 0.9375f ), new Vector2( 0.1875f, 0.9375f),
                        new Vector2( 0.125f, 1.0f ),new Vector2( 0.1875f, 1.0f )},
        /*WATER*/	  { new Vector2(0.875f,0.125f),  new Vector2(0.9375f,0.125f),
                        new Vector2(0.875f,0.1875f), new Vector2(0.9375f,0.1875f)},
        /*STONE*/	  { new Vector2( 0, 0.875f ), new Vector2( 0.0625f, 0.875f),
                        new Vector2( 0, 0.9375f ),new Vector2( 0.0625f, 0.9375f )},
        /*LEAVES*/	  { new Vector2(0.0625f,0.375f),  new Vector2(0.125f,0.375f),
                        new Vector2(0.0625f,0.4375f), new Vector2(0.125f,0.4375f)},
 		/*WOOD*/	  { new Vector2(0.375f,0.625f),  new Vector2(0.4375f,0.625f),
                        new Vector2(0.375f,0.6875f), new Vector2(0.4375f,0.6875f)},
 		/*WOODBASE*/  { new Vector2(0.375f,0.625f),  new Vector2(0.4375f,0.625f),
                        new Vector2(0.375f,0.6875f), new Vector2(0.4375f,0.6875f)},	    
        /*SAND*/	  { new Vector2(0.125f,0.875f),  new Vector2(0.1875f,0.875f),
                        new Vector2(0.125f,0.9375f), new Vector2(0.1875f,0.9375f)},
        /*GOLD*/		{ new Vector2(0f,0.8125f),  new Vector2(0.0625f,0.8125f),
                          new Vector2(0f,0.875f), new Vector2(0.0625f,0.875f)},
		/*BEDROCK*/		{new Vector2( 0.3125f, 0.8125f ), new Vector2( 0.375f, 0.8125f),
                                new Vector2( 0.3125f, 0.875f ),new Vector2( 0.375f, 0.875f )},
		/*REDSTONE*/	{new Vector2( 0.1875f, 0.75f ), new Vector2( 0.25f, 0.75f),
                                new Vector2( 0.1875f, 0.8125f ),new Vector2( 0.25f, 0.8125f )},
		/*DIAMOND*/		{new Vector2( 0.125f, 0.75f ), new Vector2( 0.1875f, 0.75f),
                                new Vector2( 0.125f, 0.8125f ),new Vector2( 0.1875f, 0.8125f )},
		/*NOCRACK*/		{new Vector2( 0.6875f, 0f ), new Vector2( 0.75f, 0f),
                                new Vector2( 0.6875f, 0.0625f ),new Vector2( 0.75f, 0.0625f )},
		/*CRACK1*/		{ new Vector2(0f,0f),  new Vector2(0.0625f,0f),
                                 new Vector2(0f,0.0625f), new Vector2(0.0625f,0.0625f)},
 		/*CRACK2*/		{ new Vector2(0.0625f,0f),  new Vector2(0.125f,0f),
                                 new Vector2(0.0625f,0.0625f), new Vector2(0.125f,0.0625f)},
 		/*CRACK3*/		{ new Vector2(0.125f,0f),  new Vector2(0.1875f,0f),
                                 new Vector2(0.125f,0.0625f), new Vector2(0.1875f,0.0625f)},
 		/*CRACK4*/		{ new Vector2(0.1875f,0f),  new Vector2(0.25f,0f),
                                 new Vector2(0.1875f,0.0625f), new Vector2(0.25f,0.0625f)}
    };

    public static float fBM(float x, float z, int octaves, float scale, float heightScale, float heightOffset)
    {
        float total = 0;
        float frequency = 1;
        for (int i = 0; i < octaves; i++)
        {
            total += Mathf.PerlinNoise(x * scale * frequency, z * scale * frequency) * heightScale;
            frequency *= 2;
        }
        return total + heightOffset;
    }

    public static float fBM3D(float x, float y, float z, int octaves, float scale, float heightScale, float heightOffset)
    {
        float XY = fBM(x, y, octaves, scale, heightScale, heightOffset);
        float YZ = fBM(y, z, octaves, scale, heightScale, heightOffset);
        float XZ = fBM(x, z, octaves, scale, heightScale, heightOffset);
        float YX = fBM(y, x, octaves, scale, heightScale, heightOffset);
        float ZY = fBM(z, y, octaves, scale, heightScale, heightOffset);
        float ZX = fBM(z, x, octaves, scale, heightScale, heightOffset);

        return (XY + YZ + XZ + YX + ZY + ZX) / 6.0f;
    }


    public static Mesh MergeMeshes(Mesh[] meshes)
    {
        Mesh mesh = new Mesh();

        Dictionary<VertexData, int> pointsOrder = new Dictionary<VertexData, int>();
        HashSet<VertexData> pointsHash = new HashSet<VertexData>();
        List<int> tris = new List<int>();

        int pIndex = 0;
        for (int i = 0; i < meshes.Length; i++) //loop through each mesh
        {
            if (meshes[i] == null) continue;
            for (int j = 0; j < meshes[i].vertices.Length; j++) //loop through each vertex of the current mesh
            {
                Vector3 v = meshes[i].vertices[j];
                Vector3 n = meshes[i].normals[j];
                Vector2 u = meshes[i].uv[j];
                Vector2 u2 = meshes[i].uv2[j];
                VertexData p = new VertexData(v, n, u, u2);
                if (!pointsHash.Contains(p))
                {
                    pointsOrder.Add(p, pIndex);
                    pointsHash.Add(p);

                    pIndex++;
                }

            }

            for (int t = 0; t < meshes[i].triangles.Length; t++)
            {
                int triPoint = meshes[i].triangles[t];
                Vector3 v = meshes[i].vertices[triPoint];
                Vector3 n = meshes[i].normals[triPoint];
                Vector2 u = meshes[i].uv[triPoint];
                Vector2 u2 = meshes[i].uv2[triPoint];
                VertexData p = new VertexData(v, n, u, u2);

                int index;
                pointsOrder.TryGetValue(p, out index);
                tris.Add(index);
            }
            meshes[i] = null;
        }

        ExtractArrays(pointsOrder, mesh);
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static void ExtractArrays(Dictionary<VertexData, int> list, Mesh mesh)
    {
        List<Vector3> verts = new List<Vector3>();
        List<Vector3> norms = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Vector2> uvs2 = new List<Vector2>();

        foreach (VertexData v in list.Keys)
        {
            verts.Add(v.Item1);
            norms.Add(v.Item2);
            uvs.Add(v.Item3);
            uvs2.Add(v.Item4);
        }
        mesh.vertices = verts.ToArray();
        mesh.normals = norms.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.uv2 = uvs2.ToArray();
    }

}
