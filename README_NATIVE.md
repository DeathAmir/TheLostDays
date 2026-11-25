# The Lost Days — Native Windows Port

The native client is a separate .NET 10 desktop application. It does not host Blazor, WebAssembly, JavaScript, WebView, or a browser.

## Stack

- SDL3-CS for the native window, events, and input.
- OpenTK OpenGL 3.3 Core for 3D rendering.
- .NET 10 self-contained Windows x64 publishing.

## Build

```powershell
dotnet restore .\TheLostDays.Native\TheLostDays.Native.csproj
dotnet run --project .\TheLostDays.Native\TheLostDays.Native.csproj
dotnet publish .\TheLostDays.Native\TheLostDays.Native.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Current native milestone

The first native slice creates an SDL3 desktop window and OpenGL context, loads the existing Deer Isle elevation data when it is present, builds a terrain mesh, and supports first-person WASD movement, sprint, and arrow-key camera control. When the heightmap is unavailable, it falls back to deterministic procedural terrain.

This is the foundation of the port, not a claim that every browser feature has already been migrated. Survival systems, inventory, crafting, wildlife, weather, audio, saves, HUD, and VR still need native implementations and integration. The existing browser project remains untouched.

## Controls

- WASD: move
- Shift: sprint
- Arrow keys: look
- Escape: quit
