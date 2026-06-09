using System.Numerics;

namespace WorldEditor.Core;

public sealed record HydraulicErosionSettings
{
    public static HydraulicErosionSettings Default { get; } = new();

    public int MaxDropletLifetime { get; init; } = 48;
    /// <summary>How strongly a droplet keeps its previous direction (0 = always follows the gradient).</summary>
    public float Inertia { get; init; } = 0.05f;
    public float SedimentCapacityFactor { get; init; } = 4.0f;
    public float MinSedimentCapacity { get; init; } = 0.01f;
    public float ErodeSpeed { get; init; } = 0.3f;
    public float DepositSpeed { get; init; } = 0.3f;
    public float EvaporateSpeed { get; init; } = 0.02f;
    public float Gravity { get; init; } = 4.0f;
    public float InitialWater { get; init; } = 1.0f;
    public float InitialSpeed { get; init; } = 1.0f;
    /// <summary>Radius in heightmap cells over which eroded material is removed, to avoid single-sample pits.</summary>
    public int ErodeRadiusCells { get; init; } = 3;
}

/// <summary>
/// Particle-based hydraulic erosion: simulated rain droplets flow downhill,
/// picking up sediment on steep descents and depositing it where they slow down.
/// Positions are tracked on the world-spanning heightmap grid so droplets cross tile boundaries.
/// </summary>
public static class HydraulicErosion
{
    public static int Simulate(
        TerrainWorld world,
        float centreXMetres,
        float centreZMetres,
        float radiusMetres,
        int dropletCount,
        HydraulicErosionSettings settings,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(settings);
        if (radiusMetres <= 0 || dropletCount <= 0) return 0;

        var template = world.Tiles.Values.First();
        var resolution = template.ResolutionMetres;
        var random = new Random(seed);
        var radiusCells = radiusMetres / resolution;
        var centre = new Vector2(centreXMetres / resolution, centreZMetres / resolution);
        var changed = 0;

        for (var i = 0; i < dropletCount; i++)
        {
            var angle = random.NextSingle() * MathF.Tau;
            var distance = radiusCells * MathF.Sqrt(random.NextSingle());
            var spawn = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            changed += SimulateDroplet(world, template, settings, random, spawn);
        }

        if (changed > 0)
        {
            world.SynchronizeSharedEdges();
        }

        return changed;
    }

    private static int SimulateDroplet(
        TerrainWorld world,
        TerrainTile template,
        HydraulicErosionSettings settings,
        Random random,
        Vector2 position)
    {
        var direction = Vector2.Zero;
        var speed = settings.InitialSpeed;
        var water = settings.InitialWater;
        var sediment = 0.0f;
        var changed = 0;

        for (var lifetime = 0; lifetime < settings.MaxDropletLifetime; lifetime++)
        {
            if (!TrySampleHeightAndGradient(world, template, position, out var height, out var gradient))
            {
                return changed;
            }

            direction = direction * settings.Inertia - gradient * (1.0f - settings.Inertia);
            if (direction.LengthSquared() < 1e-12f)
            {
                var angle = random.NextSingle() * MathF.Tau;
                direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }

            direction = Vector2.Normalize(direction);
            var newPosition = position + direction;
            if (!TrySampleHeightAndGradient(world, template, newPosition, out var newHeight, out _))
            {
                // The droplet flowed off the edge of the world, carrying its sediment with it.
                return changed;
            }

            var deltaHeight = newHeight - height;
            var capacity = MathF.Max(-deltaHeight, settings.MinSedimentCapacity) * speed * water * settings.SedimentCapacityFactor;

            if (sediment > capacity || deltaHeight > 0.0f)
            {
                // Moving uphill fills the pit behind; otherwise drop the surplus over capacity.
                var amount = deltaHeight > 0.0f
                    ? MathF.Min(deltaHeight, sediment)
                    : (sediment - capacity) * settings.DepositSpeed;
                sediment -= amount;
                changed += DepositBilinear(world, template, position, amount);
            }
            else
            {
                // Never erode more than the drop, so a step cannot dig below the destination height.
                var amount = MathF.Min((capacity - sediment) * settings.ErodeSpeed, -deltaHeight);
                sediment += amount;
                changed += ErodeRadius(world, template, position, amount, settings.ErodeRadiusCells);
            }

            speed = MathF.Sqrt(MathF.Max(0.0f, speed * speed - deltaHeight * settings.Gravity));
            water *= 1.0f - settings.EvaporateSpeed;
            position = newPosition;

            if (water < 0.01f)
            {
                break;
            }
        }

        changed += DepositBilinear(world, template, position, sediment);
        return changed;
    }

