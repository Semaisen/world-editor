# World Editor

Standalone terrain editor vertical slice for heightmap terrain intended for Godot export.

## Requirements

- .NET 10 SDK

## Run

```powershell
dotnet run --project src\WorldEditor.App\WorldEditor.App.csproj
```

## UI And Controls

The editor uses a dark charcoal theme with an orange accent, arranged in four regions:

- Top header bar: app title plus New / Save / Load and an accented Export button
- Left tool rail: icon buttons for Raise/Lower, Smooth, Flatten, Paint, Erosion, Hydraulic, and Tile Mode (the active tool is highlighted in orange; hover for tooltips)
- Right properties panel: contextual sections for the active tool, brush settings, view mode, and camera speed
- Bottom status bar: cursor position, tile coordinate, active tool, camera speed, and the last action

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
- Paint tool + left mouse drag: softly blend the selected color into terrain albedo
- Erosion tool + left mouse drag: weather slopes steeper than the talus angle, moving material into lower neighbours
- Hydraulic tool + left mouse drag: rain droplets inside the brush; they flow downhill far beyond it until the water evaporates, carving channels and depositing sediment (Rain Rate slider controls droplets per second)
- Brush Shape: choose Circle, Square, or Noise footprints for sculpt and paint tools
- Noise brush: use Noise Scale and Noise Amount to vary brush strength procedurally
- Tile Mode button: click exposed ghost tiles to add terrain chunks
- Tile Mode + Ctrl + left click: remove a tile when the terrain stays connected
- Added tiles blend from connected edges, and sculpt brushes affect every tile under the brush radius.
- 1/2: decrease/increase brush radius
- 3/4: decrease/increase active brush strength
- Ctrl+Z: undo the last terrain edit
- Ctrl+Y: redo the last undone terrain edit
- Ctrl+S: save `SampleTerrainProject`
- Ctrl+O: load `SampleTerrainProject`
- Ctrl+P: export `GodotExport`

The editor uses a lighter preview mesh for interactive rendering. Project save/load and Godot export use the full `2501 x 2501` heightmap.

## Test

```powershell
dotnet test WorldEditor.slnx
```
