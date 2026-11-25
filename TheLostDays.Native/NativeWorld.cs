using System.Buffers.Binary;

namespace TheLostDays.Native;

internal sealed class NativeWorld
{
    public const int SeaLevel = 64;
    private readonly short[]? _heightmap;
    private readonly int _mapSize;
    private readonly float _cellSize = 15.62f;
    private readonly int _seed = 42;

    private NativeWorld(short[]? heightmap, int mapSize)
    {
        _heightmap = heightmap;
        _mapSize = mapSize;
    }

    public static NativeWorld Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "deer_isle.heightmap");
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "deer_isle.heightmap");
        if (File.Exists(path))
        {
            byte[] bytes = File.ReadAllBytes(path);
            int samples = bytes.Length / 2;
            int size = (int)Math.Sqrt(samples);
            if (size * size == samples)
            {
                var values = new short[samples];
                for (int i = 0; i < samples; i++)
                    values[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2));
                ApplyBathymetry(values, size, 15.62f);
                return new NativeWorld(values, size);
            }
        }
        return new NativeWorld(null, 0);
    }

    private static void ApplyBathymetry(short[] map, int size, float cellSize)
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
            float meters = distance[i] / 3f * cellSize;
            float noise = Hash01(i % size, i / size, 9001);
            map[i] = (short)-(int)Math.Clamp(MathF.Round(1f + meters * 0.055f + (noise - 0.5f) * 1.4f), 1f, 28f);
        }
    }

    public int GetHeight(float worldX, float worldZ)
    {
        if (_heightmap == null)
        {
            float n = Noise(worldX * 0.0015f, worldZ * 0.0015f);
            float detail = Noise(worldX * 0.009f + 71, worldZ * 0.009f - 23);
            return Math.Clamp(79 + (int)(n * 25 + detail * 4), 4, 180);
        }

        float mapMeters = _mapSize * _cellSize;
        float gx = Math.Clamp((worldX + mapMeters * 0.5f) / _cellSize, 0, _mapSize - 1.001f);
        float gz = Math.Clamp((worldZ + mapMeters * 0.5f) / _cellSize, 0, _mapSize - 1.001f);
        int x = (int)gx;
        int z = (int)gz;
        float fx = gx - x;
        float fz = gz - z;
        float a = _heightmap[z * _mapSize + x];
        float b = _heightmap[z * _mapSize + x + 1];
        float c = _heightmap[(z + 1) * _mapSize + x];
        float d = _heightmap[(z + 1) * _mapSize + x + 1];
        return SeaLevel + (int)MathF.Round(a * (1 - fx) * (1 - fz) + b * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz);
    }

    public float[] BuildMesh()
    {
        const int radius = 12;
        const int step = 2;
        var vertices = new List<float>(300000);
        for (int z = -radius; z < radius; z += step)
        for (int x = -radius; x < radius; x += step)
        {
            float wx = x * step;
            float wz = z * step;
            int h00 = GetHeight(wx, wz);
            int h10 = GetHeight(wx + step, wz);
            int h01 = GetHeight(wx, wz + step);
            int h11 = GetHeight(wx + step, wz + step);
            var color = TerrainColor(Math.Min(Math.Min(h00, h10), Math.Min(h01, h11)));
            AddTriangle(vertices, wx, h00, wz, wx, h01, wz + step, wx + step, h11, wz + step, color);
            AddTriangle(vertices, wx, h00, wz, wx + step, h11, wz + step, wx + step, h10, wz, color);
            AddSide(vertices, wx, h00, wz, wx + step, h10, wz, color);
            AddSide(vertices, wx + step, h10, wz, wx + step, h11, wz + step, color);
            AddSide(vertices, wx + step, h11, wz + step, wx, h01, wz + step, color);
            AddSide(vertices, wx, h01, wz + step, wx, h00, wz, color);
        }
        return vertices.ToArray();
    }

    private static void AddSide(List<float> v, float ax, int ay, float az, float bx, int by, float bz, (float r,float g,float b) c)
    {
        int low = Math.Min(ay, by);
        if (low <= SeaLevel) low = SeaLevel;
        if (ay == by) return;
        var side = (c.r * 0.62f, c.g * 0.62f, c.b * 0.62f);
        AddTriangle(v, ax, low, az, ax, ay, az, bx, by, bz, side);
        AddTriangle(v, ax, low, az, bx, by, bz, bx, low, bz, side);
    }

    private static void AddTriangle(List<float> v, float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, (float r,float g,float b) color)
    {
        var a = new System.Numerics.Vector3(ax, ay, az);
        var b = new System.Numerics.Vector3(bx, by, bz);
        var c = new System.Numerics.Vector3(cx, cy, cz);
        var n = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(b - a, c - a));
        if (!float.IsFinite(n.X)) n = System.Numerics.Vector3.UnitY;
        AddVertex(v, a, n, color);
        AddVertex(v, b, n, color);
        AddVertex(v, c, n, color);
    }

    private static void AddVertex(List<float> v, System.Numerics.Vector3 p, System.Numerics.Vector3 n, (float r,float g,float b) c)
    {
        v.Add(p.X); v.Add(p.Y); v.Add(p.Z);
        v.Add(n.X); v.Add(n.Y); v.Add(n.Z);
        v.Add(c.r); v.Add(c.g); v.Add(c.b);
    }

    private static (float,float,float) TerrainColor(int h) => h switch
    {
        <= SeaLevel + 1 => (0.64f, 0.57f, 0.39f),
        >= 145 => (0.39f, 0.40f, 0.39f),
        _ => (0.31f, 0.37f, 0.22f)
    };

    private static float Hash01(int x, int z, int seed)
    {
        uint h = unchecked((uint)(x * 374761393 + z * 668265263 + seed * 1442695041));
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    private static float Noise(float x, float z)
    {
        int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
        float fx = x - x0, fz = z - z0;
        fx = fx * fx * (3 - 2 * fx);
        fz = fz * fz * (3 - 2 * fz);
        float a = Hash01(x0, z0, 42);
        float b = Hash01(x0 + 1, z0, 42);
        float c = Hash01(x0, z0 + 1, 42);
        float d = Hash01(x0 + 1, z0 + 1, 42);
        return ((a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fz) * 2 - 1;
    }
}
