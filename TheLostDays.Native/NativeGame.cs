using System.Numerics;
using Raylib_cs;

namespace TheLostDays.Native;

internal sealed class NativeGame : IDisposable
{
    private readonly NativeWorld _world;
    private List<TerrainTriangle> _terrain = [];
    private Vector3 _position;
    private float _yaw = -90f;
    private float _pitch = -12f;
    private float _meshCenterX = float.NaN;
    private float _meshCenterZ = float.NaN;
    private Camera3D _camera;
    private double _elapsed;
    private int _fps;

    public NativeGame()
    {
        _world = NativeWorld.Load();
        var spawn = _world.FindSpawn();
        _position = new Vector3(spawn.X, _world.GetHeight(spawn.X, spawn.Z) + 1.85f, spawn.Z);
        UpdateCamera();
        RebuildTerrain();
    }

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            float delta = Math.Clamp(Raylib.GetFrameTime(), 0f, 0.05f);
            _elapsed += delta;
            Update(delta);
            Draw();
        }
    }

    private void Update(float delta)
    {
        var mouse = Raylib.GetMouseDelta();
        _yaw += mouse.X * 0.12f;
        _pitch = Math.Clamp(_pitch - mouse.Y * 0.12f, -85f, 85f);

        float yaw = _yaw * MathF.PI / 180f;
        var forward = Vector3.Normalize(new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)));
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        float speed = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift) ? 18f : 7f;
        Vector3 movement = Vector3.Zero;

        if (Raylib.IsKeyDown(KeyboardKey.W)) movement += forward;
        if (Raylib.IsKeyDown(KeyboardKey.S)) movement -= forward;
        if (Raylib.IsKeyDown(KeyboardKey.D)) movement += right;
        if (Raylib.IsKeyDown(KeyboardKey.A)) movement -= right;

        if (movement.LengthSquared() > 0)
            _position += Vector3.Normalize(movement) * speed * delta;

        _position.Y = _world.GetHeight(_position.X, _position.Z) + 1.85f;
        UpdateCamera();

        if (float.IsNaN(_meshCenterX) ||
            MathF.Abs(_position.X - _meshCenterX) > 28f ||
            MathF.Abs(_position.Z - _meshCenterZ) > 28f)
            RebuildTerrain();
    }

    private void UpdateCamera()
    {
        float yaw = _yaw * MathF.PI / 180f;
        float pitch = _pitch * MathF.PI / 180f;
        var direction = Vector3.Normalize(new Vector3(
            MathF.Cos(yaw) * MathF.Cos(pitch),
            MathF.Sin(pitch),
            MathF.Sin(yaw) * MathF.Cos(pitch)));

        _camera = new Camera3D
        {
            Position = _position,
            Target = _position + direction,
            Up = Vector3.UnitY,
            FovY = 74f,
            Projection = CameraProjection.Perspective
        };
    }

    private void RebuildTerrain()
    {
        _meshCenterX = MathF.Round(_position.X / 16f) * 16f;
        _meshCenterZ = MathF.Round(_position.Z / 16f) * 16f;
        _terrain = _world.BuildMesh(_meshCenterX, _meshCenterZ, 88, 4);
    }

    private void Draw()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(117, 163, 190, 255));
        Raylib.BeginMode3D(_camera);

        Raylib.DrawPlane(
            new Vector3(_meshCenterX, NativeWorld.SeaLevel - 0.2f, _meshCenterZ),
            new Vector2(420, 420),
            new Color(42, 103, 133, 255));

        foreach (var triangle in _terrain)
            Raylib.DrawTriangle3D(triangle.A, triangle.B, triangle.C, triangle.Color);

        DrawVegetation();
        Raylib.EndMode3D();

        Raylib.DrawRectangle(18, 18, 370, 112, new Color(10, 17, 22, 205));
        Raylib.DrawText("THE LOST DAYS", 34, 30, 25, Color.White);
        Raylib.DrawText("NATIVE SURVIVAL PROTOTYPE", 35, 61, 13, new Color(115, 210, 165, 255));
        Raylib.DrawText($"POSITION  {_position.X:0} / {_position.Z:0}", 35, 88, 14, Color.RayWhite);
        Raylib.DrawText("WASD Move   SHIFT Sprint   MOUSE Look   ESC Exit", 22, Raylib.GetScreenHeight() - 34, 17, Color.White);
        Raylib.DrawFPS(Raylib.GetScreenWidth() - 92, 22);
        Raylib.DrawRectangleLines(18, 18, 370, 112, new Color(90, 130, 145, 255));

        Raylib.EndDrawing();
    }

    private void DrawVegetation()
    {
        int startX = (int)MathF.Floor(_meshCenterX / 16f) * 16 - 72;
        int startZ = (int)MathF.Floor(_meshCenterZ / 16f) * 16 - 72;

        for (int z = startZ; z <= startZ + 144; z += 18)
        for (int x = startX; x <= startX + 144; x += 18)
        {
            uint hash = NativeWorld.Hash(x, z);
            if (hash % 100 > 34) continue;
            float offsetX = ((hash >> 8) % 900) / 100f - 4.5f;
            float offsetZ = ((hash >> 18) % 900) / 100f - 4.5f;
            float tx = x + offsetX;
            float tz = z + offsetZ;
            int ground = _world.GetHeight(tx, tz);
            if (ground <= NativeWorld.SeaLevel + 4) continue;

            float trunkHeight = 3.2f + (hash % 30) / 10f;
            var trunk = new Vector3(tx, ground + trunkHeight * 0.5f, tz);
            var crown = new Vector3(tx, ground + trunkHeight + 1.8f, tz);
            Raylib.DrawCube(trunk, 0.55f, trunkHeight, 0.55f, new Color(91, 65, 42, 255));
            var leaves = hash % 3 == 0
                ? new Color(38, 79, 43, 255)
                : new Color(48, 99, 49, 255);
            Raylib.DrawSphere(crown, 2.1f + (hash % 8) * 0.12f, leaves);
        }
    }

    public void Dispose()
    {
    }
}
