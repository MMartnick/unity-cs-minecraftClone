using static MeshUtils;

public static class BlockScoring
{
    public static int GetBlockScore(BlockType type)
    {
        switch (type)
        {
            case BlockType.SAND: return 1;
            case BlockType.DIRT: return 1;
            case BlockType.STONE: return 0;
            case BlockType.GRASSTOP: return 2;
            case BlockType.WATER: return 3; // but not standable
            default: return 0;
        }
    }
}
