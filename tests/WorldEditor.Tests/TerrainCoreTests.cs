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
}
