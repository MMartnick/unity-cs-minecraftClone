using UnityEngine; // So we can reference MeshUtils.BlockType

/// <summary>
/// Each voxel has a density + a block type (for texturing/material).
/// density > 0 => solid, density < 0 => air
/// </summary>
public struct VoxelData
{
    public float density;
    public MeshUtils.BlockType blockType;

    public VoxelData(float density, MeshUtils.BlockType type)
    {
        this.density = density;
        this.blockType = type;
    }
}
