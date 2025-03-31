public static class BlockScoring
{
    public static float GetBlockScore(MeshUtils.BlockType b)
    {
        switch (b)
        {
            case MeshUtils.BlockType.STONE: return 0f;
            case MeshUtils.BlockType.SAND: return 1f;
            case MeshUtils.BlockType.DIRT: return 3f;
            case MeshUtils.BlockType.GRASSSIDE: return 5f;
            case MeshUtils.BlockType.WOOD:
            case MeshUtils.BlockType.WOODBASE: return 1f;
            case MeshUtils.BlockType.GRASSTOP: return 8f;
            case MeshUtils.BlockType.DIAMOND: return 0f;     // Possibly reward diamonds
            case MeshUtils.BlockType.WATER: return 10f;    // Negative so water doesn't inflate scores
            default: return 0f;
        }
    }
}