using System.Numerics;
using System.Text.Json;
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
        Assert.Equal(0.5f, tile.ResolutionMetres);
        Assert.Equal(1001, tile.HeightmapWidth);
        Assert.Equal(1001, tile.HeightmapHeight);
        Assert.Equal(tile.HeightmapWidth * tile.HeightmapHeight, tile.Heights.Length);
    }

    [Fact]
    public void TileCloneCopiesHeightAndAlbedoWithoutSharingArrays()
    {
        var tile = new TerrainTile(2, 2, 1);
        tile.SetHeight(1, 1, 4.0f);
        tile.Albedo[0] = 20;

        var clone = tile.Clone();
        tile.SetHeight(1, 1, 8.0f);
        tile.Albedo[0] = 40;

        Assert.Equal(4.0f, clone.GetHeight(1, 1), precision: 5);
        Assert.Equal(20, clone.Albedo[0]);
    }

    [Fact]
    public void WorldCloneCopiesTilesWithoutSharingTileData()
    {
        var world = new TerrainWorld();
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));
        var tile = world.GetTile(new TerrainCoord(1, 0));
        tile.SetHeight(2, 2, 5.0f);

        var clone = world.Clone();
        tile.SetHeight(2, 2, 9.0f);

        Assert.Equal(2, clone.TileCount);
        Assert.Equal(5.0f, clone.GetTile(new TerrainCoord(1, 0)).GetHeight(2, 2), precision: 5);
    }

    [Fact]
    public void WorldSupportsPoiAddUpdateRemoveAndClone()
    {
        var world = new TerrainWorld();
        var poi = world.AddPoi("Camp", 12.0f, 24.0f, TerrainPoiKind.Landmark, "First stop");

        Assert.Single(world.Pois);
        Assert.Equal("Camp", world.Pois[0].Name);
        Assert.True(world.UpdatePoi(poi with { Name = "Base Camp", Kind = TerrainPoiKind.Settlement }));

        var clone = world.Clone();
        Assert.True(world.RemovePoi(poi.Id));

        Assert.Empty(world.Pois);
        Assert.Single(clone.Pois);
        Assert.Equal(poi.Id, clone.Pois[0].Id);
        Assert.Equal("Base Camp", clone.Pois[0].Name);
        Assert.Equal(TerrainPoiKind.Settlement, clone.Pois[0].Kind);
    }

    [Fact]
    public void WorldSupportsPathAddUpdateRemoveAndClone()
    {
        var world = new TerrainWorld();
        var path = world.AddPath("North Road", TerrainPathKind.Road, 6.0f, [new TerrainPathPoint(1.0f, 2.0f)]);
        var updatedPoints = path.Points.ToList();
        updatedPoints.Add(new TerrainPathPoint(3.0f, 4.0f));

        Assert.True(world.UpdatePath(path with { Name = "Old North Road", Points = updatedPoints }));
        var clone = world.Clone();
        updatedPoints.Add(new TerrainPathPoint(5.0f, 6.0f));
        Assert.True(world.UpdatePath(path with { Points = updatedPoints }));
        Assert.True(world.RemovePath(path.Id));

        Assert.Empty(world.Paths);
        Assert.Single(clone.Paths);
        Assert.Equal(path.Id, clone.Paths[0].Id);
        Assert.Equal("Old North Road", clone.Paths[0].Name);
        Assert.Equal(TerrainPathKind.Road, clone.Paths[0].Kind);
        Assert.Equal(6.0f, clone.Paths[0].WidthMetres, precision: 5);
        Assert.Equal(2, clone.Paths[0].Points.Count);
        Assert.Equal(3.0f, clone.Paths[0].Points[1].XMetres, precision: 5);
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
    public void CircleBrushProfileMatchesLegacyRaiseBrush()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        var brush = new TerrainBrushProfile(TerrainBrushShape.Circle, 1.5f, BrushFalloff.Linear);

        var changed = TerrainBrush.ApplyRaiseLower(world, 2, 2, brush, 2, 0.5f, lower: false);
        var tile = world.GetTile(new TerrainCoord(0, 0));

        Assert.True(changed > 0);
        Assert.Equal(1.0f, tile.GetHeight(2, 2), precision: 5);
        Assert.Equal(0.0f, tile.GetHeight(0, 0), precision: 5);
    }

    [Fact]
    public void SquareBrushProfileAffectsSquareFootprint()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        var brush = new TerrainBrushProfile(TerrainBrushShape.Square, 1.5f, BrushFalloff.Linear);

        var changed = TerrainBrush.ApplyRaiseLower(world, 2, 2, brush, 1, 1, lower: false);
        var tile = world.GetTile(new TerrainCoord(0, 0));

        Assert.True(changed > 0);
        Assert.Equal(1.0f, tile.GetHeight(2, 2), precision: 5);
        Assert.True(tile.GetHeight(1, 1) > 0.0f);
        Assert.Equal(0.0f, tile.GetHeight(0, 0), precision: 5);
    }

    [Fact]
    public void NoiseBrushProfileIsDeterministicAndModulatesCircleWeight()
    {
        var circleWorld = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        var noiseWorldA = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        var noiseWorldB = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        var circle = new TerrainBrushProfile(TerrainBrushShape.Circle, 1.5f, BrushFalloff.Linear);
        var noise = new TerrainBrushProfile(TerrainBrushShape.Noise, 1.5f, BrushFalloff.Linear, NoiseScale: 0.18f, NoiseAmount: 0.55f);

        TerrainBrush.ApplyRaiseLower(circleWorld, 2, 2, circle, 1, 1, lower: false);
        TerrainBrush.ApplyRaiseLower(noiseWorldA, 2, 2, noise, 1, 1, lower: false);
        TerrainBrush.ApplyRaiseLower(noiseWorldB, 2, 2, noise, 1, 1, lower: false);
        var circleTile = circleWorld.GetTile(new TerrainCoord(0, 0));
        var noiseTileA = noiseWorldA.GetTile(new TerrainCoord(0, 0));
        var noiseTileB = noiseWorldB.GetTile(new TerrainCoord(0, 0));

        Assert.Equal(noiseTileA.GetHeight(2, 2), noiseTileB.GetHeight(2, 2), precision: 5);
        Assert.True(noiseTileA.GetHeight(2, 2) > 0.0f);
        Assert.True(noiseTileA.GetHeight(2, 2) < circleTile.GetHeight(2, 2));
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
            Assert.True(File.Exists(Path.Combine(directory, "pois.json")));
            Assert.True(File.Exists(Path.Combine(directory, "paths.json")));
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
    public void SmoothBrushRelaxesHeightSpike()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        var tile = world.GetTile(new TerrainCoord(0, 0));
        tile.SetHeight(2, 2, 10.0f);

        var changed = TerrainBrush.ApplySmooth(world, 2, 2, 1.5f, 10.0f, 0.5f, BrushFalloff.Linear);

        Assert.True(changed > 0);
        Assert.True(tile.GetHeight(2, 2) < 10.0f);
    }

    [Fact]
    public void ThermalErosionLowersSpikeAndRaisesNeighbours()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        var tile = world.GetTile(new TerrainCoord(0, 0));
        tile.SetHeight(2, 2, 10.0f);
        var brush = new TerrainBrushProfile(TerrainBrushShape.Circle, 2.5f, BrushFalloff.Linear);

        var changed = TerrainBrush.ApplyThermalErosion(world, 2, 2, brush, talusAngleDegrees: 34.0f, strengthPerSecond: 10.0f, deltaSeconds: 0.5f);

        Assert.True(changed > 0);
        Assert.True(tile.GetHeight(2, 2) < 10.0f);
        Assert.True(tile.GetHeight(1, 2) > 0.0f);
        Assert.True(tile.GetHeight(2, 1) > 0.0f);
    }

    [Fact]
    public void ThermalErosionLeavesShallowSlopesAlone()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        var tile = world.GetTile(new TerrainCoord(0, 0));
        for (var z = 0; z < tile.HeightmapHeight; z++)
        {
            for (var x = 0; x < tile.HeightmapWidth; x++)
            {
                tile.SetHeight(x, z, x * 0.2f);
            }
        }

        var brush = new TerrainBrushProfile(TerrainBrushShape.Circle, 2.5f, BrushFalloff.Linear);
        var changed = TerrainBrush.ApplyThermalErosion(world, 2, 2, brush, talusAngleDegrees: 34.0f, strengthPerSecond: 10.0f, deltaSeconds: 0.5f);

        Assert.Equal(0, changed);
        Assert.Equal(0.4f, tile.GetHeight(2, 2), precision: 5);
    }

    [Fact]
    public void ThermalErosionKeepsTileBoundaryHeightsInSync()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));
        var west = world.GetTile(new TerrainCoord(0, 0));
        var east = world.GetTile(new TerrainCoord(1, 0));
        west.SetHeight(west.HeightmapWidth - 1, 2, 8.0f);
        east.SetHeight(0, 2, 8.0f);
        var brush = new TerrainBrushProfile(TerrainBrushShape.Circle, 2.5f, BrushFalloff.Linear);

        var changed = TerrainBrush.ApplyThermalErosion(world, 4, 2, brush, talusAngleDegrees: 34.0f, strengthPerSecond: 10.0f, deltaSeconds: 0.5f);

        Assert.True(changed > 0);
        Assert.True(west.GetHeight(west.HeightmapWidth - 1, 2) < 8.0f);
        Assert.Equal(west.GetHeight(west.HeightmapWidth - 1, 2), east.GetHeight(0, 2), precision: 5);
    }

    [Fact]
    public void HydraulicErosionLeavesFlatTerrainUnchanged()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(8, 8, 1));
        var tile = world.GetTile(new TerrainCoord(0, 0));

        var changed = HydraulicErosion.Simulate(world, 4, 4, 3.0f, 100, HydraulicErosionSettings.Default, seed: 42);

        Assert.Equal(0, changed);
        Assert.All(tile.Heights, height => Assert.Equal(0.0f, height, precision: 5));
    }

    [Fact]
    public void HydraulicErosionCarvesSlopesAndDepositsSediment()
    {
        var world = TerrainWorld.FromSingleTile(CreateConeTile());
        var tile = world.GetTile(new TerrainCoord(0, 0));
        var original = (float[])tile.Heights.Clone();

        var changed = HydraulicErosion.Simulate(world, 8, 8, 4.0f, 300, HydraulicErosionSettings.Default, seed: 1234);

        Assert.True(changed > 0);
        var lowered = false;
        var raised = false;
        for (var i = 0; i < tile.Heights.Length; i++)
        {
            if (tile.Heights[i] < original[i] - 1e-4f) lowered = true;
            if (tile.Heights[i] > original[i] + 1e-4f) raised = true;
        }

        Assert.True(lowered);
        Assert.True(raised);
    }

    [Fact]
    public void HydraulicErosionIsDeterministicForSeed()
    {
        var worldA = TerrainWorld.FromSingleTile(CreateConeTile());
        var worldB = TerrainWorld.FromSingleTile(CreateConeTile());

        HydraulicErosion.Simulate(worldA, 8, 8, 4.0f, 200, HydraulicErosionSettings.Default, seed: 99);
        HydraulicErosion.Simulate(worldB, 8, 8, 4.0f, 200, HydraulicErosionSettings.Default, seed: 99);

        var tileA = worldA.GetTile(new TerrainCoord(0, 0));
        var tileB = worldB.GetTile(new TerrainCoord(0, 0));
        for (var i = 0; i < tileA.Heights.Length; i++)
        {
            Assert.Equal(tileA.Heights[i], tileB.Heights[i], precision: 6);
        }
    }

    [Fact]
    public void HydraulicErosionKeepsTileBoundaryHeightsInSync()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(8, 8, 1));
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));
        var brush = new TerrainBrushProfile(TerrainBrushShape.Circle, 2.5f, BrushFalloff.Linear);
        TerrainBrush.ApplyRaiseLower(world, 8, 4, brush, 4.0f, 1.0f, lower: false);

        var changed = HydraulicErosion.Simulate(world, 8, 4, 3.0f, 300, HydraulicErosionSettings.Default, seed: 7);
        var west = world.GetTile(new TerrainCoord(0, 0));
        var east = world.GetTile(new TerrainCoord(1, 0));

        Assert.True(changed > 0);
        for (var z = 0; z < west.HeightmapHeight; z++)
        {
            Assert.Equal(west.GetHeight(west.HeightmapWidth - 1, z), east.GetHeight(0, z), precision: 4);
        }
    }

    private static TerrainTile CreateConeTile()
    {
        var tile = new TerrainTile(16, 16, 1);
        for (var z = 0; z < tile.HeightmapHeight; z++)
        {
            for (var x = 0; x < tile.HeightmapWidth; x++)
            {
                var distance = MathF.Sqrt((x - 8) * (x - 8) + (z - 8) * (z - 8));
                tile.SetHeight(x, z, MathF.Max(0.0f, 6.0f - distance));
            }
        }

        return tile;
    }

    [Fact]
    public void FlattenBrushAffectsBothSidesOfTileBoundary()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));

        var changed = TerrainBrush.ApplyFlatten(world, 4, 2, 1.5f, 3.0f, 10.0f, 0.5f, BrushFalloff.Linear);
        var west = world.GetTile(new TerrainCoord(0, 0));
        var east = world.GetTile(new TerrainCoord(1, 0));

        Assert.True(changed > 0);
        Assert.True(west.GetHeight(3, 2) > 0);
        Assert.True(east.GetHeight(1, 2) > 0);
        Assert.Equal(west.GetHeight(west.HeightmapWidth - 1, 2), east.GetHeight(0, 2), precision: 5);
    }

    [Fact]
    public void PaintBrushBlendsAlbedoInsideRadius()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        var tile = world.GetTile(new TerrainCoord(0, 0));

        var changed = TerrainBrush.ApplyPaint(world, 2, 2, 1.1f, new Vector3(1, 0, 0), 1.0f, 0.5f, BrushFalloff.Linear);
        var centre = tile.GetIndex(2, 2) * 4;
        var outside = tile.GetIndex(0, 0) * 4;

        Assert.True(changed > 0);
        Assert.Equal(172, tile.Albedo[centre]);
        Assert.Equal(61, tile.Albedo[centre + 1]);
        Assert.Equal(37, tile.Albedo[centre + 2]);
        Assert.Equal(255, tile.Albedo[centre + 3]);
        Assert.Equal(88, tile.Albedo[outside]);
        Assert.Equal(122, tile.Albedo[outside + 1]);
        Assert.Equal(74, tile.Albedo[outside + 2]);
        Assert.Equal(255, tile.Albedo[outside + 3]);
    }

    [Fact]
    public void PaintBrushAffectsBothSidesOfTileBoundary()
    {
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));

        var changed = TerrainBrush.ApplyPaint(world, 4, 2, 1.1f, new Vector3(0, 0, 1), 1.0f, 1.0f, BrushFalloff.Linear);
        var west = world.GetTile(new TerrainCoord(0, 0));
        var east = world.GetTile(new TerrainCoord(1, 0));
        var westEdge = west.GetIndex(west.HeightmapWidth - 1, 2) * 4;
        var eastEdge = east.GetIndex(0, 2) * 4;

        Assert.True(changed > 0);
        Assert.Equal(0, west.Albedo[westEdge]);
        Assert.Equal(0, west.Albedo[westEdge + 1]);
        Assert.Equal(255, west.Albedo[westEdge + 2]);
        Assert.Equal(255, west.Albedo[westEdge + 3]);
        Assert.Equal(0, east.Albedo[eastEdge]);
        Assert.Equal(0, east.Albedo[eastEdge + 1]);
        Assert.Equal(255, east.Albedo[eastEdge + 2]);
        Assert.Equal(255, east.Albedo[eastEdge + 3]);
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
    public void MultiTileProjectRoundTripPreservesPois()
    {
        var directory = Path.Combine(Path.GetTempPath(), "world-editor-poi-test-" + Guid.NewGuid());
        var world = new TerrainWorld();
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));
        var poi = world.AddPoi("Mine", 520.0f, 18.0f, TerrainPoiKind.Resource, "Iron");

        try
        {
            TerrainProjectStore.Save(world, directory);
            var loaded = TerrainProjectStore.LoadWorld(directory);

            Assert.Single(loaded.Pois);
            Assert.Equal(poi.Id, loaded.Pois[0].Id);
            Assert.Equal("Mine", loaded.Pois[0].Name);
            Assert.Equal(520.0f, loaded.Pois[0].XMetres, precision: 5);
            Assert.Equal(18.0f, loaded.Pois[0].ZMetres, precision: 5);
            Assert.Equal(TerrainPoiKind.Resource, loaded.Pois[0].Kind);
            Assert.Equal("Iron", loaded.Pois[0].Notes);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MultiTileProjectRoundTripPreservesPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "world-editor-path-test-" + Guid.NewGuid());
        var world = new TerrainWorld();
        Assert.True(world.AddTile(new TerrainCoord(1, 0)));
        var path = world.AddPath(
            "River Run",
            TerrainPathKind.River,
            12.0f,
            [new TerrainPathPoint(10.0f, 20.0f), new TerrainPathPoint(520.0f, 25.0f)]);

        try
        {
            TerrainProjectStore.Save(world, directory);
            var loaded = TerrainProjectStore.LoadWorld(directory);

            Assert.Single(loaded.Paths);
            Assert.Equal(path.Id, loaded.Paths[0].Id);
            Assert.Equal("River Run", loaded.Paths[0].Name);
            Assert.Equal(TerrainPathKind.River, loaded.Paths[0].Kind);
            Assert.Equal(12.0f, loaded.Paths[0].WidthMetres, precision: 5);
            Assert.Equal(2, loaded.Paths[0].Points.Count);
            Assert.Equal(520.0f, loaded.Paths[0].Points[1].XMetres, precision: 5);
            Assert.Equal(25.0f, loaded.Paths[0].Points[1].ZMetres, precision: 5);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GodotExportWritesPoisWithComputedHeight()
    {
        var directory = Path.Combine(Path.GetTempPath(), "world-editor-poi-export-" + Guid.NewGuid());
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        world.GetTile(new TerrainCoord(0, 0)).SetHeight(2, 3, 7.5f);
        var poi = world.AddPoi("Vista", 2.0f, 3.0f, TerrainPoiKind.Landmark, "Lookout");

        try
        {
            GodotExporter.Export(world, directory);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "pois.json")));
            var exported = document.RootElement[0];

            Assert.Equal(poi.Id, exported.GetProperty("id").GetGuid());
            Assert.Equal("Vista", exported.GetProperty("name").GetString());
            Assert.Equal("Landmark", exported.GetProperty("kind").GetString());
            Assert.Equal("Lookout", exported.GetProperty("notes").GetString());
            Assert.Equal(2.0f, exported.GetProperty("position_m").GetProperty("x").GetSingle(), precision: 5);
            Assert.Equal(7.5f, exported.GetProperty("position_m").GetProperty("y").GetSingle(), precision: 5);
            Assert.Equal(3.0f, exported.GetProperty("position_m").GetProperty("z").GetSingle(), precision: 5);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GodotExportWritesPathsWithComputedHeights()
    {
        var directory = Path.Combine(Path.GetTempPath(), "world-editor-path-export-" + Guid.NewGuid());
        var world = TerrainWorld.FromSingleTile(new TerrainTile(4, 4, 1));
        world.GetTile(new TerrainCoord(0, 0)).SetHeight(1, 1, 2.5f);
        world.GetTile(new TerrainCoord(0, 0)).SetHeight(3, 2, 8.0f);
        var path = world.AddPath(
            "Main Road",
            TerrainPathKind.Road,
            7.0f,
            [new TerrainPathPoint(1.0f, 1.0f), new TerrainPathPoint(3.0f, 2.0f)]);

        try
        {
            GodotExporter.Export(world, directory);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "paths.json")));
            var exported = document.RootElement[0];

            Assert.Equal(path.Id, exported.GetProperty("id").GetGuid());
            Assert.Equal("Main Road", exported.GetProperty("name").GetString());
            Assert.Equal("Road", exported.GetProperty("kind").GetString());
            Assert.Equal(7.0f, exported.GetProperty("width_m").GetSingle(), precision: 5);
            Assert.Equal(1.0f, exported.GetProperty("points")[0].GetProperty("x").GetSingle(), precision: 5);
            Assert.Equal(2.5f, exported.GetProperty("points")[0].GetProperty("y").GetSingle(), precision: 5);
            Assert.Equal(8.0f, exported.GetProperty("points")[1].GetProperty("y").GetSingle(), precision: 5);
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
            Assert.Empty(loaded.Pois);
            Assert.Empty(loaded.Paths);
            Assert.True(loaded.ContainsTile(new TerrainCoord(0, 0)));
            Assert.Equal(7.25f, loaded.GetTile(new TerrainCoord(0, 0)).GetHeight(1, 1), precision: 5);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
