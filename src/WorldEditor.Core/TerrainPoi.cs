namespace WorldEditor.Core;

public sealed record TerrainPoi(
    Guid Id,
    string Name,
    float XMetres,
    float ZMetres,
    TerrainPoiKind Kind,
    string Notes);

