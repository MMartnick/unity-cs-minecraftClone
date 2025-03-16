public static class BlockScoring
{
    public static float GetBlockScore(MeshUtils.BlockType b)
    {
        switch (b)
        {
            case MeshUtils.BlockType.STONE: return 0.5f;
            case MeshUtils.BlockType.DIRT: return 0.5f;
            case MeshUtils.BlockType.GRASSTOP: return 4f;
            case MeshUtils.BlockType.GRASSSIDE: return 2.5f;
            case MeshUtils.BlockType.WATER: return 4f;
            case MeshUtils.BlockType.SAND: return 0.5f;
            case MeshUtils.BlockType.DIAMOND: return 0f;
            case MeshUtils.BlockType.WOOD:
            case MeshUtils.BlockType.WOODBASE: return 0f;
            // etc...
            default: return 0f;
        }
    }
}
