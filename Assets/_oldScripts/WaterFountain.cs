using UnityEngine;

public class WaterFountain : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public GameObject waterDrop;

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Instantiate(waterDrop, this.transform.position, Quaternion.identity);
    }
}
