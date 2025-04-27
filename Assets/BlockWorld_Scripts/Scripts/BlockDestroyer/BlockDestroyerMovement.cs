using UnityEngine;

/// <summary>
/// Spawns this GameObject just outside the world on a random side
/// (x or z axis), with Y in [10,20], then moves it straight across
/// toward the opposite point around the world’s center.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BlockDestroyerMovement : MonoBehaviour
{
    [Tooltip("Movement speed in units/sec.")]
    [SerializeField] private float speed = 5f;

    private Vector3 _direction;
    Vector3 opposite;
    void Start()
    {
        // Compute total world size in blocks
        float worldWidth = World.worldDimensions.x * World.chunkDimensions.x;
        float worldDepth = World.worldDimensions.z * World.chunkDimensions.z;

        // Pick a random side: 0=left,1=right,2=front,3=back
        int side = Random.Range(0, 4);

        // Random Y between 10 and 20
        float y = Random.Range(10f, 20f);

        Vector3 spawn = Vector3.zero;
        switch (side)
        {
            case 0: // left side
                spawn.x = Random.Range(-World.chunkDimensions.x, 0f);
                spawn.z = Random.Range(0f, worldDepth);
                break;
            case 1: // right side
                spawn.x = Random.Range(worldWidth, worldWidth + World.chunkDimensions.x);
                spawn.z = Random.Range(0f, worldDepth);
                break;
            case 2: // front side
                spawn.x = Random.Range(0f, worldWidth);
                spawn.z = Random.Range(-World.chunkDimensions.z, 0f);
                break;
            case 3: // back side
                spawn.x = Random.Range(0f, worldWidth);
                spawn.z = Random.Range(worldDepth, worldDepth + World.chunkDimensions.z);
                break;
        }
        spawn.y = y;

        // Position the object
        transform.position = spawn;

        // Compute world center (on XZ plane), keep same Y
        Vector3 center = new Vector3(worldWidth / 2f, y, worldDepth / 2f);

        // Diametrically opposite point across the center
         opposite = new Vector3(
            2f * center.x - spawn.x,
            y,
            2f * center.z - spawn.z
        );

        // Movement direction
        _direction = (opposite - spawn).normalized;

        // Rigidbody kinematic so collisions still fire, but no physics drift
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void Update()
    {
        // Move in a straight line
        transform.position += _direction * speed * Time.deltaTime;

        if(transform.position == new Vector3(opposite.x, transform.position.y, opposite.z))
        {
            var thisDestroyer = FindAnyObjectByType<BlockDestroyer>();
            Destroy(thisDestroyer);
        }
    }


}
