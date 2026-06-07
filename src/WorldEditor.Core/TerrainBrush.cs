namespace WorldEditor.Core;

public static class TerrainBrush
{
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
}
