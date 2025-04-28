using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.UI;
using static MeshUtils;

public struct PerlinSettings
{
    public float heightScale;
    public float scale;
    public int octaves;
    public float heightOffset;
    public float probability;

    public PerlinSettings(float hs, float s, int o, float ho, float p)
    {
        heightScale = hs;
        scale = s;
        octaves = o;
        heightOffset = ho;
        probability = p;
    }
}

public class World : MonoBehaviour
{
    public static Vector3Int worldDimensions = new Vector3Int(5,5,5);
    public static Vector3Int extraWorldDimensions = new Vector3Int(1, 1, 1);
    public static Vector3Int chunkDimensions = new Vector3Int(10, 10, 10);
    public bool loadFromFile = false;
    public GameObject chunkPrefab;

    public GameObject mCamera;
    public GameObject fpc;

    // ADD THIS: reference to your third-person camera:
    public GameObject thirdPersonCamera;

    // If you want a quick check for which camera is on:
    private bool usingThirdPerson = false;

    public Image crosshair;

    public GameObject[] agents;
    public Slider loadingBar;

    public static PerlinSettings surfaceSettings;
    public PerlinGrapher surface;

    public static PerlinSettings sandSettings;
    public PerlinGrapher sand;

    public static PerlinSettings stoneSettings;
    public PerlinGrapher stone;

    public static PerlinSettings diamondTSettings;
    public PerlinGrapher diamondT;

    public static PerlinSettings diamondBSettings;
    public PerlinGrapher diamondB;

    public static PerlinSettings caveSettings;
    public Perlin3DGrapher caves;

    public GameObject highlightPrefab;  // Assign in Inspector
    private GameObject highlightObject; // We'll instantiate at runtime

    public HashSet<Vector3Int> chunkChecker = new HashSet<Vector3Int>();
    public HashSet<Vector2Int> chunkColumns = new HashSet<Vector2Int>();
    public Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();

    Vector3Int lastBuildPosition;
    int drawRadius = 50;

    Queue<IEnumerator> buildQueue = new Queue<IEnumerator>();

    IEnumerator BuildCoordinator()
    {
        while (true)
        {
            while (buildQueue.Count > 0)
                yield return StartCoroutine(buildQueue.Dequeue());
            yield return null;
        }
    }

    public void SaveWorld()
    {
        FileSaver.Save(this);
    }

    IEnumerator LoadWorldFromFile()
    {
        WorldData wd = FileSaver.Load();
        if (wd == null)
        {
            StartCoroutine(BuildWorld());


            yield break;

        }

        chunkChecker.Clear();
        for (int i = 0; i < wd.chunkCheckerValues.Length; i += 3)
        {
            chunkChecker.Add(new Vector3Int(
                wd.chunkCheckerValues[i],
                wd.chunkCheckerValues[i + 1],
                wd.chunkCheckerValues[i + 2]
            ));
        }

        chunkColumns.Clear();
        for (int i = 0; i < wd.chunkColumnValues.Length; i += 2)
        {
            chunkColumns.Add(new Vector2Int(
                wd.chunkColumnValues[i],
                wd.chunkColumnValues[i + 1]
            ));
        }

        int index = 0;
        int vIndex = 0;
        loadingBar.maxValue = chunkChecker.Count;
        foreach (Vector3Int chunkPos in chunkChecker)
        {
            GameObject chunkGO = Instantiate(chunkPrefab);
            chunkGO.name = "Chunk_" + chunkPos.x + "_" + chunkPos.y + "_" + chunkPos.z;
            Chunk c = chunkGO.GetComponent<Chunk>();

            int blockCount = chunkDimensions.x * chunkDimensions.y * chunkDimensions.z;
            c.chunkData = new MeshUtils.BlockType[blockCount];
            c.healthData = new MeshUtils.BlockType[blockCount];

            for (int i = 0; i < blockCount; i++)
            {
                c.chunkData[i] = (MeshUtils.BlockType)wd.allChunkData[index];
                c.healthData[i] = MeshUtils.BlockType.NOCRACK;
                index++;
            }
            loadingBar.value++;
            c.CreateChunk(chunkDimensions, chunkPos, false);
            chunks.Add(chunkPos, c);
            RedrawChunk(c);
            if (c.meshRendererSolid != null)
                c.meshRendererSolid.enabled = wd.chunkVisibility[vIndex];
            if (c.meshRendererFluid != null)
                c.meshRendererFluid.enabled = wd.chunkVisibility[vIndex];

            vIndex++;
            yield return null;
        }

        fpc.transform.position = new Vector3(wd.fpcX, wd.fpcY, wd.fpcZ);
        thirdPersonCamera.transform.position = new Vector3(wd.fpcX, 35, wd.fpcZ);

        mCamera.SetActive(false);
        thirdPersonCamera.SetActive(true);


        loadingBar.gameObject.SetActive(false);
        lastBuildPosition = Vector3Int.CeilToInt(fpc.transform.position);
        StartCoroutine(BuildCoordinator());
        StartCoroutine(UpdateWorld());



        
    }


