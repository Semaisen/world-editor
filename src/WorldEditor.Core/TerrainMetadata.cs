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
}
