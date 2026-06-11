namespace WorldEditor.Core;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class GodotExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    static GodotExporter()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static void Export(TerrainTile tile, string exportDirectory)
    {
        ArgumentNullException.ThrowIfNull(tile);
        Export(TerrainWorld.FromSingleTile(tile), exportDirectory);
    }

    public static void Export(TerrainWorld world, string exportDirectory)
    {
        ArgumentNullException.ThrowIfNull(world);
        Directory.CreateDirectory(exportDirectory);
        Directory.CreateDirectory(Path.Combine(exportDirectory, "tiles"));

        TerrainProjectStore.Save(world, exportDirectory);
        WritePois(world, Path.Combine(exportDirectory, "pois.json"));
        foreach (var (coord, tile) in world.Tiles)
        {
            var offsetX = coord.X * tile.WidthMetres;
            var offsetZ = coord.Z * tile.DepthMetres;
            GltfWriter.WriteTerrainGlb(tile, Path.Combine(exportDirectory, "tiles", $"tile_{coord.X}_{coord.Z}_mesh.glb"), offsetX, offsetZ);
        }

        if (world.TileCount == 1 && world.TryGetTile(new TerrainCoord(0, 0), out var originTile) && originTile is not null)
        {
            TerrainProjectStore.WriteHeightmap(originTile, Path.Combine(exportDirectory, "heightmap.bin"));
            TerrainProjectStore.WriteAlbedo(originTile, Path.Combine(exportDirectory, "albedo.png"));
            GltfWriter.WriteTerrainGlb(originTile, Path.Combine(exportDirectory, "terrain_mesh.glb"));
        }
    }

    private static void WritePois(TerrainWorld world, string path)
    {
        var pois = world.Pois.Select(poi => new ExportPoi(
            poi.Id,
            poi.Name,
            poi.Kind,
            poi.Notes,
            new ExportPosition(poi.XMetres, SampleWorldHeight(world, poi.XMetres, poi.ZMetres), poi.ZMetres)));

        File.WriteAllText(path, JsonSerializer.Serialize(pois, JsonOptions));
    }

    private static float SampleWorldHeight(TerrainWorld world, float worldX, float worldZ)
    {
        var template = world.Tiles.Values.First();
        var coord = new TerrainCoord(
            (int)MathF.Floor(worldX / template.WidthMetres),
            (int)MathF.Floor(worldZ / template.DepthMetres));
        if (!world.TryGetTile(coord, out var tile) || tile is null) return 0.0f;

        var localX = Math.Clamp(worldX - coord.X * tile.WidthMetres, 0.0f, tile.WidthMetres);
        var localZ = Math.Clamp(worldZ - coord.Z * tile.DepthMetres, 0.0f, tile.DepthMetres);
        var sampleX = Math.Clamp((int)MathF.Round(localX / tile.ResolutionMetres), 0, tile.HeightmapWidth - 1);
        var sampleZ = Math.Clamp((int)MathF.Round(localZ / tile.ResolutionMetres), 0, tile.HeightmapHeight - 1);
        return tile.GetHeight(sampleX, sampleZ);
    }

    private sealed record ExportPoi(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("kind")] TerrainPoiKind Kind,
        [property: JsonPropertyName("notes")] string Notes,
        [property: JsonPropertyName("position_m")] ExportPosition Position);

    private sealed record ExportPosition(
        [property: JsonPropertyName("x")] float X,
        [property: JsonPropertyName("y")] float Y,
        [property: JsonPropertyName("z")] float Z);
}
