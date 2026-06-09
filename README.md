# World Editor

Standalone terrain editor vertical slice for heightmap terrain intended for Godot export.

## Requirements

- .NET 10 SDK

## Run

```powershell
dotnet run --project src\WorldEditor.App\WorldEditor.App.csproj
```

## UI And Controls

The app has a simple left-hand toolbar for brush settings, project actions, and cursor readout.
The terrain preview uses directional lighting, slope shading, and subtle height tinting so sculpted height changes are visible even before texture painting exists.

- Right mouse drag: rotate camera
- Middle mouse drag: pan camera
- Mouse wheel: increase/decrease camera movement speed
- WASD: move camera
- Q/E: move camera down/up
- Shift while moving: double camera movement speed
- Left mouse drag: raise terrain
- Ctrl + left mouse drag: lower terrain
- Smooth tool + left mouse drag: smooth height changes
- Flatten tool + left mouse drag: flatten to the height sampled when the stroke starts
- Tile Mode button: click exposed ghost tiles to add terrain chunks
- Tile Mode + Ctrl + left click: remove a tile when the terrain stays connected
- Added tiles blend from connected edges, and sculpt brushes affect every tile under the brush radius.
- 1/2: decrease/increase brush radius
- 3/4: decrease/increase brush strength
- Ctrl+S: save `SampleTerrainProject`
- Ctrl+O: load `SampleTerrainProject`
- Ctrl+P: export `GodotExport`

The editor uses a lighter preview mesh for interactive rendering. Project save/load and Godot export use the full `2501 x 2501` heightmap.

## Test

```powershell
dotnet test WorldEditor.slnx
```
