namespace WorldEditor.Core;

public readonly record struct TerrainCoord(int X, int Z)
{
    public override string ToString() => $"({X}, {Z})";
}
