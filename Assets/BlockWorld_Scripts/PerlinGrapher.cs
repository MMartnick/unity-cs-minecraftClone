using UnityEngine;

[ExecuteInEditMode]
public class PerlinGrapher : MonoBehaviour
{

    LineRenderer lr;    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = 100;
        Graph();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void Graph()
    {
        int z = 11;
        Vector3[] positions = new Vector3[lr.positionCount];
        for(int x =0; x < lr.positionCount; x++)
        {
            positions[x] = new Vector3(x, 0, z);
        }
        lr.SetPositions(positions);
    }
}
