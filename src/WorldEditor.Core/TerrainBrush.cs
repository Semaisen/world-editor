namespace WorldEditor.Core;

public static class TerrainBrush
{
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
}