        struct CalculateBlockTypes : IJobParallelFor
        {
            public NativeArray<MeshUtils.BlockType> cData;
            public NativeArray<MeshUtils.BlockType> hData;
            public int width;
            public int height;
            public Vector3 location;
            public NativeArray<Unity.Mathematics.Random> randoms;

            public void Execute(int i)
            {
                int x = i % width + (int)location.x;
                int y = (i / width) % height + (int)location.y;
                int z = i / (width * height) + (int)location.z;

                var random = randoms[i];

                int surfaceHeight = (int)MeshUtils.fBM(x, z, World.surfaceSettings.octaves,
                                                       World.surfaceSettings.scale, World.surfaceSettings.heightScale,
                                                       World.surfaceSettings.heightOffset);

            // other heights: stone, diamond, etc...
            int sandHeight = (int)MeshUtils.fBM(x, z, World.sandSettings.octaves,
                                                 World.sandSettings.scale, World.sandSettings.heightScale,
                                                 World.sandSettings.heightOffset);

            // other heights: stone, diamond, etc...
            int stoneHeight = (int)MeshUtils.fBM(x, z, World.stoneSettings.octaves,
                                                     World.stoneSettings.scale, World.stoneSettings.heightScale,
                                                     World.stoneSettings.heightOffset);

                // ...
                hData[i] = MeshUtils.BlockType.NOCRACK;

                if (y == 0)
                {
                    cData[i] = MeshUtils.BlockType.BEDROCK;
                    return;
                }

                // caves
                int digCave = (int)MeshUtils.fBM3D(x, y, z, World.caveSettings.octaves,
                                                   World.caveSettings.scale, World.caveSettings.heightScale,
                                                   World.caveSettings.heightOffset);

                if (digCave < World.caveSettings.probability)
                {
                    cData[i] = MeshUtils.BlockType.WATER;
                    return;
                }

                // *** FIX *** If y is exactly surfaceHeight => top block:
                if (surfaceHeight == y)
                {
                    // remove WOODBASE line
                    cData[i] = MeshUtils.BlockType.GRASSTOP; // or GRASSSIDE, but GRASSTOP helps the “top” look
                }
                else if (y < stoneHeight)
                {
                    cData[i] = MeshUtils.BlockType.STONE;
                }
                 else if (y < sandHeight)
                 {
                     cData[i] = MeshUtils.BlockType.SAND;
                 }
                 else if (y < surfaceHeight)
                {
                    cData[i] = MeshUtils.BlockType.DIRT;
                }
                else if (y < 9)
                {
                    cData[i] = MeshUtils.BlockType.WATER;
                }
                else
                    cData[i] = MeshUtils.BlockType.AIR;
            }
        }

        // The rest of World code (BuildWorld, Update, etc.) is typical.
        // ...
    


    // Start is called before the first frame update
    void Start()
    {
        loadingBar.maxValue = worldDimensions.x * worldDimensions.z;

        foreach (GameObject agent in agents)
        {
            if (agent != null)
                agent.SetActive(false);
        }

        surfaceSettings = new PerlinSettings(surface.heightScale, surface.scale,
                                             surface.octaves, surface.heightOffset, surface.probability);

        sandSettings = new PerlinSettings(sand.heightScale, sand.scale,
                                           sand.octaves, sand.heightOffset, sand.probability);

        stoneSettings = new PerlinSettings(stone.heightScale, stone.scale,
                                           stone.octaves, stone.heightOffset, stone.probability);

        diamondTSettings = new PerlinSettings(diamondT.heightScale, diamondT.scale,
                                              diamondT.octaves, diamondT.heightOffset, diamondT.probability);

        diamondBSettings = new PerlinSettings(diamondB.heightScale, diamondB.scale,
                                              diamondB.octaves, diamondB.heightOffset, diamondB.probability);

        caveSettings = new PerlinSettings(caves.heightScale, caves.scale,
                                          caves.octaves, caves.heightOffset, caves.DrawCutOff);

        if (highlightPrefab != null)
        {
            highlightObject = Instantiate(highlightPrefab);
            highlightObject.name = "BlockHighlight";
            highlightObject.SetActive(false);  // start hidden
        }

        if (loadFromFile)
            StartCoroutine(LoadWorldFromFile());
        else
            StartCoroutine(BuildWorld());
    }

