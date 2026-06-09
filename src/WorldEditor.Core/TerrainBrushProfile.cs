namespace WorldEditor.Core;

public readonly record struct TerrainBrushProfile(
    TerrainBrushShape Shape,
    float RadiusMetres,
    BrushFalloff Falloff,
    float NoiseScale = 0.18f,
    float NoiseAmount = 0.55f);
