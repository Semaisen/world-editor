using System.Numerics;

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
        return ApplyRaiseLower(
            world,
            centreXMetres,
            centreZMetres,
            new TerrainBrushProfile(TerrainBrushShape.Circle, radiusMetres, falloff),
            strengthMetresPerSecond,
            deltaSeconds,
            lower);
    }

    public static int ApplyRaiseLower(
        TerrainWorld world,
        float centreXMetres,
        float centreZMetres,
        TerrainBrushProfile brush,
        float strengthMetresPerSecond,
        float deltaSeconds,
        bool lower)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (brush.RadiusMetres <= 0 || strengthMetresPerSecond <= 0 || deltaSeconds <= 0) return 0;

        var direction = lower ? -1.0f : 1.0f;
        var samples = GetWeightedSamples(world, centreXMetres, centreZMetres, brush);
        foreach (var (sample, weight) in samples)
        {
            var tile = world.GetTile(sample.Coord);
            tile.Heights[tile.GetIndex(sample.X, sample.Z)] += direction * strengthMetresPerSecond * deltaSeconds * weight;
        }

        if (samples.Count > 0)
        {
            world.SynchronizeSharedEdges();
        }

        return samples.Count;
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
        return ApplySmooth(
            world,
            centreXMetres,
            centreZMetres,
            new TerrainBrushProfile(TerrainBrushShape.Circle, radiusMetres, falloff),
            strengthPerSecond,
            deltaSeconds);
    }

    public static int ApplySmooth(
        TerrainWorld world,
        float centreXMetres,
        float centreZMetres,
        TerrainBrushProfile brush,
        float strengthPerSecond,
        float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (brush.RadiusMetres <= 0 || strengthPerSecond <= 0 || deltaSeconds <= 0) return 0;

        var samples = GetWeightedSamples(world, centreXMetres, centreZMetres, brush);
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
        return ApplyFlatten(
            world,
            centreXMetres,
            centreZMetres,
            new TerrainBrushProfile(TerrainBrushShape.Circle, radiusMetres, falloff),
            targetHeight,
            strengthPerSecond,
            deltaSeconds);
    }

    public static int ApplyFlatten(
        TerrainWorld world,
        float centreXMetres,
        float centreZMetres,
        TerrainBrushProfile brush,
        float targetHeight,
        float strengthPerSecond,
        float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (brush.RadiusMetres <= 0 || strengthPerSecond <= 0 || deltaSeconds <= 0) return 0;

        var samples = GetWeightedSamples(world, centreXMetres, centreZMetres, brush);
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

    public static int ApplyPaint(
        TerrainWorld world,
        float centreXMetres,
        float centreZMetres,
        float radiusMetres,
        Vector3 color,
        float strengthPerSecond,
        float deltaSeconds,
        BrushFalloff falloff)
    {
        return ApplyPaint(
            world,
            centreXMetres,
            centreZMetres,
            new TerrainBrushProfile(TerrainBrushShape.Circle, radiusMetres, falloff),
            color,
            strengthPerSecond,
            deltaSeconds);
    }

    public static int ApplyPaint(
        TerrainWorld world,
        float centreXMetres,
        float centreZMetres,
        TerrainBrushProfile brush,
        Vector3 color,
        float strengthPerSecond,
        float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (brush.RadiusMetres <= 0 || strengthPerSecond <= 0 || deltaSeconds <= 0) return 0;

        var targetR = Math.Clamp(color.X, 0.0f, 1.0f) * 255.0f;
        var targetG = Math.Clamp(color.Y, 0.0f, 1.0f) * 255.0f;
        var targetB = Math.Clamp(color.Z, 0.0f, 1.0f) * 255.0f;
        var samples = GetWeightedSamples(world, centreXMetres, centreZMetres, brush);
        foreach (var (sample, weight) in samples)
        {
            var tile = world.GetTile(sample.Coord);
            var index = tile.GetIndex(sample.X, sample.Z) * 4;
            var amount = Math.Clamp(strengthPerSecond * deltaSeconds * weight, 0.0f, 1.0f);
            tile.Albedo[index] = LerpByte(tile.Albedo[index], targetR, amount);
            tile.Albedo[index + 1] = LerpByte(tile.Albedo[index + 1], targetG, amount);
            tile.Albedo[index + 2] = LerpByte(tile.Albedo[index + 2], targetB, amount);
            tile.Albedo[index + 3] = 255;
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

    private static bool BrushIntersectsTile(float centreX, float centreZ, TerrainBrushProfile brush, float tileX, float tileZ, float tileWidth, float tileDepth)
    {
        var radius = brush.RadiusMetres;
        if (brush.Shape == TerrainBrushShape.Square)
        {
            return centreX + radius >= tileX &&
                centreX - radius <= tileX + tileWidth &&
                centreZ + radius >= tileZ &&
                centreZ - radius <= tileZ + tileDepth;
        }

        var closestX = Math.Clamp(centreX, tileX, tileX + tileWidth);
        var closestZ = Math.Clamp(centreZ, tileZ, tileZ + tileDepth);
        var dx = centreX - closestX;
        var dz = centreZ - closestZ;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static Dictionary<SampleRef, float> GetWeightedSamples(TerrainWorld world, float centreXMetres, float centreZMetres, TerrainBrushProfile brush)
    {
        var samples = new Dictionary<SampleRef, float>();
        var radiusMetres = brush.RadiusMetres;
        var radiusSq = radiusMetres * radiusMetres;

        foreach (var (coord, tile) in world.Tiles)
        {
            var originX = coord.X * tile.WidthMetres;
            var originZ = coord.Z * tile.DepthMetres;
            if (!BrushIntersectsTile(centreXMetres, centreZMetres, brush, originX, originZ, tile.WidthMetres, tile.DepthMetres))
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
                    var weight = brush.Shape == TerrainBrushShape.Square
                        ? GetSquareWeight(dx, dz, radiusMetres, brush.Falloff)
                        : GetCircleWeight(dx, dz, radiusMetres, radiusSq, brush.Falloff);
                    if (weight < 0.0f) continue;

                    if (brush.Shape == TerrainBrushShape.Noise)
                    {
                        var noise = ValueNoise(sampleX * brush.NoiseScale, sampleZ * brush.NoiseScale);
                        var noiseAmount = Math.Clamp(brush.NoiseAmount, 0.0f, 1.0f);
                        weight *= Lerp(1.0f - noiseAmount, 1.0f, noise);
                    }

                    samples[new SampleRef(coord, x, z)] = weight;
                }
            }
        }

        return samples;
    }

    private static float GetCircleWeight(float dx, float dz, float radiusMetres, float radiusSq, BrushFalloff falloff)
    {
        var distSq = dx * dx + dz * dz;
        if (distSq > radiusSq) return -1.0f;

        var t = 1.0f - MathF.Sqrt(distSq) / radiusMetres;
        return ApplyFalloff(t, falloff);
    }

    private static float GetSquareWeight(float dx, float dz, float radiusMetres, BrushFalloff falloff)
    {
        var absX = MathF.Abs(dx);
        var absZ = MathF.Abs(dz);
        if (absX > radiusMetres || absZ > radiusMetres) return -1.0f;

        var t = 1.0f - MathF.Max(absX, absZ) / radiusMetres;
        return ApplyFalloff(t, falloff);
    }

    private static float ApplyFalloff(float t, BrushFalloff falloff)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return falloff == BrushFalloff.Smooth ? t * t * (3.0f - 2.0f * t) : t;
    }

    private static float ValueNoise(float x, float z)
    {
        var x0 = (int)MathF.Floor(x);
        var z0 = (int)MathF.Floor(z);
        var tx = x - x0;
        var tz = z - z0;
        var sx = tx * tx * (3.0f - 2.0f * tx);
        var sz = tz * tz * (3.0f - 2.0f * tz);
        var north = Lerp(HashNoise(x0, z0), HashNoise(x0 + 1, z0), sx);
        var south = Lerp(HashNoise(x0, z0 + 1), HashNoise(x0 + 1, z0 + 1), sx);
        return Lerp(north, south, sz);
    }

    private static float HashNoise(int x, int z)
    {
        var hash = unchecked((uint)(x * 374761393 + z * 668265263));
        hash = (hash ^ (hash >> 13)) * 1274126177u;
        return (hash ^ (hash >> 16)) / (float)uint.MaxValue;
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

    private static byte LerpByte(byte from, float to, float amount)
    {
        return (byte)Math.Clamp((int)MathF.Round(Lerp(from, to, amount)), byte.MinValue, byte.MaxValue);
    }
}
