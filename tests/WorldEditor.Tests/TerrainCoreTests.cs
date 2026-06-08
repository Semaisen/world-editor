using WorldEditor.Core;

namespace WorldEditor.Tests;

public sealed class TerrainCoreTests
{
    [Fact]
    public void DefaultTileUsesSpecResolution()
    {
        var tile = TerrainTile.CreateDefault();

        Assert.Equal(500.0f, tile.WidthMetres);
        Assert.Equal(500.0f, tile.DepthMetres);
        Assert.Equal(0.2f, tile.ResolutionMetres);
        Assert.Equal(2501, tile.HeightmapWidth);
        Assert.Equal(2501, tile.HeightmapHeight);
        Assert.Equal(tile.HeightmapWidth * tile.HeightmapHeight, tile.Heights.Length);
    }

    [Fact]
    public void RaiseBrushChangesSamplesInsideRadius()
    {
        var tile = new TerrainTile(4, 4, 1);

        var changed = TerrainBrush.ApplyRaiseLower(tile, 2, 2, 1.5f, 2, 0.5f, lower: false, BrushFalloff.Linear);

        Assert.True(changed > 0);
        Assert.True(tile.GetHeight(2, 2) > tile.GetHeight(0, 0));
        Assert.Equal(1.0f, tile.GetHeight(2, 2), precision: 5);
    }

