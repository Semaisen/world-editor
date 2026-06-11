namespace WorldEditor.Core;

public sealed record TerrainPath(
    Guid Id,
    string Name,
    TerrainPathKind Kind,
    float WidthMetres,
    IReadOnlyList<TerrainPathPoint> Points);

