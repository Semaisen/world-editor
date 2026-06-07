using System.Buffers.Binary;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WorldEditor.Core;

public static class TerrainProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Save(TerrainTile tile, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(tile);
        Directory.CreateDirectory(projectDirectory);

        var metadata = tile.ToMetadata();
        File.WriteAllText(Path.Combine(projectDirectory, "terrain.json"), JsonSerializer.Serialize(metadata, JsonOptions));
        WriteHeightmap(tile, Path.Combine(projectDirectory, metadata.Heightmap));
        WriteAlbedo(tile, Path.Combine(projectDirectory, metadata.Albedo));
    }

    public static TerrainTile Load(string projectDirectory)
    {
        var metadataPath = Path.Combine(projectDirectory, "terrain.json");
        var metadata = JsonSerializer.Deserialize<TerrainMetadata>(File.ReadAllText(metadataPath))
            ?? throw new InvalidDataException("terrain.json could not be parsed.");

        var tile = new TerrainTile(metadata.TerrainWidthMetres, metadata.TerrainDepthMetres, metadata.ResolutionMetres);
        if (tile.HeightmapWidth != metadata.HeightmapWidth || tile.HeightmapHeight != metadata.HeightmapHeight)
        {
            throw new InvalidDataException("Terrain dimensions do not match metadata sample counts.");
        }

        ReadHeightmap(tile, Path.Combine(projectDirectory, metadata.Heightmap));
        ReadAlbedo(tile, Path.Combine(projectDirectory, metadata.Albedo));
        return tile;
    }

    public static void WriteHeightmap(TerrainTile tile, string path)
    {
        var bytes = new byte[tile.Heights.Length * sizeof(float)];
        for (var i = 0; i < tile.Heights.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)), tile.Heights[i]);
        }

        File.WriteAllBytes(path, bytes);
    }

    public static void ReadHeightmap(TerrainTile tile, string path)
    {
        var bytes = File.ReadAllBytes(path);
        var expectedBytes = tile.Heights.Length * sizeof(float);
        if (bytes.Length != expectedBytes)
        {
            throw new InvalidDataException($"Expected {expectedBytes} heightmap bytes, found {bytes.Length}.");
        }

        for (var i = 0; i < tile.Heights.Length; i++)
        {
            tile.Heights[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
        }
    }

    public static void WriteAlbedo(TerrainTile tile, string path)
    {
        using var image = Image.LoadPixelData<Rgba32>(tile.Albedo, tile.HeightmapWidth, tile.HeightmapHeight);
        image.SaveAsPng(path);
    }

    public static void ReadAlbedo(TerrainTile tile, string path)
    {
        using var image = Image.Load<Rgba32>(path);
        if (image.Width != tile.HeightmapWidth || image.Height != tile.HeightmapHeight)
        {
            throw new InvalidDataException("Albedo dimensions do not match terrain dimensions.");
        }

        image.CopyPixelDataTo(tile.Albedo);
    }
}