    MeshUtils.BlockType buildType = MeshUtils.BlockType.DIRT;
    public void SetBuildType(int type)
    {
        buildType = (MeshUtils.BlockType)type;
    }

    Vector3Int FromFlat(int i)
    {
        return new Vector3Int(
            i % chunkDimensions.x,
            (i / chunkDimensions.x) % chunkDimensions.y,
            i / (chunkDimensions.x * chunkDimensions.y)
        );
    }

    public int ToFlat(Vector3Int v)
    {
        return v.x + chunkDimensions.x * (v.y + chunkDimensions.z * v.z);
    }

    public System.Tuple<Vector3Int, Vector3Int> GetWorldNeighbour(Vector3Int blockIndex, Vector3Int chunkIndex)
    {
        Chunk thisChunk = chunks[chunkIndex];
        int bx = blockIndex.x;
        int by = blockIndex.y;
        int bz = blockIndex.z;

        Vector3Int neighbour = chunkIndex;
        if (bx == chunkDimensions.x)
        {
            neighbour = new Vector3Int(
                (int)thisChunk.location.x + chunkDimensions.x,
                (int)thisChunk.location.y,
                (int)thisChunk.location.z
            );
            bx = 0;
        }
        else if (bx == -1)
        {
            neighbour = new Vector3Int(
                (int)thisChunk.location.x - chunkDimensions.x,
                (int)thisChunk.location.y,
                (int)thisChunk.location.z
            );
            bx = chunkDimensions.x - 1;
        }
        else if (by == chunkDimensions.y)
        {
            neighbour = new Vector3Int(
                (int)thisChunk.location.x,
                (int)thisChunk.location.y + chunkDimensions.y,
                (int)thisChunk.location.z
            );
            by = 0;
        }
        else if (by == -1)
        {
            neighbour = new Vector3Int(
                (int)thisChunk.location.x,
                (int)thisChunk.location.y - chunkDimensions.y,
                (int)thisChunk.location.z
            );
            by = chunkDimensions.y - 1;
        }
        else if (bz == chunkDimensions.z)
        {
            neighbour = new Vector3Int(
                (int)thisChunk.location.x,
                (int)thisChunk.location.y,
                (int)thisChunk.location.z + chunkDimensions.z
            );
            bz = 0;
        }
        else if (bz == -1)
        {
            neighbour = new Vector3Int(
                (int)thisChunk.location.x,
                (int)thisChunk.location.y,
                (int)thisChunk.location.z - chunkDimensions.z
            );
            bz = chunkDimensions.z - 1;
        }

        return new System.Tuple<Vector3Int, Vector3Int>(
            new Vector3Int(bx, by, bz),
            neighbour
        );
    }


    void Update()
    {
        // 1) Check if we toggle between first- and third-person with R
        if (Input.GetKeyDown(KeyCode.R))
        {
            usingThirdPerson = !usingThirdPerson;

            // Enable/disable the correct controllers on the fpc object
            var fpcController = fpc.GetComponent<UnityStandardAssets.Characters.FirstPerson.FirstPersonController>();
            var tpcController = fpc.GetComponent<ThirdPersonController>();

            if (fpcController != null) fpcController.enabled = !usingThirdPerson;
            if (tpcController != null) tpcController.enabled = usingThirdPerson;

            // Switch cameras
            if (fpc != null)
            {
                mCamera.SetActive(!usingThirdPerson);
                // Turn highlight on for first-person, off for third-person
                if (highlightPrefab != null) highlightPrefab.SetActive(!usingThirdPerson);

                // Example: Show sprite in third-person, hide in first-person
                // (if you have a SpriteRenderer on the TPC, do this):
                var spriteRenderer = tpcController.spriteTransform.gameObject.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null)
                    spriteRenderer.enabled =false;
                crosshair.enabled = true;
            }
            if (thirdPersonCamera != null)
            {

                thirdPersonCamera.SetActive(usingThirdPerson);
            }
        }

