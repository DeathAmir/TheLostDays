# The Lost Days — Native Windows Port

The native client is a standalone .NET 10 desktop game. It does not host Blazor, WebAssembly, JavaScript, WebView, or a browser.

## Native stack

- Raylib-cs 8.1.0 for the native window, input, 3D camera, and rendering.
- .NET 10 self-contained Windows x64 publishing.
- The existing Deer Isle raw Int16 heightmap is reused directly when available.

## Build

```powershell
dotnet restore .\TheLostDays.Native\TheLostDays.Native.csproj
dotnet run --project .\TheLostDays.Native\TheLostDays.Native.csproj
dotnet publish .\TheLostDays.Native\TheLostDays.Native.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Native milestone

The current native slice uses Raylib's higher-level 3D API instead of a hand-written SDL/OpenGL renderer. It finds a dry-land spawn on Deer Isle, draws terrain and water, adds deterministic trees, and supports first-person WASD movement, sprint, and mouse look. It falls back to procedural terrain if the heightmap is unavailable.

This is an early native gameplay foundation. Inventory, crafting, survival stats, wildlife, weather, audio, save/load, detailed voxel textures, multiplayer, and the full HUD still need native implementations. The original browser branch remains unchanged.

## Controls

- WASD: move
- Shift: sprint
- Mouse: look
- Escape: quit
