using static MeshUtils;

public static class BlockScoring
{
    public static int GetBlockScore(BlockType type)
    {
        switch (type)
        {
            case BlockType.SAND: return 1;
            case BlockType.DIRT: return 2;
            case BlockType.STONE: return 1;
            case BlockType.GRASSTOP: return 5;
            case BlockType.GRASSSIDE: return 3;
            case BlockType.WATER: return 5; // but not standable
            case BlockType.AIR:
                return 0; // *** EXPLICIT: Air => 0
            default:
                return 0;
        }
    }
}