        // 2) If we are currently in third-person, update overhead camera, then return
        if (usingThirdPerson)
        {
            var tpcController = fpc.GetComponent<ThirdPersonController>();
            var spriteRenderer = tpcController.spriteTransform.gameObject.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                spriteRenderer.enabled = true;
            // Keep the overhead camera above the player
            thirdPersonCamera.transform.position = fpc.transform.position + new Vector3(0, 35, -15);

            // No highlighting or block modifications in third-person
            if (highlightPrefab != null) highlightPrefab.SetActive(false);

            // disable crosshair
            crosshair.enabled = false;
            return;
        }

        // 3) Otherwise (first-person mode) => do highlight & building logic
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // Raycast to see if we hit a chunk
        if (Physics.Raycast(ray, out hit, 10f))
        {
            Chunk thisChunk = hit.collider.gameObject.GetComponent<Chunk>();
            if (thisChunk != null)
            {
                // Figure out which block we’re pointing at
                Vector3 centerOfBlock = hit.point - hit.normal / 2.0f;
                // Convert to local chunk coords
                int bx = (int)(Mathf.Round(centerOfBlock.x) - thisChunk.location.x);
                int by = (int)(Mathf.Round(centerOfBlock.y) - thisChunk.location.y);
                int bz = (int)(Mathf.Round(centerOfBlock.z) - thisChunk.location.z);

                var blockNeighbour = GetWorldNeighbour(new Vector3Int(bx, by, bz),
                                                       Vector3Int.CeilToInt(thisChunk.location));

                thisChunk = chunks[blockNeighbour.Item2];
                Vector3Int localBlockIndex = blockNeighbour.Item1;

                // Convert local coords -> world coords
                Vector3Int worldBlockPos = new Vector3Int(
                    (int)thisChunk.location.x + localBlockIndex.x,
                    (int)thisChunk.location.y + localBlockIndex.y,
                    (int)thisChunk.location.z + localBlockIndex.z
                );

                // Show highlight at that block
                if (highlightObject != null)
                {
                    highlightObject.transform.position = worldBlockPos;
                    highlightObject.SetActive(true);
                }
            }
            else
            {
                // We hit something that isn't a chunk => hide highlight
                if (highlightObject != null)
                    highlightObject.SetActive(false);
            }
        }
        else
        {
            // Missed entirely => hide highlight
            if (highlightObject != null)
                highlightObject.SetActive(false);
        }

