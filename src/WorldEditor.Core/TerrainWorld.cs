namespace WorldEditor.Core;

public sealed class TerrainWorld
{
    private const float StitchBlendDistanceMetres = 24.0f;

    private static readonly TerrainCoord[] NeighbourOffsets =
    [
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1)
    ];

    private readonly Dictionary<TerrainCoord, TerrainTile> _tiles = [];

    public TerrainWorld()
    {
        _tiles.Add(new TerrainCoord(0, 0), TerrainTile.CreateDefault());
    }

    private TerrainWorld(Dictionary<TerrainCoord, TerrainTile> tiles)
    {
        if (tiles.Count == 0) throw new ArgumentException("A terrain world must contain at least one tile.", nameof(tiles));
        _tiles = tiles;
    }

    public IReadOnlyDictionary<TerrainCoord, TerrainTile> Tiles => _tiles;

    public IEnumerable<TerrainCoord> Coordinates => _tiles.Keys;

    public int TileCount => _tiles.Count;

    public static TerrainWorld FromSingleTile(TerrainTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        return FromTiles(new Dictionary<TerrainCoord, TerrainTile> { [new TerrainCoord(0, 0)] = tile });
    }

    public static TerrainWorld FromTiles(Dictionary<TerrainCoord, TerrainTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        return new TerrainWorld(new Dictionary<TerrainCoord, TerrainTile>(tiles));
    }

    public TerrainWorld Clone()
    {
        return FromTiles(_tiles.ToDictionary(entry => entry.Key, entry => entry.Value.Clone()));
    }

    public TerrainTile GetTile(TerrainCoord coord) => _tiles[coord];

    public bool TryGetTile(TerrainCoord coord, out TerrainTile? tile) => _tiles.TryGetValue(coord, out tile);

    public bool ContainsTile(TerrainCoord coord) => _tiles.ContainsKey(coord);

    public bool CanAddTile(TerrainCoord coord)
    {
        if (_tiles.ContainsKey(coord)) return false;
        return Neighbours(coord).Any(_tiles.ContainsKey);
    }

    public bool AddTile(TerrainCoord coord)
    {
        if (!CanAddTile(coord)) return false;

        var template = _tiles.Values.First();
        var tile = new TerrainTile(template.WidthMetres, template.DepthMetres, template.ResolutionMetres);
        BlendTileFromNeighbours(coord, tile);
        _tiles.Add(coord, tile);
        SynchronizeSharedEdges(coord);
        return true;
    }

    public bool CanRemoveTile(TerrainCoord coord)
    {
        if (!_tiles.ContainsKey(coord) || _tiles.Count == 1) return false;

        var remaining = _tiles.Keys.Where(candidate => candidate != coord).ToHashSet();
        var visited = new HashSet<TerrainCoord>();
        var queue = new Queue<TerrainCoord>();
        queue.Enqueue(remaining.First());

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            foreach (var neighbour in Neighbours(current))
            {
                if (remaining.Contains(neighbour) && !visited.Contains(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        return visited.Count == remaining.Count;
    }

    public bool RemoveTile(TerrainCoord coord)
    {
        if (!CanRemoveTile(coord)) return false;
        return _tiles.Remove(coord);
    }

    public HashSet<TerrainCoord> GetAddableTileCoords()
    {
        var addable = new HashSet<TerrainCoord>();
        foreach (var coord in _tiles.Keys)
        {
            foreach (var neighbour in Neighbours(coord))
            {
                if (CanAddTile(neighbour))
                {
                    addable.Add(neighbour);
                }
            }
        }

        return addable;
    }

    public void SynchronizeSharedEdges()
    {
        foreach (var coord in _tiles.Keys.ToList())
        {
            SynchronizeSharedEdges(coord);
        }
    }

    private void BlendTileFromNeighbours(TerrainCoord coord, TerrainTile tile)
    {
        var blendDistance = Math.Max(tile.ResolutionMetres, Math.Min(StitchBlendDistanceMetres, Math.Min(tile.WidthMetres, tile.DepthMetres)));

        for (var z = 0; z < tile.HeightmapHeight; z++)
        {
            var localZ = z * tile.ResolutionMetres;
            for (var x = 0; x < tile.HeightmapWidth; x++)
            {
                var localX = x * tile.ResolutionMetres;
                var totalWeight = 0.0f;
                var totalHeight = 0.0f;

                AddNeighbourBlend(coord, tile, x, z, localX, blendDistance, new TerrainCoord(coord.X - 1, coord.Z), Edge.West, ref totalHeight, ref totalWeight);
                AddNeighbourBlend(coord, tile, x, z, tile.WidthMetres - localX, blendDistance, new TerrainCoord(coord.X + 1, coord.Z), Edge.East, ref totalHeight, ref totalWeight);
                AddNeighbourBlend(coord, tile, x, z, localZ, blendDistance, new TerrainCoord(coord.X, coord.Z - 1), Edge.North, ref totalHeight, ref totalWeight);
                AddNeighbourBlend(coord, tile, x, z, tile.DepthMetres - localZ, blendDistance, new TerrainCoord(coord.X, coord.Z + 1), Edge.South, ref totalHeight, ref totalWeight);

                if (totalWeight > 0.0f)
                {
                    tile.SetHeight(x, z, totalHeight / totalWeight);
                }
            }
        }
    }

    private void AddNeighbourBlend(
        TerrainCoord coord,
        TerrainTile tile,
        int x,
        int z,
        float distanceFromEdge,
        float blendDistance,
        TerrainCoord neighbourCoord,
        Edge edge,
        ref float totalHeight,
        ref float totalWeight)
    {
        if (!_tiles.TryGetValue(neighbourCoord, out var neighbour)) return;
        if (distanceFromEdge > blendDistance) return;

        var t = 1.0f - Math.Clamp(distanceFromEdge / blendDistance, 0.0f, 1.0f);
        var weight = t * t * (3.0f - 2.0f * t);
        if (weight <= 0.0f) return;

        totalHeight += GetNeighbourEdgeHeight(neighbour, edge, x, z, tile) * weight;
        totalWeight += weight;
    }

    private static float GetNeighbourEdgeHeight(TerrainTile neighbour, Edge edge, int x, int z, TerrainTile tile)
    {
        var sampleX = Math.Clamp(x, 0, neighbour.HeightmapWidth - 1);
        var sampleZ = Math.Clamp(z, 0, neighbour.HeightmapHeight - 1);
        return edge switch
        {
            Edge.West => neighbour.GetHeight(neighbour.HeightmapWidth - 1, sampleZ),
            Edge.East => neighbour.GetHeight(0, sampleZ),
            Edge.North => neighbour.GetHeight(sampleX, neighbour.HeightmapHeight - 1),
            Edge.South => neighbour.GetHeight(sampleX, 0),
            _ => 0.0f
        };
    }

    private void SynchronizeSharedEdges(TerrainCoord coord)
    {
        if (!_tiles.TryGetValue(coord, out var tile)) return;

        if (_tiles.TryGetValue(new TerrainCoord(coord.X + 1, coord.Z), out var east))
        {
            SynchronizeVerticalEdge(tile, east);
        }

        if (_tiles.TryGetValue(new TerrainCoord(coord.X, coord.Z + 1), out var south))
        {
            SynchronizeHorizontalEdge(tile, south);
        }
    }

    private static void SynchronizeVerticalEdge(TerrainTile west, TerrainTile east)
    {
        var count = Math.Min(west.HeightmapHeight, east.HeightmapHeight);
        for (var z = 0; z < count; z++)
        {
            var height = (west.GetHeight(west.HeightmapWidth - 1, z) + east.GetHeight(0, z)) * 0.5f;
            west.SetHeight(west.HeightmapWidth - 1, z, height);
            east.SetHeight(0, z, height);
        }
    }

    private static void SynchronizeHorizontalEdge(TerrainTile north, TerrainTile south)
    {
        var count = Math.Min(north.HeightmapWidth, south.HeightmapWidth);
        for (var x = 0; x < count; x++)
        {
            var height = (north.GetHeight(x, north.HeightmapHeight - 1) + south.GetHeight(x, 0)) * 0.5f;
            north.SetHeight(x, north.HeightmapHeight - 1, height);
            south.SetHeight(x, 0, height);
        }
    }

    private static IEnumerable<TerrainCoord> Neighbours(TerrainCoord coord)
    {
        foreach (var offset in NeighbourOffsets)
        {
            yield return new TerrainCoord(coord.X + offset.X, coord.Z + offset.Z);
        }
    }

    private enum Edge
    {
        West,
        East,
        North,
        South
    }
}
