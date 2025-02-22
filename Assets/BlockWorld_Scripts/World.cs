using UnityEngine;

public class World : MonoBehaviour
{
    public static Vector3 worldDimension = new Vector3(3, 3, 3);
    public static Vector3 chunkDimensions = new Vector3(10, 10, 10);
    public GameObject chunkPrefab;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
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
                }
            }
        }
     

        // Update is called once per frame
        void Update()
        {

        }
    }
}