    private static bool TrySampleHeightAndGradient(
        TerrainWorld world,
        TerrainTile template,
        Vector2 position,
        out float height,
        out Vector2 gradient)
    {
        height = 0.0f;
        gradient = Vector2.Zero;
        var x0 = (int)MathF.Floor(position.X);
        var z0 = (int)MathF.Floor(position.Y);
        if (!TryGetHeight(world, template, x0, z0, out var h00) ||
            !TryGetHeight(world, template, x0 + 1, z0, out var h10) ||
            !TryGetHeight(world, template, x0, z0 + 1, out var h01) ||
            !TryGetHeight(world, template, x0 + 1, z0 + 1, out var h11))
        {
            return false;
        }

        var u = position.X - x0;
        var v = position.Y - z0;
        gradient = new Vector2(
            (h10 - h00) * (1.0f - v) + (h11 - h01) * v,
            (h01 - h00) * (1.0f - u) + (h11 - h10) * u);
        height = h00 * (1.0f - u) * (1.0f - v) + h10 * u * (1.0f - v) + h01 * (1.0f - u) * v + h11 * u * v;
        return true;
    }

    private static int DepositBilinear(TerrainWorld world, TerrainTile template, Vector2 position, float amount)
    {
        if (amount <= 0.0f) return 0;

        var x0 = (int)MathF.Floor(position.X);
        var z0 = (int)MathF.Floor(position.Y);
        var u = position.X - x0;
        var v = position.Y - z0;
        var changed = 0;
        if (AddHeight(world, template, x0, z0, amount * (1.0f - u) * (1.0f - v))) changed++;
        if (AddHeight(world, template, x0 + 1, z0, amount * u * (1.0f - v))) changed++;
        if (AddHeight(world, template, x0, z0 + 1, amount * (1.0f - u) * v)) changed++;
        if (AddHeight(world, template, x0 + 1, z0 + 1, amount * u * v)) changed++;
        return changed;
    }

    private static int ErodeRadius(TerrainWorld world, TerrainTile template, Vector2 position, float amount, int radiusCells)
    {
        if (amount <= 0.0f) return 0;

        var radius = Math.Max(1, radiusCells);
        var nodes = new List<(int Gx, int Gz, float Weight)>();
        var totalWeight = 0.0f;
        var centreX = (int)MathF.Floor(position.X);
        var centreZ = (int)MathF.Floor(position.Y);

        for (var dz = -radius; dz <= radius; dz++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var gx = centreX + dx;
                var gz = centreZ + dz;
                var weight = radius - new Vector2(gx - position.X, gz - position.Y).Length();
                if (weight <= 0.0f || !TryGetHeight(world, template, gx, gz, out _)) continue;

                nodes.Add((gx, gz, weight));
                totalWeight += weight;
            }
        }

        if (totalWeight <= 0.0f) return 0;

        var changed = 0;
        foreach (var (gx, gz, weight) in nodes)
        {
            if (AddHeight(world, template, gx, gz, -amount * weight / totalWeight)) changed++;
        }

        return changed;
    }

    private static bool TryGetHeight(TerrainWorld world, TerrainTile template, int gx, int gz, out float height)
    {
        foreach (var (coord, x, z) in EnumerateCopies(template, gx, gz))
        {
            if (world.TryGetTile(coord, out var tile) && tile is not null)
            {
                height = tile.GetHeight(x, z);
                return true;
            }
        }

        height = 0.0f;
        return false;
    }

    private static bool AddHeight(TerrainWorld world, TerrainTile template, int gx, int gz, float delta)
    {
        if (delta == 0.0f) return false;

        // Boundary samples are duplicated in adjacent tiles; update every copy so the
        // edge-averaging in SynchronizeSharedEdges does not halve the edit.
        var changed = false;
        foreach (var (coord, x, z) in EnumerateCopies(template, gx, gz))
        {
            if (world.TryGetTile(coord, out var tile) && tile is not null)
            {
                tile.Heights[tile.GetIndex(x, z)] += delta;
                changed = true;
            }
        }

        return changed;
    }

    private static IEnumerable<(TerrainCoord Coord, int X, int Z)> EnumerateCopies(TerrainTile template, int gx, int gz)
    {
        var cellsX = template.HeightmapWidth - 1;
        var cellsZ = template.HeightmapHeight - 1;
        var tileX = FloorDiv(gx, cellsX);
        var tileZ = FloorDiv(gz, cellsZ);
        var x = gx - tileX * cellsX;
        var z = gz - tileZ * cellsZ;

        yield return (new TerrainCoord(tileX, tileZ), x, z);
        if (x == 0) yield return (new TerrainCoord(tileX - 1, tileZ), cellsX, z);
        if (z == 0) yield return (new TerrainCoord(tileX, tileZ - 1), x, cellsZ);
        if (x == 0 && z == 0) yield return (new TerrainCoord(tileX - 1, tileZ - 1), cellsX, cellsZ);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}
