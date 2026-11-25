using System.Buffers.Binary;
using LostSpawns.Models;
using LostSpawns.Rendering;

namespace TheLostDays.Native;

internal sealed class NativeWorld
{
    public const int SeaLevel = 64;
    private const float CellSize = 15.62f;
    private readonly short[]? _heightmap;
    private readonly int _mapSize;
    private readonly TerrainGenerator _fallback = new(42);

    private NativeWorld(short[]? heightmap, int mapSize)
    {
        _heightmap = heightmap;
        _mapSize = mapSize;
    }

    public static NativeWorld Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "deer_isle.heightmap");
        if (!File.Exists(path))
            return new NativeWorld(null, 0);

        byte[] bytes = File.ReadAllBytes(path);
        int count = bytes.Length / 2;
        int size = (int)Math.Sqrt(count);
        if (size * size != count)
            throw new InvalidDataException("Deer Isle heightmap is not a square Int16 map.");

        var map = new short[count];
        for (int i = 0; i < count; i++)
            map[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2));

        ApplyBathymetry(map, size);
        return new NativeWorld(map, size);
    }

    public (float X, float Z) FindSpawn()
    {
        if (_heightmap == null)
            return (0, 0);

        if (GetHeight(0, 0) > SeaLevel + 4)
            return (0, 0);

        for (int radius = 64; radius <= 7000; radius += 64)
        {
            for (int angle = 0; angle < 360; angle += 8)
            {
                float a = angle * MathF.PI / 180f;
                float x = MathF.Cos(a) * radius;
                float z = MathF.Sin(a) * radius;
                if (GetHeight(x, z) > SeaLevel + 4)
                    return (x, z);
            }
        }

        throw new InvalidDataException("No land spawn was found in the Deer Isle heightmap.");
    }

    public int GetHeight(float worldX, float worldZ)
    {
        if (_heightmap == null)
            return _fallback.GetHeight(worldX, worldZ) + 64;

        float mapMeters = _mapSize * CellSize;
        float gx = Math.Clamp((worldX + mapMeters * 0.5f) / CellSize, 0, _mapSize - 1.001f);
        float gz = Math.Clamp((worldZ + mapMeters * 0.5f) / CellSize, 0, _mapSize - 1.001f);
        int x = Math.Clamp((int)MathF.Floor(gx), 0, _mapSize - 2);
        int z = Math.Clamp((int)MathF.Floor(gz), 0, _mapSize - 2);
        float fx = gx - x;
        float fz = gz - z;
        float a = _heightmap[z * _mapSize + x];
        float b = _heightmap[z * _mapSize + x + 1];
        float c = _heightmap[(z + 1) * _mapSize + x];
        float d = _heightmap[(z + 1) * _mapSize + x + 1];
        return Math.Clamp(SeaLevel + (int)MathF.Round(
            a * (1 - fx) * (1 - fz) +
            b * fx * (1 - fz) +
            c * (1 - fx) * fz +
            d * fx * fz), 1, ChunkData.Height - 2);
    }

    public ChunkData GenerateChunk(int chunkX, int chunkZ)
    {
        if (_heightmap == null)
            return _fallback.GenerateChunk(chunkX, chunkZ);

        var chunk = new ChunkData(chunkX, chunkZ);
        for (int x = 0; x < ChunkData.SizeXZ; x++)
        for (int z = 0; z < ChunkData.SizeXZ; z++)
        {
            int wx = chunkX * ChunkData.SizeXZ + x;
            int wz = chunkZ * ChunkData.SizeXZ + z;
            int h = GetHeight(wx, wz);
            int slope = Math.Max(
                Math.Max(Math.Abs(h - GetHeight(wx, wz - 1)), Math.Abs(h - GetHeight(wx, wz + 1))),
                Math.Max(Math.Abs(h - GetHeight(wx + 1, wz)), Math.Abs(h - GetHeight(wx - 1, wz))));
            FillColumn(chunk, x, z, wx, wz, h, slope);
        }

        Vegetation.Place(chunk, chunkX, chunkZ, SurfaceAt, SeaLevel, 42);
        Structures.Place(chunk, chunkX, chunkZ, SurfaceAt, SeaLevel, 42);
        return chunk;
    }

    public (int Y, BlockType Type) SurfaceAt(int wx, int wz)
    {
        int h = GetHeight(wx, wz);
        int slope = Math.Max(
            Math.Max(Math.Abs(h - GetHeight(wx, wz - 1)), Math.Abs(h - GetHeight(wx, wz + 1))),
            Math.Max(Math.Abs(h - GetHeight(wx + 1, wz)), Math.Abs(h - GetHeight(wx - 1, wz))));
        return (h, SurfaceBlock(h, slope, wx, wz));
    }

    public static int FloorDiv(int value, int divisor) => (int)MathF.Floor(value / (float)divisor);

    private static void FillColumn(ChunkData chunk, int x, int z, int wx, int wz, int surface, int slope)
    {
        surface = Math.Clamp(surface, 1, ChunkData.Height - 2);
        BlockType top = SurfaceBlock(surface, slope, wx, wz);
        BlockType under = top switch
        {
            BlockType.Sand or BlockType.Gravel => BlockType.Sand,
            BlockType.Granite => BlockType.Granite,
            _ => BlockType.Dirt
        };

        for (int y = 0; y < ChunkData.Height; y++)
        {
            BlockType block;
            if (y > surface) block = y <= SeaLevel ? BlockType.Water : BlockType.Air;
            else if (y == surface) block = top;
            else if (y > surface - 2 && slope >= 4) block = BlockType.Granite;
            else if (y > surface - 4) block = under;
            else block = BlockType.Stone;
            chunk.SetBlock(x, y, z, block);
        }
    }

    private static BlockType SurfaceBlock(int y, int slope, int wx, int wz)
    {
        int above = y - SeaLevel;
        if (y <= SeaLevel)
            return SeaLevel - y > 2 || Vegetation.ShoreGravel(wx, wz) > 0.5f ? BlockType.Gravel : BlockType.Sand;
        if (above <= 3)
            return slope >= 3 ? BlockType.Granite : Vegetation.ShoreGravel(wx, wz) > 0.45f ? BlockType.Gravel : BlockType.Sand;
        if (slope >= 4 || above > 55) return BlockType.Granite;
        if (above > 45) return slope >= 2 ? BlockType.Granite : BlockType.Dirt;
        if (above > 6 && Vegetation.BarePatch(wx, wz) > 0.8f && slope <= 1) return BlockType.Dirt;
        return BlockType.Grass;
    }

    private static void ApplyBathymetry(short[] map, int size)
    {
        const int inf = 60000;
        var distance = new ushort[map.Length];
        for (int i = 0; i < map.Length; i++)
            distance[i] = map[i] > 0 ? (ushort)0 : (ushort)inf;

        for (int z = 0; z < size; z++)
        for (int x = 0; x < size; x++)
        {
            int i = z * size + x;
            int d = distance[i];
            if (x > 0) d = Math.Min(d, distance[i - 1] + 3);
            if (z > 0)
            {
                d = Math.Min(d, distance[i - size] + 3);
                if (x > 0) d = Math.Min(d, distance[i - size - 1] + 4);
                if (x < size - 1) d = Math.Min(d, distance[i - size + 1] + 4);
            }
            distance[i] = (ushort)Math.Min(d, inf);
        }

        for (int z = size - 1; z >= 0; z--)
        for (int x = size - 1; x >= 0; x--)
        {
            int i = z * size + x;
            int d = distance[i];
            if (x < size - 1) d = Math.Min(d, distance[i + 1] + 3);
            if (z < size - 1)
            {
                d = Math.Min(d, distance[i + size] + 3);
                if (x < size - 1) d = Math.Min(d, distance[i + size + 1] + 4);
                if (x > 0) d = Math.Min(d, distance[i + size - 1] + 4);
            }
            distance[i] = (ushort)Math.Min(d, inf);
        }

        for (int i = 0; i < map.Length; i++)
        {
            if (map[i] > 0) continue;
            float meters = distance[i] / 3f * CellSize;
            float relief = (TileNoise.Rand(i % size, i / size, 9001) - 0.5f) * 1.4f;
            map[i] = (short)-(int)Math.Clamp(MathF.Round(1f + meters * 0.055f + relief), 1f, 28f);
        }
    }
}