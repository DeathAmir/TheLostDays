using System.Numerics;
using LostSpawns.Models;
using LostSpawns.Services;
using Raylib_cs;

namespace TheLostDays.Native;

internal sealed class NativeGame : IDisposable
{
    private readonly NativeWorld _world;
    private readonly Dictionary<(int X, int Z), ChunkData> _chunks = new();
    private readonly Dictionary<(int X, int Z), NativeChunkMesh> _meshes = new();
    private readonly Queue<(int X, int Z)> _pending = new();
    private readonly HashSet<(int X, int Z)> _queued = new();
    private readonly PlayerStatsService _stats = new();
    private readonly InventoryService _inventory;
    private readonly CraftingService _crafting;
    private readonly WeatherService _weather = new();
    private readonly WorldTimeService _time = new();
    private readonly Material _material;
    private Camera3D _camera;
    private Vector3 _position;
    private float _yaw = -90f;
    private float _pitch = -12f;
    private int _centerX = int.MinValue;
    private int _centerZ = int.MinValue;
    private int _activeSlot;
    private bool _inventoryOpen;
    private bool _craftingOpen;
    private bool _paused;
    private float _jumpVelocity;
    private bool _grounded = true;
    private float _saveFlash;
    private int _fps;

    public NativeGame()
    {
        _inventory = new InventoryService(_stats);
        _crafting = new CraftingService(_inventory, _stats);
        _world = NativeWorld.Load();
        var spawn = _world.FindSpawn();
        _position = new Vector3(spawn.X, _world.GetHeight(spawn.X, spawn.Z) + 1.8f, spawn.Z);
        _material = Raylib.LoadMaterialDefault();
        UpdateCamera();
        RefreshChunkWindow(true);
    }

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            float dt = Math.Clamp(Raylib.GetFrameTime(), 0f, 0.05f);
            if (!_paused && !_inventoryOpen && !_craftingOpen)
                Update(dt);
            ProcessChunkQueue(1);
            Draw();
        }
    }

    private void Update(float dt)
    {
        var mouse = Raylib.GetMouseDelta();
        _yaw += mouse.X * 0.12f;
        _pitch = Math.Clamp(_pitch - mouse.Y * 0.12f, -85f, 85f);

        bool sprinting = (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift)) && _stats.Stamina > 0.05f;
        float speed = sprinting ? 8.5f : 4.2f;
        float yaw = _yaw * MathF.PI / 180f;
        Vector3 forward = Vector3.Normalize(new(MathF.Cos(yaw), 0, MathF.Sin(yaw)));
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 movement = Vector3.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W)) movement += forward;
        if (Raylib.IsKeyDown(KeyboardKey.S)) movement -= forward;
        if (Raylib.IsKeyDown(KeyboardKey.D)) movement += right;
        if (Raylib.IsKeyDown(KeyboardKey.A)) movement -= right;
        if (movement.LengthSquared() > 0)
        {
            movement = Vector3.Normalize(movement);
            _position += movement * speed * dt;
            _stats.AddDistance(movement.Length() * speed * dt);
        }

        float ground = _world.GetHeight(_position.X, _position.Z) + 1.8f;
        if (Raylib.IsKeyPressed(KeyboardKey.Space) && _grounded)
        {
            _jumpVelocity = 6.5f;
            _grounded = false;
        }
        _jumpVelocity -= 18f * dt;
        _position.Y += _jumpVelocity * dt;
        if (_position.Y <= ground)
        {
            _position.Y = ground;
            _jumpVelocity = 0;
            _grounded = true;
        }

        _stats.Tick(dt, _time.AmbientColor.Y, sprinting);
        _time.Tick(dt);
        _weather.Tick(dt);
        if (_stats.Health <= 0)
        {
            _stats.RecordDeath();
            _stats.ResetToDefaults();
            _position.Y = ground;
        }

        HandleKeys();
        HandleWorldInteraction();
        UpdateCamera();
        RefreshChunkWindow(false);
    }

    private void HandleKeys()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            _paused = !_paused;
            if (_paused) Raylib.EnableCursor(); else Raylib.DisableCursor();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            _inventoryOpen = !_inventoryOpen;
            if (_inventoryOpen) Raylib.EnableCursor(); else Raylib.DisableCursor();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.C))
        {
            _craftingOpen = !_craftingOpen;
            if (_craftingOpen) Raylib.EnableCursor(); else Raylib.DisableCursor();
        }

        for (int i = 0; i < 9; i++)
        {
            if (Raylib.IsKeyPressed((KeyboardKey)((int)KeyboardKey.One + i)))
                _activeSlot = i;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.G))
            UseFirstItem(ItemCategory.Food);
        if (Raylib.IsKeyPressed(KeyboardKey.T))
            UseFirstItem(ItemCategory.Drink);
        if (Raylib.IsKeyPressed(KeyboardKey.H))
            UseFirstItem(ItemCategory.Medical);

        if (Raylib.IsKeyPressed(KeyboardKey.F5))
            _saveFlash = 2f;

        if (Raylib.IsKeyPressed(KeyboardKey.F))
        {
            foreach (var recipe in _crafting.Recipes)
            {
                if (_crafting.TryCraft(recipe))
                {
                    _saveFlash = 2f;
                    break;
                }
            }
        }

        if (_saveFlash > 0) _saveFlash -= Raylib.GetFrameTime();
    }

    private void UseFirstItem(ItemCategory category)
    {
        for (int i = 0; i < InventoryService.HotbarSize; i++)
        {
            var item = _inventory.GetHotbar(i);
            if (item?.Category == category && _inventory.TryUseSlot(true, i))
                return;
        }

        for (int i = 0; i < InventoryService.BackpackSize; i++)
        {
            var item = _inventory.GetBackpack(i);
            if (item?.Category == category && _inventory.TryUseSlot(false, i))
                return;
        }
    }

    private void HandleWorldInteraction()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left))
            return;

        Ray ray = Raylib.GetMouseRay(new Vector2(Raylib.GetScreenWidth() * 0.5f, Raylib.GetScreenHeight() * 0.5f), _camera);
        for (float distance = 0.25f; distance <= 6f; distance += 0.2f)
        {
            Vector3 p = ray.Position + ray.Direction * distance;
            int wx = (int)MathF.Floor(p.X);
            int wy = (int)MathF.Floor(p.Y);
            int wz = (int)MathF.Floor(p.Z);
            int cx = NativeWorld.FloorDiv(wx, ChunkData.SizeXZ);
            int cz = NativeWorld.FloorDiv(wz, ChunkData.SizeXZ);
            if (!_chunks.TryGetValue((cx, cz), out var chunk)) continue;
            int lx = wx - cx * ChunkData.SizeXZ;
            int lz = wz - cz * ChunkData.SizeXZ;
            BlockType block = chunk.GetBlock(lx, wy, lz);
            if (block == BlockType.Air || block == BlockType.Water) continue;
            chunk.SetBlock(lx, wy, lz, BlockType.Air);
            if (_meshes.Remove((cx, cz), out var oldMesh)) oldMesh.Dispose();
            _meshes[(cx, cz)] = new NativeChunkMesh(chunk);
            _stats.AwardXp(block is BlockType.Wood or BlockType.Birch ? 2 : 1);
            _inventory.TryAdd(new InventoryItem("material." + block.ToString().ToLowerInvariant(), block.ToString(), 1, ItemCategory.Material));
            break;
        }
    }

    private void RefreshChunkWindow(bool initial)
    {
        int cx = NativeWorld.FloorDiv((int)MathF.Floor(_position.X), ChunkData.SizeXZ);
        int cz = NativeWorld.FloorDiv((int)MathF.Floor(_position.Z), ChunkData.SizeXZ);
        if (!initial && cx == _centerX && cz == _centerZ) return;
        _centerX = cx;
        _centerZ = cz;

        for (int z = cz - 1; z <= cz + 1; z++)
        for (int x = cx - 1; x <= cx + 1; x++)
        {
            var key = (x, z);
            if (_meshes.ContainsKey(key) || _queued.Contains(key)) continue;
            _pending.Enqueue(key);
            _queued.Add(key);
        }

        foreach (var key in _meshes.Keys.Where(k => Math.Abs(k.X - cx) > 2 || Math.Abs(k.Z - cz) > 2).ToArray())
        {
            _meshes[key].Dispose();
            _meshes.Remove(key);
            _chunks.Remove(key);
        }
    }

    private void ProcessChunkQueue(int count)
    {
        while (count-- > 0 && _pending.Count > 0)
        {
            var key = _pending.Dequeue();
            _queued.Remove(key);
            if (_meshes.ContainsKey(key)) continue;
            var chunk = _world.GenerateChunk(key.X, key.Z);
            _chunks[key] = chunk;
            _meshes[key] = new NativeChunkMesh(chunk);
        }
    }

    private void UpdateCamera()
    {
        float yaw = _yaw * MathF.PI / 180f;
        float pitch = _pitch * MathF.PI / 180f;
        Vector3 direction = Vector3.Normalize(new(
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

    private void Draw()
    {
        float daylight = Math.Clamp(MathF.Sin((_time.DayFraction - 0.25f) * MathF.Tau) * 0.5f + 0.5f, 0.08f, 1f);
        var sky = new Color((byte)(14 + 86 * daylight), (byte)(22 + 112 * daylight), (byte)(34 + 145 * daylight), 255);

        Raylib.BeginDrawing();
        Raylib.ClearBackground(sky);
        Raylib.BeginMode3D(_camera);

        foreach (var mesh in _meshes.Values)
            mesh.Draw(_material);

        Raylib.DrawPlane(new Vector3(_position.X, NativeWorld.SeaLevel - 0.15f, _position.Z), new Vector2(10000, 10000), new Color(28, 82, 112, 255));
        Raylib.EndMode3D();

        DrawHud();
        Raylib.EndDrawing();
    }

    private void DrawHud()
    {
        int w = Raylib.GetScreenWidth();
        Raylib.DrawRectangle(18, 18, 330, 148, new Color(8, 13, 18, 220));
        Raylib.DrawText("THE LOST DAYS", 32, 29, 24, Color.White);
        Raylib.DrawText("DEER ISLE  |  NATIVE BUILD", 34, 58, 12, new Color(110, 205, 165, 255));
        Raylib.DrawText($"DAY {_time.DayNumber}   {( _weather.IsRaining ? "RAIN" : "CLEAR" )}", 34, 80, 15, Color.RayWhite);
        Raylib.DrawText($"POS  {_position.X:0} / {_position.Y:0} / {_position.Z:0}", 34, 103, 13, Color.RayWhite);
        Raylib.DrawText($"CHUNKS {_meshes.Count}   FPS {Raylib.GetFPS()}", 34, 126, 13, Color.RayWhite);

        string[] names = ["HEALTH", "STAMINA", "HUNGER", "THIRST", "WARMTH"];
        float[] values = [_stats.Health, _stats.Stamina, _stats.Hunger, _stats.Thirst, _stats.Temperature];
        int[] colors = [0xD94B4B, 0x59C77B, 0xDDA84B, 0x45A9D6, 0xE58D48];
        for (int i = 0; i < values.Length; i++)
        {
            int y = 185 + i * 27;
            Raylib.DrawText(names[i], 24, y, 12, Color.RayWhite);
            Raylib.DrawRectangle(105, y + 2, 170, 12, new Color(28, 34, 40, 230));
            int color = colors[i];
            Raylib.DrawRectangle(105, y + 2, (int)(170 * Math.Clamp(values[i], 0, 1)), 12, new Color((byte)(color >> 16), (byte)(color >> 8), (byte)color, 255));
        }

        for (int i = 0; i < InventoryService.HotbarSize; i++)
        {
            int x = w / 2 - 225 + i * 50;
            bool active = i == _activeSlot;
            Raylib.DrawRectangle(x, Raylib.GetScreenHeight() - 72, 44, 44, active ? new Color(177, 137, 71, 245) : new Color(12, 18, 24, 220));
            Raylib.DrawRectangleLines(x, Raylib.GetScreenHeight() - 72, 44, 44, active ? Color.Gold : new Color(110, 125, 135, 255));
            var item = _inventory.GetHotbar(i);
            if (item != null)
                Raylib.DrawText(item.Name.Length > 6 ? item.Name[..6] : item.Name, x + 3, Raylib.GetScreenHeight() - 57, 9, Color.White);
            Raylib.DrawText((i + 1).ToString(), x + 3, Raylib.GetScreenHeight() - 27, 9, Color.RayWhite);
        }

        Raylib.DrawText("WASD MOVE  |  SHIFT RUN  |  SPACE JUMP  |  LMB MINE  |  TAB PACK  |  C CRAFT  |  G/T/H USE  |  F CRAFT", 20, Raylib.GetScreenHeight() - 20, 12, Color.RayWhite);

        if (_inventoryOpen)
        {
            Raylib.DrawRectangle(w / 2 - 250, 100, 500, 390, new Color(7, 12, 18, 245));
            Raylib.DrawText("BACKPACK", w / 2 - 220, 120, 26, Color.White);
            for (int i = 0; i < InventoryService.BackpackSize; i++)
            {
                int x = w / 2 - 220 + (i % 8) * 52;
                int y = 165 + (i / 8) * 58;
                Raylib.DrawRectangle(x, y, 46, 48, new Color(25, 33, 40, 255));
                var item = _inventory.GetBackpack(i);
                if (item != null)
                    Raylib.DrawText(item.Name.Length > 7 ? item.Name[..7] : item.Name, x + 2, y + 15, 9, Color.RayWhite);
            }
            Raylib.DrawText("Press TAB to close", w / 2 - 90, 445, 14, Color.Gray);
        }

        if (_craftingOpen)
        {
            Raylib.DrawRectangle(w / 2 - 220, 150, 440, 300, new Color(7, 12, 18, 245));
            Raylib.DrawText("CRAFTING", w / 2 - 190, 172, 25, Color.White);
            for (int i = 0; i < _crafting.Recipes.Count; i++)
            {
                var recipe = _crafting.Recipes[i];
                Raylib.DrawText($"{i + 1}. {recipe.DisplayName}  [{(_crafting.CanCraft(recipe) ? "READY" : "MISSING")}]",
                    w / 2 - 185, 218 + i * 28, 15, Color.RayWhite);
            }
            Raylib.DrawText("Press F to craft first available recipe", w / 2 - 180, 405, 13, Color.Gray);
        }

        if (_paused)
        {
            Raylib.DrawRectangle(0, 0, w, Raylib.GetScreenHeight(), new Color(0, 0, 0, 165));
            Raylib.DrawText("PAUSED", w / 2 - 75, Raylib.GetScreenHeight() / 2 - 35, 34, Color.White);
            Raylib.DrawText("ESC TO RESUME", w / 2 - 72, Raylib.GetScreenHeight() / 2 + 12, 15, Color.RayWhite);
        }

        if (_saveFlash > 0)
            Raylib.DrawText("ACTION COMPLETE", w - 220, 24, 15, Color.Gold);

        Raylib.DrawCircle(w / 2, Raylib.GetScreenHeight() / 2, 2, Color.White);
    }

    public void Dispose()
    {
        foreach (var mesh in _meshes.Values)
            mesh.Dispose();
        _meshes.Clear();
        Raylib.UnloadMaterial(_material);
    }
}