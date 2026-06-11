using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WorldEditor.Core;

public static class TerrainProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    static TerrainProjectStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static void Save(TerrainTile tile, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(tile);
        Directory.CreateDirectory(projectDirectory);

        var metadata = tile.ToMetadata();
        File.WriteAllText(Path.Combine(projectDirectory, "terrain.json"), JsonSerializer.Serialize(metadata, JsonOptions));
        WriteHeightmap(tile, Path.Combine(projectDirectory, metadata.Heightmap));
        WriteAlbedo(tile, Path.Combine(projectDirectory, metadata.Albedo));
    }

    public static void Save(TerrainWorld world, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(world);
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(Path.Combine(projectDirectory, "tiles"));

        var template = world.Tiles.Values.First();
        var metadata = template.ToMetadata() with
        {
            Tiles = world.Tiles
                .OrderBy(tile => tile.Key.X)
                .ThenBy(tile => tile.Key.Z)
                .Select(tile => CreateTileMetadata(tile.Key))
                .ToList(),
            Pois = world.Pois.Select(CreatePoiMetadata).ToList(),
            Paths = world.Paths.Select(CreatePathMetadata).ToList()
        };
        File.WriteAllText(Path.Combine(projectDirectory, "terrain.json"), JsonSerializer.Serialize(metadata, JsonOptions));

        foreach (var (coord, tile) in world.Tiles)
        {
            var tileMetadata = CreateTileMetadata(coord);
            WriteHeightmap(tile, Path.Combine(projectDirectory, tileMetadata.Heightmap));
            WriteAlbedo(tile, Path.Combine(projectDirectory, tileMetadata.Albedo));
        }
    }

    public static TerrainTile Load(string projectDirectory) => LoadWorld(projectDirectory).GetTile(new TerrainCoord(0, 0));

    public static TerrainWorld LoadWorld(string projectDirectory)
    {
        var metadataPath = Path.Combine(projectDirectory, "terrain.json");
        var metadata = JsonSerializer.Deserialize<TerrainMetadata>(File.ReadAllText(metadataPath), JsonOptions)
            ?? throw new InvalidDataException("terrain.json could not be parsed.");

        if (metadata.Tiles is null || metadata.Tiles.Count == 0)
        {
            return TerrainWorld.FromTiles(
                new Dictionary<TerrainCoord, TerrainTile>
                {
                    [new TerrainCoord(0, 0)] = LoadTile(projectDirectory, metadata, metadata.Heightmap, metadata.Albedo)
                },
                LoadPois(metadata),
                LoadPaths(metadata));
        }

        var tiles = new Dictionary<TerrainCoord, TerrainTile>();
        foreach (var tileMetadata in metadata.Tiles)
        {
            var coord = new TerrainCoord(tileMetadata.X, tileMetadata.Z);
            tiles.Add(coord, LoadTile(projectDirectory, metadata, tileMetadata.Heightmap, tileMetadata.Albedo));
        }

        return TerrainWorld.FromTiles(tiles, LoadPois(metadata), LoadPaths(metadata));
    }

    private static TerrainTile LoadTile(string projectDirectory, TerrainMetadata metadata, string heightmap, string albedo)
    {
        var tile = new TerrainTile(metadata.TerrainWidthMetres, metadata.TerrainDepthMetres, metadata.ResolutionMetres);
        if (tile.HeightmapWidth != metadata.HeightmapWidth || tile.HeightmapHeight != metadata.HeightmapHeight)
        {
            throw new InvalidDataException("Terrain dimensions do not match metadata sample counts.");
        }

        ReadHeightmap(tile, Path.Combine(projectDirectory, heightmap));
        ReadAlbedo(tile, Path.Combine(projectDirectory, albedo));
        return tile;
    }

    private static TerrainTileMetadata CreateTileMetadata(TerrainCoord coord)
    {
        var prefix = $"tiles/tile_{coord.X}_{coord.Z}";
        return new TerrainTileMetadata
        {
            X = coord.X,
            Z = coord.Z,
            Heightmap = $"{prefix}_heightmap.bin",
            Albedo = $"{prefix}_albedo.png"
        };
    }

    private static TerrainPoiMetadata CreatePoiMetadata(TerrainPoi poi) => new()
    {
        Id = poi.Id,
        Name = poi.Name,
        XMetres = poi.XMetres,
        ZMetres = poi.ZMetres,
        Kind = poi.Kind,
        Notes = poi.Notes
    };

    private static IEnumerable<TerrainPoi> LoadPois(TerrainMetadata metadata)
    {
        if (metadata.Pois is null) yield break;

        foreach (var poi in metadata.Pois)
        {
            yield return new TerrainPoi(
                poi.Id == Guid.Empty ? Guid.NewGuid() : poi.Id,
                poi.Name,
                poi.XMetres,
                poi.ZMetres,
                poi.Kind,
                poi.Notes);
        }
    }

    private static TerrainPathMetadata CreatePathMetadata(TerrainPath path) => new()
    {
        Id = path.Id,
        Name = path.Name,
        Kind = path.Kind,
        WidthMetres = path.WidthMetres,
        Points = path.Points
            .Select(point => new TerrainPathPointMetadata { XMetres = point.XMetres, ZMetres = point.ZMetres })
            .ToList()
    };

    private static IEnumerable<TerrainPath> LoadPaths(TerrainMetadata metadata)
    {
        if (metadata.Paths is null) yield break;

        foreach (var path in metadata.Paths)
        {
            yield return new TerrainPath(
                path.Id == Guid.Empty ? Guid.NewGuid() : path.Id,
                path.Name,
                path.Kind,
                path.WidthMetres,
                path.Points.Select(point => new TerrainPathPoint(point.XMetres, point.ZMetres)).ToList());
        }
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