    [Fact]
    public void ProjectRoundTripPreservesHeightAndAlbedo()
    {
        var directory = Path.Combine(Path.GetTempPath(), "world-editor-test-" + Guid.NewGuid());
        var tile = new TerrainTile(2, 2, 1);
        tile.SetHeight(1, 1, 3.25f);
        tile.Albedo[0] = 12;
        tile.Albedo[1] = 34;
        tile.Albedo[2] = 56;
        tile.Albedo[3] = 255;

        try
        {
            TerrainProjectStore.Save(tile, directory);
            var loaded = TerrainProjectStore.Load(directory);

            Assert.Equal(3.25f, loaded.GetHeight(1, 1), precision: 5);
            Assert.Equal(12, loaded.Albedo[0]);
            Assert.Equal(34, loaded.Albedo[1]);
            Assert.Equal(56, loaded.Albedo[2]);
            Assert.Equal(255, loaded.Albedo[3]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GodotExportWritesPackageFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "world-editor-export-" + Guid.NewGuid());
        var tile = new TerrainTile(2, 2, 1);

        try
        {
            GodotExporter.Export(tile, directory);

            Assert.True(File.Exists(Path.Combine(directory, "terrain.json")));
            Assert.True(File.Exists(Path.Combine(directory, "heightmap.bin")));
            Assert.True(File.Exists(Path.Combine(directory, "albedo.png")));
            Assert.True(File.Exists(Path.Combine(directory, "terrain_mesh.glb")));
            Assert.True(File.ReadAllBytes(Path.Combine(directory, "terrain_mesh.glb")).Length > 20);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NewWorldStartsWithOriginTile()
    {
        var world = new TerrainWorld();

        Assert.Single(world.Tiles);
        Assert.True(world.ContainsTile(new TerrainCoord(0, 0)));
    }

    [Fact]
    public void AddTileRequiresAdjacency()
    {
        var world = new TerrainWorld();

        Assert.False(world.CanAddTile(new TerrainCoord(2, 0)));
        Assert.False(world.AddTile(new TerrainCoord(2, 0)));
        Assert.True(world.CanAddTile(new TerrainCoord(1, 0)));
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));
        Assert.True(world.ContainsTile(new TerrainCoord(1, 0)));
    }

    [Fact]
    public void AddedTileCopiesConnectedEdgeHeights()
    {
        var origin = new TerrainTile(4, 4, 1);
        for (var z = 0; z < origin.HeightmapHeight; z++)
        {
            origin.SetHeight(origin.HeightmapWidth - 1, z, z + 2.0f);
        }

        var world = TerrainWorld.FromSingleTile(origin);

        Assert.True(world.AddTile(new TerrainCoord(1, 0)));
        var added = world.GetTile(new TerrainCoord(1, 0));

        for (var z = 0; z < added.HeightmapHeight; z++)
        {
            Assert.Equal(origin.GetHeight(origin.HeightmapWidth - 1, z), added.GetHeight(0, z), precision: 5);
        }
    }

    [Fact]
    public void WorldBrushAffectsBothSidesOfTileBoundary()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));

        var changed = TerrainBrush.ApplyRaiseLower(world, 4, 2, 1.5f, 2, 0.5f, lower: false, BrushFalloff.Linear);
        var west = world.GetTile(new TerrainCoord(0, 0));
        var east = world.GetTile(new TerrainCoord(1, 0));

        Assert.True(changed > 0);
        Assert.True(west.GetHeight(3, 2) > 0);
        Assert.True(east.GetHeight(1, 2) > 0);
        Assert.Equal(west.GetHeight(west.HeightmapWidth - 1, 2), east.GetHeight(0, 2), precision: 5);
    }

    [Fact]
    public void RemoveTileRejectsFinalTile()
    {
        var world = new TerrainWorld();

        Assert.False(world.CanRemoveTile(new TerrainCoord(0, 0)));
        Assert.False(world.RemoveTile(new TerrainCoord(0, 0)));
    }

    [Fact]
    public void RemoveTileKeepsWorldConnected()
    {
        var world = new TerrainWorld();
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));
        Assert.True(world.AddTile(new TerrainCoord(2, 0)));

        Assert.False(world.CanRemoveTile(new TerrainCoord(1, 0)));
        Assert.False(world.RemoveTile(new TerrainCoord(1, 0)));
        Assert.True(world.RemoveTile(new TerrainCoord(2, 0)));
        Assert.False(world.ContainsTile(new TerrainCoord(2, 0)));
    }

    [Fact]
    public void MultiTileProjectRoundTripPreservesCoordinatesHeightAndAlbedo()
    {
        var directory = Path.Combine(Path.GetTempPath(), "world-editor-world-test-" + Guid.NewGuid());
        var world = new TerrainWorld();
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));
        var tile = world.GetTile(new TerrainCoord(1, 0));
        tile.SetHeight(2, 3, 4.5f);
        tile.Albedo[0] = 22;
        tile.Albedo[1] = 44;
        tile.Albedo[2] = 66;
        tile.Albedo[3] = 255;

        try
        {
            TerrainProjectStore.Save(world, directory);
            var loaded = TerrainProjectStore.LoadWorld(directory);
            var loadedTile = loaded.GetTile(new TerrainCoord(1, 0));

            Assert.Equal(2, loaded.TileCount);
            Assert.True(loaded.ContainsTile(new TerrainCoord(0, 0)));
            Assert.True(loaded.ContainsTile(new TerrainCoord(1, 0)));
            Assert.Equal(4.5f, loadedTile.GetHeight(2, 3), precision: 5);
            Assert.Equal(22, loadedTile.Albedo[0]);
            Assert.Equal(44, loadedTile.Albedo[1]);
            Assert.Equal(66, loadedTile.Albedo[2]);
            Assert.Equal(255, loadedTile.Albedo[3]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LegacySingleTileProjectLoadsAsWorld()
    {
        var directory = Path.Combine(Path.GetTempPath(), "world-editor-legacy-test-" + Guid.NewGuid());
        var tile = new TerrainTile(2, 2, 1);
        tile.SetHeight(1, 1, 7.25f);

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "terrain.json"),
                """
                {
                  "version": 1,
                  "unit": "metres",
                  "terrain_width_m": 2,
                  "terrain_depth_m": 2,
                  "resolution_m": 1,
                  "heightmap_width": 3,
                  "heightmap_height": 3,
                  "height_format": "float32",
                  "albedo": "albedo.png",
                  "heightmap": "heightmap.bin"
                }
                """);
            TerrainProjectStore.WriteHeightmap(tile, Path.Combine(directory, "heightmap.bin"));
            TerrainProjectStore.WriteAlbedo(tile, Path.Combine(directory, "albedo.png"));

            var loaded = TerrainProjectStore.LoadWorld(directory);

            Assert.Single(loaded.Tiles);
            Assert.True(loaded.ContainsTile(new TerrainCoord(0, 0)));
            Assert.Equal(7.25f, loaded.GetTile(new TerrainCoord(0, 0)).GetHeight(1, 1), precision: 5);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
