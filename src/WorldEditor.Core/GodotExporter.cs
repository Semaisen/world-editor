namespace WorldEditor.Core;

public static class GodotExporter
{
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
}
