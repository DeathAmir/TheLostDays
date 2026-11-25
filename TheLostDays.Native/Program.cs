using Raylib_cs;
using TheLostDays.Native;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
            Raylib.InitWindow(1600, 900, "The Lost Days");
            Raylib.SetTargetFPS(60);
            Raylib.SetExitKey(KeyboardKey.Escape);
            Raylib.DisableCursor();

            using var game = new NativeGame();
            game.Run();
            Raylib.CloseWindow();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            try { Raylib.CloseWindow(); } catch { }
            return 1;
        }
    }
}
