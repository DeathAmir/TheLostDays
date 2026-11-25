using System.Diagnostics;
using SDL3;

namespace TheLostDays.Native;

internal sealed class NativeGame : IDisposable
{
    private readonly IntPtr _window;
    private readonly NativeRenderer _renderer;
    private readonly NativeWorld _world;
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _running = true;
    private float _yaw = -90f;
    private float _pitch = -24f;
    private System.Numerics.Vector3 _position;

    public NativeGame(IntPtr window)
    {
        _window = window;
        _world = NativeWorld.Load();
        _position = new System.Numerics.Vector3(0, _world.GetHeight(0, 0) + 3, 12);
        _renderer = new NativeRenderer(window, _world);
    }

    public void Run()
    {
        double previous = _clock.Elapsed.TotalSeconds;
        while (_running)
        {
            while (SDL.PollEvent(out var e))
            {
                if (e.Type == (uint)SDL.EventType.Quit)
                    _running = false;
                else if (e.Type == (uint)SDL.EventType.KeyDown)
                {
                    var key = e.Key.Key.ToString();
                    _keys.Add(key);
                    if (key.Equals("Escape", StringComparison.OrdinalIgnoreCase))
                        _running = false;
                }
                else if (e.Type == (uint)SDL.EventType.KeyUp)
                {
                    _keys.Remove(e.Key.Key.ToString());
                }
            }

            double now = _clock.Elapsed.TotalSeconds;
            float delta = (float)Math.Clamp(now - previous, 0.0, 0.05);
            previous = now;

            Update(delta);
            int width = 0;
            int height = 0;
            SDL.GetWindowSizeInPixels(_window, out width, out height);
            _renderer.Render(_position, _yaw, _pitch, Math.Max(width, 1), Math.Max(height, 1));
            SDL.GLSwapWindow(_window);
        }
    }

    private void Update(float delta)
    {
        if (_keys.Contains("Left")) _yaw -= 90f * delta;
        if (_keys.Contains("Right")) _yaw += 90f * delta;
        if (_keys.Contains("Up")) _pitch = Math.Clamp(_pitch + 65f * delta, -85f, 85f);
        if (_keys.Contains("Down")) _pitch = Math.Clamp(_pitch - 65f * delta, -85f, 85f);

        float yaw = MathF.PI / 180f * _yaw;
        var forward = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)));
        var right = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(forward, System.Numerics.Vector3.UnitY));
        float speed = (_keys.Contains("LeftShift") || _keys.Contains("RightShift")) ? 18f : 7f;
        if (_keys.Contains("W")) _position += forward * speed * delta;
        if (_keys.Contains("S")) _position -= forward * speed * delta;
        if (_keys.Contains("A")) _position -= right * speed * delta;
        if (_keys.Contains("D")) _position += right * speed * delta;
        _position.Y = Math.Max(_position.Y, _world.GetHeight(_position.X, _position.Z) + 1.8f);
    }

    public void Dispose() => _renderer.Dispose();
}
