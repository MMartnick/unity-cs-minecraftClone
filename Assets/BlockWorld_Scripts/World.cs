using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.UI;


public class World : MonoBehaviour
{
    public static Vector3 worldDimension = new Vector3(10, 10, 10);
    public static Vector3 chunkDimensions = new Vector3(10, 10, 10);
    public GameObject chunkPrefab;

    public GameObject mCamera;
    public GameObject fpc;
    public Slider loadingBar;

    void Start()
    {
        loadingBar.maxValue = worldDimension.x * worldDimension.y * worldDimension.z;
        StartCoroutine(BuildWorld());
    }
    // Update is called once per frame
    void Update()
    {

    }
    IEnumerator BuildWorld()
    {

        for (int z = 0; z < worldDimension.z; z++)
        {
            for (int y = 0; y < worldDimension.y; y++)
            {
                for (int x = 0; x < worldDimension.x; x++)
                {
                    GameObject chunk = Instantiate(chunkPrefab);
                    Vector3 position = new Vector3(x * chunkDimensions.x, y * chunkDimensions.y, z * chunkDimensions.z);
                    chunk.GetComponent<Chunk>().CreateChunk(chunkDimensions, position);
                    loadingBar.value++;

                    yield return null;
                }
            }
        }

        mCamera.SetActive(false);       

        float xpos = (worldDimension.x * chunkDimensions.x) /2.0f;
        float zpos = (worldDimension.z * chunkDimensions.z) / 2.0f;
        Chunk c = chunkPrefab.GetComponent<Chunk>();
        float ypos = MeshUtils.fBM(xpos, zpos, c.octaves, c.scale, c.heightScale, c.heightOffset) + 10;
        fpc.transform.position = new Vector3(xpos, ypos, zpos);
        loadingBar.gameObject.SetActive(false);
        fpc.SetActive(true);
    }
}
