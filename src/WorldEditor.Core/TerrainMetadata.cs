using System.Text.Json.Serialization;

namespace WorldEditor.Core;

public sealed record TerrainMetadata
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = "metres";

    [JsonPropertyName("terrain_width_m")]
    public float TerrainWidthMetres { get; init; }

    [JsonPropertyName("terrain_depth_m")]
    public float TerrainDepthMetres { get; init; }

    [JsonPropertyName("resolution_m")]
    public float ResolutionMetres { get; init; }

    [JsonPropertyName("heightmap_width")]
    public int HeightmapWidth { get; init; }

    [JsonPropertyName("heightmap_height")]
    public int HeightmapHeight { get; init; }

    [JsonPropertyName("height_format")]
    public string HeightFormat { get; init; } = "float32";

    [JsonPropertyName("albedo")]
    public string Albedo { get; init; } = "albedo.png";

    [JsonPropertyName("heightmap")]
    public string Heightmap { get; init; } = "heightmap.bin";

    [JsonPropertyName("tiles")]
    public List<TerrainTileMetadata>? Tiles { get; init; }

    [JsonPropertyName("pois")]
    public List<TerrainPoiMetadata>? Pois { get; init; }

    [JsonPropertyName("paths")]
    public List<TerrainPathMetadata>? Paths { get; init; }
}

public sealed record TerrainTileMetadata
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("z")]
    public int Z { get; init; }

    [JsonPropertyName("albedo")]
    public string Albedo { get; init; } = "";

    [JsonPropertyName("heightmap")]
    public string Heightmap { get; init; } = "";
}

public sealed record TerrainPoiMetadata
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("x_m")]
    public float XMetres { get; init; }

    [JsonPropertyName("z_m")]
    public float ZMetres { get; init; }

    [JsonPropertyName("kind")]
    public TerrainPoiKind Kind { get; init; }

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = "";
}

public sealed record TerrainPathMetadata
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("kind")]
    public TerrainPathKind Kind { get; init; }

    [JsonPropertyName("width_m")]
    public float WidthMetres { get; init; }

    [JsonPropertyName("points")]
    public List<TerrainPathPointMetadata> Points { get; init; } = [];
}

public sealed record TerrainPathPointMetadata
{
    [JsonPropertyName("x_m")]
    public float XMetres { get; init; }

    [JsonPropertyName("z_m")]
    public float ZMetres { get; init; }
}
