namespace WorldEditor.Core;

public sealed class TerrainTile
{
    public const float DefaultSizeMetres = 500.0f;
    public const float DefaultResolutionMetres = 0.5f;

    public TerrainTile(float widthMetres, float depthMetres, float resolutionMetres)
    {
        if (widthMetres <= 0) throw new ArgumentOutOfRangeException(nameof(widthMetres));
        if (depthMetres <= 0) throw new ArgumentOutOfRangeException(nameof(depthMetres));
        if (resolutionMetres <= 0) throw new ArgumentOutOfRangeException(nameof(resolutionMetres));

        WidthMetres = widthMetres;
        DepthMetres = depthMetres;
        ResolutionMetres = resolutionMetres;
        HeightmapWidth = checked((int)MathF.Round(widthMetres / resolutionMetres) + 1);
        HeightmapHeight = checked((int)MathF.Round(depthMetres / resolutionMetres) + 1);
        Heights = new float[HeightmapWidth * HeightmapHeight];
        Albedo = new byte[HeightmapWidth * HeightmapHeight * 4];
        FillAlbedo(88, 122, 74, 255);
    }

    public float WidthMetres { get; }
    public float DepthMetres { get; }
    public float ResolutionMetres { get; }
    public int HeightmapWidth { get; }
    public int HeightmapHeight { get; }
    public float[] Heights { get; }
    public byte[] Albedo { get; }

    public static TerrainTile CreateDefault() => new(DefaultSizeMetres, DefaultSizeMetres, DefaultResolutionMetres);

    public TerrainTile Clone()
    {
        var clone = new TerrainTile(WidthMetres, DepthMetres, ResolutionMetres);
        Heights.CopyTo(clone.Heights, 0);
        Albedo.CopyTo(clone.Albedo, 0);
        return clone;
    }

    public TerrainMetadata ToMetadata() => new()
    {
        TerrainWidthMetres = WidthMetres,
        TerrainDepthMetres = DepthMetres,
        ResolutionMetres = ResolutionMetres,
        HeightmapWidth = HeightmapWidth,
        HeightmapHeight = HeightmapHeight
    };

    public int GetIndex(int x, int z) => z * HeightmapWidth + x;

    public float GetHeight(int x, int z) => Heights[GetIndex(x, z)];

    public void SetHeight(int x, int z, float height) => Heights[GetIndex(x, z)] = height;

    public void FillAlbedo(byte r, byte g, byte b, byte a)
    {
        for (var i = 0; i < Albedo.Length; i += 4)
        {
            Albedo[i] = r;
            Albedo[i + 1] = g;
            Albedo[i + 2] = b;
            Albedo[i + 3] = a;
        }
    }
}
