using System.Numerics;
using System.Text;
using System.Text.Json;

namespace WorldEditor.Core;

internal static class GltfWriter
{
    public static void WriteTerrainGlb(TerrainTile tile, string path, float offsetX = 0.0f, float offsetZ = 0.0f)
    {
        var vertexCount = checked(tile.HeightmapWidth * tile.HeightmapHeight);
        var quadCount = checked((tile.HeightmapWidth - 1) * (tile.HeightmapHeight - 1));
        var indexCount = checked(quadCount * 6);

        var positionsOffset = 0;
        var positionsLength = vertexCount * 3 * sizeof(float);
        var normalsOffset = Align4(positionsOffset + positionsLength);
        var normalsLength = vertexCount * 3 * sizeof(float);
        var texCoordsOffset = Align4(normalsOffset + normalsLength);
        var texCoordsLength = vertexCount * 2 * sizeof(float);
        var indicesOffset = Align4(texCoordsOffset + texCoordsLength);
        var indicesLength = indexCount * sizeof(uint);
        var binaryLength = Align4(indicesOffset + indicesLength);

        var binary = new byte[binaryLength];
        WriteVertices(tile, binary.AsSpan(positionsOffset, positionsLength), binary.AsSpan(normalsOffset, normalsLength), binary.AsSpan(texCoordsOffset, texCoordsLength));
        WriteIndices(tile, binary.AsSpan(indicesOffset, indicesLength));

        var json = BuildJson(tile, vertexCount, indexCount, binaryLength, positionsOffset, positionsLength, normalsOffset, normalsLength, texCoordsOffset, texCoordsLength, indicesOffset, indicesLength, offsetX, offsetZ);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonPaddedLength = Align4(jsonBytes.Length);
        Array.Resize(ref jsonBytes, jsonPaddedLength);
        for (var i = json.Length; i < jsonBytes.Length; i++) jsonBytes[i] = 0x20;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(0x46546C67u);
        writer.Write(2u);
        writer.Write((uint)(12 + 8 + jsonBytes.Length + 8 + binary.Length));
        writer.Write((uint)jsonBytes.Length);
        writer.Write(0x4E4F534Au);
        writer.Write(jsonBytes);
        writer.Write((uint)binary.Length);
        writer.Write(0x004E4942u);
        writer.Write(binary);
    }

    private static void WriteVertices(TerrainTile tile, Span<byte> positions, Span<byte> normals, Span<byte> texCoords)
    {
        for (var z = 0; z < tile.HeightmapHeight; z++)
        {
            for (var x = 0; x < tile.HeightmapWidth; x++)
            {
                var index = tile.GetIndex(x, z);
                var posIndex = index * 3 * sizeof(float);
                var normalIndex = index * 3 * sizeof(float);
                var uvIndex = index * 2 * sizeof(float);
                var normal = EstimateNormal(tile, x, z);

                WriteSingle(positions, posIndex, x * tile.ResolutionMetres);
                WriteSingle(positions, posIndex + 4, tile.Heights[index]);
                WriteSingle(positions, posIndex + 8, z * tile.ResolutionMetres);

                WriteSingle(normals, normalIndex, normal.X);
                WriteSingle(normals, normalIndex + 4, normal.Y);
                WriteSingle(normals, normalIndex + 8, normal.Z);

                WriteSingle(texCoords, uvIndex, x / (float)(tile.HeightmapWidth - 1));
                WriteSingle(texCoords, uvIndex + 4, z / (float)(tile.HeightmapHeight - 1));
            }
        }
    }

    private static void WriteIndices(TerrainTile tile, Span<byte> indices)
    {
        var offset = 0;
        for (var z = 0; z < tile.HeightmapHeight - 1; z++)
        {
            for (var x = 0; x < tile.HeightmapWidth - 1; x++)
            {
                var a = (uint)tile.GetIndex(x, z);
                var b = (uint)tile.GetIndex(x + 1, z);
                var c = (uint)tile.GetIndex(x, z + 1);
                var d = (uint)tile.GetIndex(x + 1, z + 1);
                WriteUInt(indices, ref offset, a);
                WriteUInt(indices, ref offset, c);
                WriteUInt(indices, ref offset, b);
                WriteUInt(indices, ref offset, b);
                WriteUInt(indices, ref offset, c);
                WriteUInt(indices, ref offset, d);
            }
        }
    }

    private static string BuildJson(
        TerrainTile tile,
        int vertexCount,
        int indexCount,
        int binaryLength,
        int positionsOffset,
        int positionsLength,
        int normalsOffset,
        int normalsLength,
        int texCoordsOffset,
        int texCoordsLength,
        int indicesOffset,
        int indicesLength,
        float offsetX,
        float offsetZ)
    {
        var maxHeight = tile.Heights.Length == 0 ? 0 : tile.Heights.Max();
        var minHeight = tile.Heights.Length == 0 ? 0 : tile.Heights.Min();
        var document = new
        {
            asset = new { version = "2.0", generator = "World Editor" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0, name = "Terrain", translation = new[] { offsetX, 0.0f, offsetZ } } },
            meshes = new[]
            {
                new
                {
                    name = "TerrainMesh",
                    primitives = new[]
                    {
                        new
                        {
                            attributes = new { POSITION = 0, NORMAL = 1, TEXCOORD_0 = 2 },
                            indices = 3,
                            mode = 4
                        }
                    }
                }
            },
            buffers = new[] { new { byteLength = binaryLength } },
            bufferViews = new[]
            {
                new { buffer = 0, byteOffset = positionsOffset, byteLength = positionsLength, target = 34962 },
                new { buffer = 0, byteOffset = normalsOffset, byteLength = normalsLength, target = 34962 },
                new { buffer = 0, byteOffset = texCoordsOffset, byteLength = texCoordsLength, target = 34962 },
                new { buffer = 0, byteOffset = indicesOffset, byteLength = indicesLength, target = 34963 }
            },
            accessors = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = vertexCount, type = "VEC3", min = new[] { 0.0f, minHeight, 0.0f }, max = new[] { tile.WidthMetres, maxHeight, tile.DepthMetres } },
                new { bufferView = 1, componentType = 5126, count = vertexCount, type = "VEC3" },
                new { bufferView = 2, componentType = 5126, count = vertexCount, type = "VEC2" },
                new { bufferView = 3, componentType = 5125, count = indexCount, type = "SCALAR" }
            }
        };

        return JsonSerializer.Serialize(document);
    }

    private static Vector3 EstimateNormal(TerrainTile tile, int x, int z)
    {
        var left = tile.GetHeight(Math.Max(0, x - 1), z);
        var right = tile.GetHeight(Math.Min(tile.HeightmapWidth - 1, x + 1), z);
        var down = tile.GetHeight(x, Math.Max(0, z - 1));
        var up = tile.GetHeight(x, Math.Min(tile.HeightmapHeight - 1, z + 1));
        return Vector3.Normalize(new Vector3(left - right, 2.0f * tile.ResolutionMetres, down - up));
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static void WriteSingle(Span<byte> buffer, int offset, float value) => BitConverter.TryWriteBytes(buffer.Slice(offset, sizeof(float)), value);

    private static void WriteUInt(Span<byte> buffer, ref int offset, uint value)
    {
        BitConverter.TryWriteBytes(buffer.Slice(offset, sizeof(uint)), value);
        offset += sizeof(uint);
    }
}
