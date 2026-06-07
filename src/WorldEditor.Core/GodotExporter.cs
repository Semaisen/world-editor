namespace WorldEditor.Core;

public static class GodotExporter
{
    public static void Export(TerrainTile tile, string exportDirectory)
    {
        ArgumentNullException.ThrowIfNull(tile);
        Directory.CreateDirectory(exportDirectory);

        TerrainProjectStore.Save(tile, exportDirectory);
        GltfWriter.WriteTerrainGlb(tile, Path.Combine(exportDirectory, "terrain_mesh.glb"));
    }
}
