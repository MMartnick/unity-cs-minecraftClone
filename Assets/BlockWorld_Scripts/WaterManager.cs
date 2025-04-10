using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaterManager : MonoBehaviour
{
    public GameObject player;

    void Update()
    {
        if (player != null)
        {
            this.transform.position = new Vector3(player.transform.position.x, 0, player.transform.position.z);
        }
    }
}