        // 4) Check mouse for dig/place; re-use the same ray so we don't do another
        if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && Physics.Raycast(ray, out hit, 10f))
        {
            Vector3 hitBlock = Vector3.zero;
            // If left-click => dig; if right-click => place
            if (Input.GetMouseButtonDown(0))
                hitBlock = hit.point - hit.normal / 2.0f;
            else
                hitBlock = hit.point + hit.normal / 2.0f;

            Chunk thisChunk = hit.collider.gameObject.GetComponent<Chunk>();
            if (thisChunk != null)
            {
                // Convert that hitBlock to local coords
                int bx = (int)(Mathf.Round(hitBlock.x) - thisChunk.location.x);
                int by = (int)(Mathf.Round(hitBlock.y) - thisChunk.location.y);
                int bz = (int)(Mathf.Round(hitBlock.z) - thisChunk.location.z);

                var blockNeighbour = GetWorldNeighbour(
                    new Vector3Int(bx, by, bz),
                    Vector3Int.CeilToInt(thisChunk.location)
                );

                thisChunk = chunks[blockNeighbour.Item2];
                int i = ToFlat(blockNeighbour.Item1);
                thisChunk.healthData[i]++;
                // Left-click => remove
                if (Input.GetMouseButtonDown(0))
                {
                    
                    // Dig logic
                    if (MeshUtils.blockTypeHealth[(int)thisChunk.chunkData[i]] != -1)
                    {
                        if (thisChunk.healthData[i] == MeshUtils.BlockType.NOCRACK)
                            StartCoroutine(HealBlock(thisChunk, i));

                       

                        // If block is fully destroyed, set to AIR and let block above drop
                        if (thisChunk.healthData[i] ==
                            MeshUtils.BlockType.NOCRACK + MeshUtils.blockTypeHealth[(int)thisChunk.chunkData[i]])
                        {
                            thisChunk.chunkData[i] = MeshUtils.BlockType.AIR;

                            Vector3Int nBlock = FromFlat(i);
                            var neighbourBlock = GetWorldNeighbour(
                                new Vector3Int(nBlock.x, nBlock.y + 1, nBlock.z),
                                Vector3Int.CeilToInt(thisChunk.location)
                            );
                            Vector3Int blockPos = neighbourBlock.Item1;
                            int neighbourBlockIndex = ToFlat(blockPos);
                            Chunk neighbourChunk = chunks[neighbourBlock.Item2];
                            StartCoroutine(Drop(neighbourChunk, neighbourBlockIndex));
                        }
                    }
                }
                else
                {
                    // Right-click => place
                    thisChunk.chunkData[i] = buildType;
                    thisChunk.healthData[i] = MeshUtils.BlockType.NOCRACK;
                    StartCoroutine(Drop(thisChunk, i));
                }

                // Redraw after changes
                RedrawChunk(thisChunk);
            }

            // Trigger any extra updates if needed
            UpdateWorld();
        }
    }


    public void RedrawChunk(Chunk c)
    {
        DestroyImmediate(c.GetComponent<MeshFilter>());
        DestroyImmediate(c.GetComponent<MeshRenderer>());
        DestroyImmediate(c.GetComponent<Collider>());
        c.CreateChunk(chunkDimensions, c.location, false);
    }

    WaitForSeconds threeSeconds = new WaitForSeconds(3);

    public IEnumerator HealBlock(Chunk c, int blockIndex)
    {
        yield return threeSeconds;
        if (c.chunkData[blockIndex] != MeshUtils.BlockType.AIR)
        {
            c.healthData[blockIndex] = MeshUtils.BlockType.NOCRACK;
            RedrawChunk(c);
        }
    }

    WaitForSeconds dropDelay = new WaitForSeconds(0.1f);

    // Example: always let water drop if there's air below.
    public IEnumerator Drop(Chunk c, int blockIndex, int strength = 3)
    {
        // 1) Determine block type
        BlockType blockType = c.chunkData[blockIndex];

        // 2) If block is NOT water and NOT canDrop => no drop
        //    (Because water is forced to drop if there's air)
        if (blockType != BlockType.WATER && !MeshUtils.canDrop.Contains(blockType))
            yield break;

        // Wait a short delay before we do anything (optional)
        yield return dropDelay;

        // 3) Loop for repeated falling (gravity)
        while (true)
        {
            // Convert flatten index => local (x,y,z)
            Vector3Int localPos = FromFlat(blockIndex);

            // Check the block below
            var belowLookup = GetWorldNeighbour(
                new Vector3Int(localPos.x, localPos.y - 1, localPos.z),
                Vector3Int.CeilToInt(c.location)
            );
            Vector3Int belowLocalPos = belowLookup.Item1;
            Vector3Int belowChunkKey = belowLookup.Item2;

            // If outside world or chunk not loaded
            if (!chunks.TryGetValue(belowChunkKey, out Chunk belowChunk))
                yield break;

            int belowIndex = ToFlat(belowLocalPos);

            // 3a) If below is AIR, we move down
            if (belowChunk.chunkData[belowIndex] == BlockType.AIR)
            {
                // Move the block down
                belowChunk.chunkData[belowIndex] = blockType;
                belowChunk.healthData[belowIndex] = BlockType.NOCRACK;

                // Clear the old position
                c.chunkData[blockIndex] = BlockType.AIR;
                c.healthData[blockIndex] = BlockType.NOCRACK;

                // Also let the block above drop
                var aboveLookup = GetWorldNeighbour(
                    new Vector3Int(localPos.x, localPos.y + 1, localPos.z),
                    Vector3Int.CeilToInt(c.location)
                );
                Vector3Int aboveLocalPos = aboveLookup.Item1;
                Vector3Int aboveChunkKey = aboveLookup.Item2;
                if (chunks.TryGetValue(aboveChunkKey, out Chunk aboveChunk))
                {
                    int aboveIndex = ToFlat(aboveLocalPos);
                    // Start a coroutine to drop the block above as well
                    StartCoroutine(Drop(aboveChunk, aboveIndex));
                }

                // Redraw changed chunks (or queue them for redraw)
                RedrawChunk(c);
                if (belowChunk != c)
                    RedrawChunk(belowChunk);

                // Update references so we keep dropping
                c = belowChunk;
                blockIndex = belowIndex;

                // If you want to limit total drops, decrement strength
                strength--;
                if (strength <= 0)
                    yield break;

                // Wait before next iteration
                yield return dropDelay;
            }
            // 3b) If we can't fall, check if the block can flow sideways
            else if (MeshUtils.canFlow.Contains(blockType))
            {
                // e.g., water or sand that can flow sideways
                int newStrength = strength - 1; // or strength as is

                // Flow in four directions
                FlowIntoNeighbour(localPos, Vector3Int.CeilToInt(c.location),
                                  new Vector3Int(1, 0, 0), newStrength);
                FlowIntoNeighbour(localPos, Vector3Int.CeilToInt(c.location),
                                  new Vector3Int(-1, 0, 0), newStrength);
                FlowIntoNeighbour(localPos, Vector3Int.CeilToInt(c.location),
                                  new Vector3Int(0, 0, 1), newStrength);
                FlowIntoNeighbour(localPos, Vector3Int.CeilToInt(c.location),
                                  new Vector3Int(0, 0, -1), newStrength);

                // Redraw if needed
                RedrawChunk(c);
                // Then stop
                yield break;
            }
            else
            {
                // Can't drop, can't flow => done
                yield break;
            }
        }
    }




    public void FlowIntoNeighbour(Vector3Int blockPosition,
                                  Vector3Int chunkPosition,
                                  Vector3Int neighbourDirection,
                                  int strength)
    {
        strength--;
        if (strength <= 0) return;

        Vector3Int neighbourPosition = blockPosition + neighbourDirection;
        var neighbourBlock = GetWorldNeighbour(neighbourPosition, chunkPosition);
        Vector3Int block = neighbourBlock.Item1;
        int neighbourBlockIndex = ToFlat(block);
        Chunk neighbourChunk = chunks[neighbourBlock.Item2];
        if (neighbourChunk == null) return;

        if (neighbourChunk.chunkData[neighbourBlockIndex] == MeshUtils.BlockType.AIR)
        {
            neighbourChunk.chunkData[neighbourBlockIndex] =
                chunks[chunkPosition].chunkData[ToFlat(blockPosition)];
            neighbourChunk.healthData[neighbourBlockIndex] =
                MeshUtils.BlockType.NOCRACK;
            RedrawChunk(neighbourChunk);
            StartCoroutine(Drop(neighbourChunk, neighbourBlockIndex, strength--));
        }
    }

    void BuildChunkColumn(int x, int z, bool meshEnabled = true)
    {
        for (int y = 0; y < worldDimensions.y; y++)
        {
            Vector3Int pos = new Vector3Int(x, y * chunkDimensions.y, z);

            // If we do NOT already have this chunk, create from scratch:
            if (!chunkChecker.Contains(pos))
            {
                GameObject chunkObj = Instantiate(chunkPrefab);
                chunkObj.name = $"Chunk_{pos.x}_{pos.y}_{pos.z}";
                Chunk c = chunkObj.GetComponent<Chunk>();

                // PROBLEM (by default): c.CreateChunk(chunkDimensions, pos);
                // That calls c.CreateChunk(..., true) => re‐perlin

                // FIX: pass 'true' only if chunk didn't exist before
                c.CreateChunk(chunkDimensions, pos, true);

                chunkChecker.Add(pos);
                chunks.Add(pos, c);
            }

            // If it DOES exist, do not re‐create the blocks:
            // (You might already skip creation entirely, so no problem.)

            // Then set meshRendererSolid.enabled = meshEnabled, etc.
            if (chunks[pos].meshRendererSolid != null)
                chunks[pos].meshRendererSolid.enabled = meshEnabled;
            if (chunks[pos].meshRendererFluid != null)
                chunks[pos].meshRendererFluid.enabled = meshEnabled;
        }
        chunkColumns.Add(new Vector2Int(x, z));
    }



    IEnumerator BuildExtraWorld()
    {
        int zEnd = worldDimensions.z + extraWorldDimensions.z;
        int zStart = worldDimensions.z - 1;
        int xEnd = worldDimensions.x + extraWorldDimensions.x;
        int xStart = worldDimensions.x - 1;

        for (int z = zStart; z < zEnd; z++)
        {
            for (int x = 0; x < xEnd; x++)
            {
                BuildChunkColumn(x * chunkDimensions.x, z * chunkDimensions.z, false);
                yield return null;
            }
        }

        for (int z = 0; z < zEnd; z++)
        {
            for (int x = xStart; x < xEnd; x++)
            {
                BuildChunkColumn(x * chunkDimensions.x, z * chunkDimensions.z, false);
                yield return null;
            }
        }
    }

    IEnumerator BuildWorld()
    {
        for (int z = 0; z < worldDimensions.z; z++)
        {
            for (int x = 0; x < worldDimensions.x; x++)
            {
                BuildChunkColumn(x * chunkDimensions.x, z * chunkDimensions.z);
                loadingBar.value++;
                yield return null;
            }
        }

        mCamera.SetActive(false);

        int xpos = (worldDimensions.x * chunkDimensions.x) / 2;
        int zpos = (worldDimensions.z * chunkDimensions.z) / 2;
        int ypos = (int)MeshUtils.fBM(
            xpos, zpos,
            surfaceSettings.octaves,
            surfaceSettings.scale,
            surfaceSettings.heightScale,
            surfaceSettings.heightOffset
        ) + 10;

        fpc.transform.position = new Vector3Int(xpos, ypos, zpos);
        thirdPersonCamera.transform.position = new Vector3(xpos, 35, zpos);
        loadingBar.gameObject.SetActive(false);
        fpc.SetActive(true);
        lastBuildPosition = Vector3Int.CeilToInt(fpc.transform.position);

        StartCoroutine(BuildCoordinator());
        StartCoroutine(UpdateWorld());
        StartCoroutine(BuildExtraWorld());

        // --- NEW: after everything is set up, enable the agent ---
        foreach(GameObject agent in agents)
        {
            if (agent != null)
                agent.SetActive(true);
        }
    }

    WaitForSeconds wfs = new WaitForSeconds(0.5f);
    public IEnumerator UpdateWorld()
    {
        while (true)
        {
            if ((lastBuildPosition - fpc.transform.position).magnitude > chunkDimensions.x)
            {
                lastBuildPosition = Vector3Int.CeilToInt(fpc.transform.position);
                int posx = (int)(fpc.transform.position.x / chunkDimensions.x) * chunkDimensions.x;
                int posz = (int)(fpc.transform.position.z / chunkDimensions.z) * chunkDimensions.z;
                buildQueue.Enqueue(BuildRecursiveWorld(posx, posz, drawRadius));
                buildQueue.Enqueue(HideColumns(posx, posz));
            }
            yield return wfs;
        }
    }

    public void HideChunkColumn(int x, int z)
    {
        for (int y = 0; y < worldDimensions.y; y++)
        {
            Vector3Int pos = new Vector3Int(x, y * chunkDimensions.y, z);
            if (chunkChecker.Contains(pos))
            {
                if (chunks[pos].meshRendererSolid != null)
                    chunks[pos].meshRendererSolid.enabled = false;
                if (chunks[pos].meshRendererFluid != null)
                    chunks[pos].meshRendererFluid.enabled = false;
            }
        }
    }

    IEnumerator HideColumns(int x, int z)
    {
        Vector2Int fpcPos = new Vector2Int(x, z);
        foreach (Vector2Int cc in chunkColumns)
        {
            if ((cc - fpcPos).magnitude >= drawRadius * chunkDimensions.x)
            {
                HideChunkColumn(cc.x, cc.y);
            }
        }
        yield return null;
    }

    IEnumerator BuildRecursiveWorld(int x, int z, int rad)
    {
        int nextrad = rad - 1;
        if (rad <= 0) yield break;

        BuildChunkColumn(x, z + chunkDimensions.z);
        buildQueue.Enqueue(BuildRecursiveWorld(x, z + chunkDimensions.z, nextrad));
        yield return null;

        BuildChunkColumn(x, z - chunkDimensions.z);
        buildQueue.Enqueue(BuildRecursiveWorld(x, z - chunkDimensions.z, nextrad));
        yield return null;

        BuildChunkColumn(x + chunkDimensions.x, z);
        buildQueue.Enqueue(BuildRecursiveWorld(x + chunkDimensions.x, z, nextrad));
        yield return null;

        BuildChunkColumn(x - chunkDimensions.x, z);
        buildQueue.Enqueue(BuildRecursiveWorld(x - chunkDimensions.x, z, nextrad));
        yield return null;
    }

    /// <summary>
    /// Return the BlockType at a **world-space** coordinate (x,y,z).
    /// We find which chunk it belongs to, then index its chunkData.
    /// </summary>
    public BlockType GetBlockType(int x, int y, int z)
    {
        // If outside the total world, fallback to AIR
        int totalX = worldDimensions.x * chunkDimensions.x;
        int totalY = worldDimensions.y * chunkDimensions.y;
        int totalZ = worldDimensions.z * chunkDimensions.z;

        if (x < 0 || x >= totalX) return BlockType.AIR;
        if (y < 0 || y >= totalY) return BlockType.AIR;
        if (z < 0 || z >= totalZ) return BlockType.AIR;

        // figure out which chunk
        int chunkX = (x / chunkDimensions.x) * chunkDimensions.x;
        int chunkY = (y / chunkDimensions.y) * chunkDimensions.y;
        int chunkZ = (z / chunkDimensions.z) * chunkDimensions.z;
        Vector3Int chunkPos = new Vector3Int(chunkX, chunkY, chunkZ);

        if (!chunks.ContainsKey(chunkPos))
        {
            return BlockType.AIR; // if no chunk, assume air
        }

        Chunk c = chunks[chunkPos];
        // local coords inside the chunk
        int lx = x % chunkDimensions.x;
        int ly = y % chunkDimensions.y;
        int lz = z % chunkDimensions.z;

        int flatIndex = lx + chunkDimensions.x * (ly + chunkDimensions.z * lz);
        return c.chunkData[flatIndex];
    }

    /// <summary>
    /// Very naive "is it lit?" check. If there's any solid block above (x, checkY, z),
    /// it's shade. Otherwise it's lit. We'll define "solid" as not AIR/WATER.
    /// </summary>
    public bool IsLit(int x, int y, int z)
    {
        int totalY = worldDimensions.y * chunkDimensions.y;
        for (int checkY = y + 1; checkY < totalY; checkY++)
        {
            BlockType bt = GetBlockType(x, checkY, z);
            if (bt != BlockType.AIR && bt != BlockType.WATER)
            {
                return false;
            }
        }
        return true;
    }

    public bool InBounds(int x, int y, int z)
    {
        // 1) Calculate total block-size of the world in each dimension
        int totalX = (worldDimensions.x + extraWorldDimensions.x) * chunkDimensions.x;
        int totalZ = (worldDimensions.z + extraWorldDimensions.z) * chunkDimensions.z;

        // 2) Calculate the total Y height in blocks, then double it
        int totalY = (worldDimensions.y + extraWorldDimensions.y) * chunkDimensions.y;
        totalY *= 2; // If you want to allow up to double the normal top

        // 3) Check the agent's x,y,z (in blocks) against these bounds
        if (x < 0 || x >= totalX) return false;
        if (y < 0 || y >= totalY) return false;
        if (z < 0 || z >= totalZ) return false;

        return true;
    }

    public void ResetEnvironment()
    {
        // Example: if you want to rebuild the world from scratch
        // or reposition chunks, or simply do nothing.
        // Possibly call StartCoroutine(BuildWorld()) again, etc.
        Debug.Log("Resetting environment...");
    }

    public float GetValueAt(int x, int z)
    {
        // Example: If you just want to re-use the old ComputeLocationScore logic,
        // you can do a simpler approach, e.g. measure the block y-height or block composition.
        // For demonstration, let’s do an extremely naive: the higher the ground, the higher the value.
        int topY = 0;
        for (int y = worldDimensions.y * chunkDimensions.y - 1; y >= 0; y--)
        {
            MeshUtils.BlockType b = GetBlockType(x, y, z);
            if (b != MeshUtils.BlockType.AIR && b != MeshUtils.BlockType.WATER)
            {
                topY = y;
                break;
            }
        }
        // Return "topY" as a rough "value"
        return topY;
    }

    public float GetTerrainHeight(float x, float z)
    {
        // If you want an approximate integer approach:
        int xx = Mathf.FloorToInt(x);
        int zz = Mathf.FloorToInt(z);

        int topY = 0;
        for (int y = worldDimensions.y * chunkDimensions.y - 1; y >= 0; y--)
        {
            MeshUtils.BlockType b = GetBlockType(xx, y, zz);
            if (b != MeshUtils.BlockType.AIR && b != MeshUtils.BlockType.WATER)
            {
                topY = y + 1; // physically above the top block
                break;
            }
        }
        return topY;
    }

    public Vector2Int GetGridPosFromWorld(Vector3 worldPos)
    {
        // Convert from world-space coords to block coords
        int bx = Mathf.FloorToInt(worldPos.x / chunkDimensions.x);
        int bz = Mathf.FloorToInt(worldPos.z / chunkDimensions.z);
        // or simpler:
        // int bx = Mathf.RoundToInt(worldPos.x);
        // int bz = Mathf.RoundToInt(worldPos.z);
        return new Vector2Int(bx, bz);
    }

    public void PlantSeedAt(Vector2Int pos)
    {
        
        // Stub: You might set a block to WOOD or place a "planted" marker, etc.
        Debug.Log($"Planted seed at {pos}");
    }


}
