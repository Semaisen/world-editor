namespace WorldEditor.Core;

public static class TerrainBrush
{
    private readonly record struct SampleRef(TerrainCoord Coord, int X, int Z);

    public static int ApplyRaiseLower(
        TerrainWorld world,
        float centreXMetres,
        float centreZMetres,
        float radiusMetres,
        float strengthMetresPerSecond,
        float deltaSeconds,
        bool lower,
        BrushFalloff falloff)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (radiusMetres <= 0 || strengthMetresPerSecond <= 0 || deltaSeconds <= 0) return 0;

        var changed = 0;
        foreach (var (coord, tile) in world.Tiles)
        {
            var originX = coord.X * tile.WidthMetres;
            var originZ = coord.Z * tile.DepthMetres;
            if (!CircleIntersectsTile(centreXMetres, centreZMetres, radiusMetres, originX, originZ, tile.WidthMetres, tile.DepthMetres))
            {
                continue;
            }

            changed += ApplyRaiseLower(
                tile,
                centreXMetres - originX,
                centreZMetres - originZ,
                radiusMetres,
                strengthMetresPerSecond,
                deltaSeconds,
                lower,
                falloff);
        }

        if (changed > 0)
        {
            world.SynchronizeSharedEdges();
        }

        return changed;
    }

    public static int ApplySmooth(
        TerrainWorld world,
        float centreXMetres,
        float centreZMetres,
        float radiusMetres,
        float strengthPerSecond,
        float deltaSeconds,
        BrushFalloff falloff)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (radiusMetres <= 0 || strengthPerSecond <= 0 || deltaSeconds <= 0) return 0;

        var samples = GetSamplesInRadius(world, centreXMetres, centreZMetres, radiusMetres, falloff);
        if (samples.Count == 0) return 0;

        var originalHeights = new Dictionary<SampleRef, float>(samples.Count);
        foreach (var sample in samples.Keys)
        {
            originalHeights[sample] = world.GetTile(sample.Coord).GetHeight(sample.X, sample.Z);
        }

        foreach (var (sample, weight) in samples)
        {
            var tile = world.GetTile(sample.Coord);
            var current = originalHeights[sample];
            var average = GetNeighbourAverage(world, originalHeights, sample);
            var amount = Math.Clamp(strengthPerSecond * deltaSeconds * weight, 0.0f, 1.0f);
            tile.SetHeight(sample.X, sample.Z, Lerp(current, average, amount));
        }

        world.SynchronizeSharedEdges();
        return samples.Count;
    }

    public static int ApplyFlatten(
        TerrainWorld world,
        float centreXMetres,
        float centreZMetres,
        float radiusMetres,
        float targetHeight,
        float strengthPerSecond,
        float deltaSeconds,
        BrushFalloff falloff)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (radiusMetres <= 0 || strengthPerSecond <= 0 || deltaSeconds <= 0) return 0;

        var samples = GetSamplesInRadius(world, centreXMetres, centreZMetres, radiusMetres, falloff);
        foreach (var (sample, weight) in samples)
        {
            var tile = world.GetTile(sample.Coord);
            var current = tile.GetHeight(sample.X, sample.Z);
            var amount = Math.Clamp(strengthPerSecond * deltaSeconds * weight, 0.0f, 1.0f);
            tile.SetHeight(sample.X, sample.Z, Lerp(current, targetHeight, amount));
        }

        if (samples.Count > 0)
        {
            world.SynchronizeSharedEdges();
        }

        return samples.Count;
    }

    public static int ApplyRaiseLower(
        TerrainTile tile,
        float centreXMetres,
        float centreZMetres,
        float radiusMetres,
        float strengthMetresPerSecond,
        float deltaSeconds,
        bool lower,
        BrushFalloff falloff)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (radiusMetres <= 0 || strengthMetresPerSecond <= 0 || deltaSeconds <= 0) return 0;

        var minX = Math.Max(0, (int)MathF.Floor((centreXMetres - radiusMetres) / tile.ResolutionMetres));
        var maxX = Math.Min(tile.HeightmapWidth - 1, (int)MathF.Ceiling((centreXMetres + radiusMetres) / tile.ResolutionMetres));
        var minZ = Math.Max(0, (int)MathF.Floor((centreZMetres - radiusMetres) / tile.ResolutionMetres));
        var maxZ = Math.Min(tile.HeightmapHeight - 1, (int)MathF.Ceiling((centreZMetres + radiusMetres) / tile.ResolutionMetres));
        var radiusSq = radiusMetres * radiusMetres;
        var direction = lower ? -1.0f : 1.0f;
        var changed = 0;

        for (var z = minZ; z <= maxZ; z++)
        {
            var sampleZ = z * tile.ResolutionMetres;
            for (var x = minX; x <= maxX; x++)
            {
                var sampleX = x * tile.ResolutionMetres;
                var dx = sampleX - centreXMetres;
                var dz = sampleZ - centreZMetres;
                var distSq = dx * dx + dz * dz;
                if (distSq > radiusSq) continue;

                var t = 1.0f - MathF.Sqrt(distSq) / radiusMetres;
                var weight = falloff == BrushFalloff.Smooth ? t * t * (3.0f - 2.0f * t) : t;
                tile.Heights[tile.GetIndex(x, z)] += direction * strengthMetresPerSecond * deltaSeconds * weight;
                changed++;
            }
        }

        return changed;
    }

    private static bool CircleIntersectsTile(float centreX, float centreZ, float radius, float tileX, float tileZ, float tileWidth, float tileDepth)
    {
        var closestX = Math.Clamp(centreX, tileX, tileX + tileWidth);
        var closestZ = Math.Clamp(centreZ, tileZ, tileZ + tileDepth);
        var dx = centreX - closestX;
        var dz = centreZ - closestZ;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static Dictionary<SampleRef, float> GetSamplesInRadius(TerrainWorld world, float centreXMetres, float centreZMetres, float radiusMetres, BrushFalloff falloff)
    {
        var samples = new Dictionary<SampleRef, float>();
        var radiusSq = radiusMetres * radiusMetres;

        foreach (var (coord, tile) in world.Tiles)
        {
            var originX = coord.X * tile.WidthMetres;
            var originZ = coord.Z * tile.DepthMetres;
            if (!CircleIntersectsTile(centreXMetres, centreZMetres, radiusMetres, originX, originZ, tile.WidthMetres, tile.DepthMetres))
            {
                continue;
            }

            var localCentreX = centreXMetres - originX;
            var localCentreZ = centreZMetres - originZ;
            var minX = Math.Max(0, (int)MathF.Floor((localCentreX - radiusMetres) / tile.ResolutionMetres));
            var maxX = Math.Min(tile.HeightmapWidth - 1, (int)MathF.Ceiling((localCentreX + radiusMetres) / tile.ResolutionMetres));
            var minZ = Math.Max(0, (int)MathF.Floor((localCentreZ - radiusMetres) / tile.ResolutionMetres));
            var maxZ = Math.Min(tile.HeightmapHeight - 1, (int)MathF.Ceiling((localCentreZ + radiusMetres) / tile.ResolutionMetres));

            for (var z = minZ; z <= maxZ; z++)
            {
                var sampleZ = originZ + z * tile.ResolutionMetres;
                for (var x = minX; x <= maxX; x++)
                {
                    var sampleX = originX + x * tile.ResolutionMetres;
                    var dx = sampleX - centreXMetres;
                    var dz = sampleZ - centreZMetres;
                    var distSq = dx * dx + dz * dz;
                    if (distSq > radiusSq) continue;

                    var t = 1.0f - MathF.Sqrt(distSq) / radiusMetres;
                    samples[new SampleRef(coord, x, z)] = falloff == BrushFalloff.Smooth ? t * t * (3.0f - 2.0f * t) : t;
                }
            }
        }

        return samples;
    }

    private static float GetNeighbourAverage(TerrainWorld world, IReadOnlyDictionary<SampleRef, float> originalHeights, SampleRef sample)
    {
        var tile = world.GetTile(sample.Coord);
        var total = 0.0f;
        var count = 0;

        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (TryResolveSample(world, sample.Coord, sample.X + dx, sample.Z + dz, tile, out var neighbour))
                {
                    total += originalHeights.TryGetValue(neighbour, out var height)
                        ? height
                        : world.GetTile(neighbour.Coord).GetHeight(neighbour.X, neighbour.Z);
                    count++;
                }
            }
        }

        return count == 0 ? tile.GetHeight(sample.X, sample.Z) : total / count;
    }

    private static bool TryResolveSample(TerrainWorld world, TerrainCoord coord, int x, int z, TerrainTile tile, out SampleRef sample)
    {
        var resolvedCoord = coord;
        var resolvedX = x;
        var resolvedZ = z;

        if (resolvedX < 0)
        {
            resolvedCoord = new TerrainCoord(resolvedCoord.X - 1, resolvedCoord.Z);
            resolvedX = tile.HeightmapWidth - 2;
        }
        else if (resolvedX >= tile.HeightmapWidth)
        {
            resolvedCoord = new TerrainCoord(resolvedCoord.X + 1, resolvedCoord.Z);
            resolvedX = 1;
        }

        if (resolvedZ < 0)
        {
            resolvedCoord = new TerrainCoord(resolvedCoord.X, resolvedCoord.Z - 1);
            resolvedZ = tile.HeightmapHeight - 2;
        }
        else if (resolvedZ >= tile.HeightmapHeight)
        {
            resolvedCoord = new TerrainCoord(resolvedCoord.X, resolvedCoord.Z + 1);
            resolvedZ = 1;
        }

        if (!world.TryGetTile(resolvedCoord, out var resolvedTile) || resolvedTile is null)
        {
            sample = default;
            return false;
        }

        sample = new SampleRef(
            resolvedCoord,
            Math.Clamp(resolvedX, 0, resolvedTile.HeightmapWidth - 1),
            Math.Clamp(resolvedZ, 0, resolvedTile.HeightmapHeight - 1));
        return true;
    }

    private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;
}
