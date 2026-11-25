using System.Buffers.Binary;
using System.Numerics;
using Raylib_cs;

namespace TheLostDays.Native;

internal readonly record struct TerrainTriangle(Vector3 A, Vector3 B, Vector3 C, Color Color);

internal sealed class NativeWorld
{
    public const int SeaLevel = 64;
    private readonly short[]? _heightmap;
    private readonly int _mapSize;
    private const float CellSize = 15.62f;

    private NativeWorld(short[]? heightmap, int mapSize)
    {
        _heightmap = heightmap;
        _mapSize = mapSize;
    }

    public static NativeWorld Load()
    {
        string[] paths =
        [
            Path.Combine(AppContext.BaseDirectory, "Data", "deer_isle.heightmap"),
            Path.Combine(AppContext.BaseDirectory, "deer_isle.heightmap"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LostSpawns", "wwwroot", "maps", "deer_isle.heightmap"))
        ];

        foreach (string path in paths)
        {
            if (!File.Exists(path)) continue;
            byte[] bytes = File.ReadAllBytes(path);
            int samples = bytes.Length / 2;
            int size = (int)Math.Sqrt(samples);
            if (size * size != samples) continue;

            var values = new short[samples];
            for (int i = 0; i < samples; i++)
                values[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2));

            ApplyBathymetry(values, size);
            return new NativeWorld(values, size);
        }

        return new NativeWorld(null, 0);
    }

    public (float X, float Z) FindSpawn()
    {
        if (_heightmap == null) return (0, 0);
        if (GetHeight(0, 0) > SeaLevel + 4) return (0, 0);

        for (int search = 1; search < 200; search += 4)
        {
            float candidate = search * 16f;
            if (GetHeight(candidate, candidate) > SeaLevel + 4)
                return (candidate, candidate);
        }

        for (int radius = 64; radius <= 4096; radius += 64)
        {
            for (int angle = 0; angle < 360; angle += 15)
            {
                float radians = angle * MathF.PI / 180f;
                float x = MathF.Cos(radians) * radius;
                float z = MathF.Sin(radians) * radius;
                if (GetHeight(x, z) > SeaLevel + 4)
                    return (x, z);
            }
        }

        return (0, 0);
    }

    public int GetHeight(float worldX, float worldZ)
    {
        if (_heightmap == null)
        {
            float broad = Noise(worldX * 0.0015f, worldZ * 0.0015f);
            float detail = Noise(worldX * 0.009f + 71, worldZ * 0.009f - 23);
            return Math.Clamp(79 + (int)(broad * 25 + detail * 4), 4, 180);
        }

        float mapMeters = _mapSize * CellSize;
        float gx = Math.Clamp((worldX + mapMeters * 0.5f) / CellSize, 0, _mapSize - 1.001f);
        float gz = Math.Clamp((worldZ + mapMeters * 0.5f) / CellSize, 0, _mapSize - 1.001f);
        int x = Math.Clamp((int)gx, 0, _mapSize - 2);
        int z = Math.Clamp((int)gz, 0, _mapSize - 2);
        float fx = gx - x;
        float fz = gz - z;
        float a = _heightmap[z * _mapSize + x];
        float b = _heightmap[z * _mapSize + x + 1];
        float c = _heightmap[(z + 1) * _mapSize + x];
        float d = _heightmap[(z + 1) * _mapSize + x + 1];
        return SeaLevel + (int)MathF.Round(
            a * (1 - fx) * (1 - fz) +
            b * fx * (1 - fz) +
            c * (1 - fx) * fz +
            d * fx * fz);
    }

    public List<TerrainTriangle> BuildMesh(float centerX, float centerZ, int radius, int step)
    {
        var triangles = new List<TerrainTriangle>((radius * 2 / step) * (radius * 2 / step) * 2);
        int minX = (int)MathF.Floor((centerX - radius) / step) * step;
        int minZ = (int)MathF.Floor((centerZ - radius) / step) * step;
        int maxX = (int)MathF.Ceiling((centerX + radius) / step) * step;
        int maxZ = (int)MathF.Ceiling((centerZ + radius) / step) * step;

        for (int z = minZ; z < maxZ; z += step)
        for (int x = minX; x < maxX; x += step)
        {
            int h00 = GetHeight(x, z);
            int h10 = GetHeight(x + step, z);
            int h01 = GetHeight(x, z + step);
            int h11 = GetHeight(x + step, z + step);
            int average = (h00 + h10 + h01 + h11) / 4;
            if (average <= SeaLevel) continue;

            var color = TerrainColor(average, h00, h10, h01, h11);
            var a = new Vector3(x, h00, z);
            var b = new Vector3(x, h01, z + step);
            var c = new Vector3(x + step, h11, z + step);
            var d = new Vector3(x + step, h10, z);

            triangles.Add(new TerrainTriangle(a, b, c, color));
            triangles.Add(new TerrainTriangle(a, c, d, color));
        }

        return triangles;
    }

    private static Color TerrainColor(int height, int h00, int h10, int h01, int h11)
    {
        int slope = Math.Max(
            Math.Max(Math.Abs(h00 - h10), Math.Abs(h00 - h01)),
            Math.Max(Math.Abs(h11 - h10), Math.Abs(h11 - h01)));

        if (height <= SeaLevel + 2) return new Color(194, 174, 119, 255);
        if (slope >= 5 || height >= SeaLevel + 52) return new Color(112, 116, 105, 255);
        if (height <= SeaLevel + 7) return new Color(194, 177, 126, 255);
        if (height >= SeaLevel + 38) return new Color(105, 111, 83, 255);
        return new Color(74, 112, 65, 255);
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
            float relief = (Hash(i % size, i / size) % 1000) / 1000f;
            map[i] = (short)-(int)Math.Clamp(MathF.Round(1f + meters * 0.055f + (relief - 0.5f) * 1.4f), 1f, 28f);
        }
    }

    public static uint Hash(int x, int z)
    {
        uint h = unchecked((uint)(x * 374761393 + z * 668265263 + 42 * 1442695041));
        h = (h ^ (h >> 13)) * 1274126177;
        return h ^ (h >> 16);
    }

    private static float Noise(float x, float z)
    {
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        float fx = x - x0;
        float fz = z - z0;
        fx = fx * fx * (3 - 2 * fx);
        fz = fz * fz * (3 - 2 * fz);
        float a = Hash(x0, z0) / (float)uint.MaxValue;
        float b = Hash(x0 + 1, z0) / (float)uint.MaxValue;
        float c = Hash(x0, z0 + 1) / (float)uint.MaxValue;
        float d = Hash(x0 + 1, z0 + 1) / (float)uint.MaxValue;
        return ((a + (b - a) * fx) * (1 - fz) + (c + (d - c) * fx) * fz) * 2 - 1;
    }
}
