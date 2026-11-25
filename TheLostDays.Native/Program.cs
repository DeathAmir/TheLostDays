using SDL3;
using TheLostDays.Native;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!SDL.Init(SDL.InitFlags.Video))
        {
            Console.Error.WriteLine($"SDL initialization failed: {SDL.GetError()}");
            return 1;
        }

        IntPtr window = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;

        try
        {
            SDL.GLSetAttribute(SDL.GLAttr.ContextMajorVersion, 3);
            SDL.GLSetAttribute(SDL.GLAttr.ContextMinorVersion, 3);
            SDL.GLSetAttribute(SDL.GLAttr.ContextProfileMask, (int)SDL.GLProfile.Core);
            SDL.GLSetAttribute(SDL.GLAttr.DoubleBuffer, 1);
            SDL.GLSetAttribute(SDL.GLAttr.DepthSize, 24);

            window = SDL.CreateWindow(
                "The Lost Days",
                1600,
                900,
                SDL.WindowFlags.OpenGL | SDL.WindowFlags.Resizable);

            if (window == IntPtr.Zero)
                throw new InvalidOperationException($"Window creation failed: {SDL.GetError()}");

            context = SDL.GLCreateContext(window);
            if (context == IntPtr.Zero)
                throw new InvalidOperationException($"OpenGL context creation failed: {SDL.GetError()}");

            SDL.GLSetSwapInterval(1);
            using var game = new NativeGame(window);
            game.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (context != IntPtr.Zero)
                SDL.GLDestroyContext(context);
            if (window != IntPtr.Zero)
                SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}
