using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using WorldEditor.Core;

namespace WorldEditor.App;

internal sealed unsafe class TerrainEditorApp : IDisposable
{
    private const int PreviewStride = 4;
    private const float MinCameraSpeed = 5.0f;
    private const float MaxCameraSpeed = 140.0f;
    private const float CameraSpeedStep = 5.0f;
    private const float CameraSpeedBoost = 2.0f;
    private const float HeaderBarHeight = 44.0f;
    private const float StatusBarHeight = 30.0f;
    private const float SidePanelWidth = 280.0f;
    private const int MaxHistoryStates = 32;
    private const ImGuiWindowFlags HudWindowFlags =
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoScrollbar;
    private static readonly Vector4 AccentColor = new(0.949f, 0.329f, 0.114f, 1.0f);
    private static readonly Vector4 AccentHoverColor = new(1.0f, 0.42f, 0.20f, 1.0f);
    private static readonly Vector4 AccentActiveColor = new(0.80f, 0.26f, 0.08f, 1.0f);
    private readonly IWindow _window;
    private readonly Camera _camera = new();
    private readonly HashSet<Key> _keys = [];
    private readonly List<TerrainWorld> _undoStack = [];
    private readonly List<TerrainWorld> _redoStack = [];
    private TerrainWorld _world = new();
    private TerrainWorld? _strokeBeforeWorld;
    private GL? _gl;
    private IInputContext? _input;
    private ShaderProgram? _shader;
    private ImGuiController? _imgui;
    private MeshBuffer? _terrainMesh;
    private MeshBuffer? _brushMesh;
    private MeshBuffer? _tileOverlayMesh;
    private MeshBuffer? _characterPreviewMesh;
    private Vector2 _lastMouse;
    private Vector2 _mouse;
    private bool _leftMouseDown;
    private bool _leftMousePressed;
    private bool _rightMouseDown;
    private bool _middleMouseDown;
    private bool _meshDirty = true;
    private bool _tileOverlayDirty = true;
    private bool _strokeChanged;
    private bool _tileMode;
    private TerrainTool _terrainTool = TerrainTool.RaiseLower;
    private TerrainBrushShape _brushShape = TerrainBrushShape.Circle;
    private float _brushRadius = 2.0f;
    private float _brushStrength = 1.5f;
    private float _noiseScale = 0.18f;
    private float _noiseAmount = 0.55f;
    private Vector3 _paintColor = new(88.0f / 255.0f, 122.0f / 255.0f, 74.0f / 255.0f);
    private float _paintStrength = 3.0f;
    private float _flattenHeight;
    private bool _hasFlattenHeight;
    private BrushFalloff _brushFalloff = BrushFalloff.Smooth;
    private TerrainViewMode _viewMode = TerrainViewMode.Albedo;
    private Vector3 _cursor = new(64, 0, 64);
    private TerrainCoord _cursorTileCoord;
    private Vector2 _cursorLocal;
    private bool _cursorHasTile = true;
    private bool _cursorCanAddTile;
    private string _lastAction = "Ready";
    private float _mouseWheel;
    private float _cameraSpeed = 35.0f;
    private bool _resourcesDisposed;

    public TerrainEditorApp()
    {
        var options = WindowOptions.Default;
        options.Title = "World Editor";
        options.Size = new Vector2D<int>(1280, 800);
        options.PreferredDepthBufferBits = 24;
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += OnClosing;
    }

    public void Run() => _window.Run();

    public void Dispose()
    {
        DisposeResources();
        _window.Dispose();
    }

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _input = _window.CreateInput();
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => _keys.Add(key);
            keyboard.KeyUp += (_, key, _) => _keys.Remove(key);
        }

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += (_, position) =>
            {
                _mouse = new Vector2(position.X, position.Y);
                UpdateCursor();
            };
            mouse.MouseDown += (_, button) => SetMouseButton(button, true);
            mouse.MouseUp += (_, button) => SetMouseButton(button, false);
            mouse.Scroll += (_, wheel) => _mouseWheel += wheel.Y;
        }

        _gl.Enable(EnableCap.DepthTest);
        _gl.ClearColor(0.078f, 0.078f, 0.086f, 1.0f);
        _shader = new ShaderProgram(_gl, VertexShaderSource, FragmentShaderSource);
        _imgui = new ImGuiController(_gl, _window);
        UpdateViewport();
        RebuildTerrainMesh();
        RebuildBrushMesh();
    }

    private void OnUpdate(double delta)
    {
        var deltaSeconds = (float)delta;
        _imgui?.Update(deltaSeconds, _mouse, _leftMouseDown, _rightMouseDown, _middleMouseDown, _mouseWheel);
        DrawUi();

        var uiHasMouse = _imgui?.WantsMouse ?? false;
        if (!uiHasMouse && _mouseWheel != 0)
        {
            _cameraSpeed = Math.Clamp(_cameraSpeed + (_mouseWheel * CameraSpeedStep), MinCameraSpeed, MaxCameraSpeed);
        }

        var speedBoost = _keys.Contains(Key.ShiftLeft) || _keys.Contains(Key.ShiftRight);
        var moveSpeed = _cameraSpeed * (speedBoost ? CameraSpeedBoost : 1.0f) * deltaSeconds;
        if (_keys.Contains(Key.W)) _camera.Position += _camera.Forward * moveSpeed;
        if (_keys.Contains(Key.S)) _camera.Position -= _camera.Forward * moveSpeed;
        if (_keys.Contains(Key.A)) _camera.Position -= _camera.Right * moveSpeed;
        if (_keys.Contains(Key.D)) _camera.Position += _camera.Right * moveSpeed;
        if (_keys.Contains(Key.Q)) _camera.Position -= Vector3.UnitY * moveSpeed;
        if (_keys.Contains(Key.E)) _camera.Position += Vector3.UnitY * moveSpeed;
        if (!uiHasMouse && _keys.Contains(Key.Number1)) _brushRadius = Math.Max(0.1f, _brushRadius - 8.0f * deltaSeconds);
        if (!uiHasMouse && _keys.Contains(Key.Number2)) _brushRadius = Math.Min(50.0f, _brushRadius + 8.0f * deltaSeconds);
        if (!uiHasMouse && _keys.Contains(Key.Number3))
        {
            if (_terrainTool == TerrainTool.Paint)
            {
                _paintStrength = Math.Max(0.1f, _paintStrength - 4.0f * deltaSeconds);
            }
            else
            {
                _brushStrength = Math.Max(0.1f, _brushStrength - 4.0f * deltaSeconds);
            }
        }

        if (!uiHasMouse && _keys.Contains(Key.Number4))
        {
            if (_terrainTool == TerrainTool.Paint)
            {
                _paintStrength = Math.Min(10.0f, _paintStrength + 4.0f * deltaSeconds);
            }
            else
            {
                _brushStrength = Math.Min(20.0f, _brushStrength + 4.0f * deltaSeconds);
            }
        }

        var mouseDelta = _mouse - _lastMouse;
        if (!uiHasMouse && _rightMouseDown) _camera.Rotate(mouseDelta.X, mouseDelta.Y);
        if (!uiHasMouse && _middleMouseDown) _camera.Pan(mouseDelta.X, mouseDelta.Y);

        if (!uiHasMouse && _tileMode && _leftMousePressed)
        {
            HandleTileModeClick();
        }
        else if (!uiHasMouse && !_tileMode && _leftMouseDown)
        {
            if (_leftMousePressed)
            {
                BeginTerrainStroke();
            }

            var changed = ApplyActiveTerrainTool(deltaSeconds);
            if (changed > 0)
            {
                _meshDirty = true;
                _strokeChanged = true;
            }
        }

        if (_meshDirty) RebuildTerrainMesh();
        UpdateCursor();
        RebuildBrushMesh();
        if (_tileMode && _tileOverlayDirty) RebuildTileOverlayMesh();
        RebuildCharacterPreviewMesh();
        HandleShortcuts();
        _lastMouse = _mouse;
        _leftMousePressed = false;
        _mouseWheel = 0.0f;
    }

    private void OnRender(double delta)
    {
        if (_gl is null || _shader is null || _terrainMesh is null || _brushMesh is null) return;

        UpdateViewport();
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _shader.Use();

        var aspect = Math.Max(1, _window.FramebufferSize.X) / (float)Math.Max(1, _window.FramebufferSize.Y);
        var view = _camera.View;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, aspect, 0.1f, 1000.0f);
        var model = Matrix4x4.Identity;
        _shader.SetMatrix("uModel", model);
        _shader.SetMatrix("uView", view);
        _shader.SetMatrix("uProjection", projection);

        _shader.SetVector("uColor", new Vector4(0.18f, 0.20f, 0.18f, 1.0f));
        _shader.SetVector("uLightDirection", new Vector4(-0.45f, 0.85f, -0.28f, 0.0f));
        _shader.SetInt("uUseLighting", 1);
        _shader.SetInt("uViewMode", (int)_viewMode);
        if (_viewMode == TerrainViewMode.Wireframe)
        {
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
        }

        _terrainMesh.DrawTriangles();
        if (_viewMode == TerrainViewMode.Wireframe)
        {
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        }

        _shader.SetVector("uColor", new Vector4(AccentColor.X, AccentColor.Y, AccentColor.Z, 1.0f));
        _shader.SetInt("uUseLighting", 0);
        if (!_tileMode)
        {
            _brushMesh.DrawLines();
        }

        if (_tileMode && _tileOverlayMesh is not null)
        {
            _shader.SetVector("uColor", new Vector4(0.24f, 0.86f, 1.0f, 1.0f));
            _tileOverlayMesh.DrawLines();
        }

        if (_keys.Contains(Key.T) && _characterPreviewMesh is not null)
        {
            _shader.SetVector("uColor", new Vector4(0.45f, 0.82f, 1.0f, 1.0f));
            _characterPreviewMesh.DrawLines();
        }

        _imgui?.Render();
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        UpdateViewport();
        _lastMouse = _mouse;
        UpdateCursor();
    }

    private void OnClosing()
    {
        DisposeResources();
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed) return;

        _terrainMesh?.Dispose();
        _brushMesh?.Dispose();
        _tileOverlayMesh?.Dispose();
        _characterPreviewMesh?.Dispose();
        _imgui?.Dispose();
        _shader?.Dispose();
        _input?.Dispose();
        _resourcesDisposed = true;
    }

    private void SetMouseButton(MouseButton button, bool pressed)
    {
        switch (button)
        {
            case MouseButton.Right:
                _rightMouseDown = pressed;
                break;
            case MouseButton.Middle:
                _middleMouseDown = pressed;
                break;
            case MouseButton.Left:
                if (pressed && !_leftMouseDown) _leftMousePressed = true;
                _leftMouseDown = pressed;
                if (!pressed)
                {
                    EndTerrainStroke();
                    _hasFlattenHeight = false;
                }
                break;
        }
    }

    private void HandleShortcuts()
    {
        var ctrl = _keys.Contains(Key.ControlLeft) || _keys.Contains(Key.ControlRight);
        if (!ctrl) return;

        if (_keys.Remove(Key.Z))
        {
            Undo();
        }
        else if (_keys.Remove(Key.Y))
        {
            Redo();
        }
        else if (_keys.Remove(Key.S))
        {
            TerrainProjectStore.Save(_world, Path.Combine(Environment.CurrentDirectory, "SampleTerrainProject"));
            _lastAction = "Saved SampleTerrainProject";
        }
        else if (_keys.Remove(Key.O))
        {
            var path = Path.Combine(Environment.CurrentDirectory, "SampleTerrainProject");
            if (Directory.Exists(path))
            {
                PushUndoState(_world);
                _world = TerrainProjectStore.LoadWorld(path);
                _redoStack.Clear();
                _meshDirty = true;
                _tileOverlayDirty = true;
                _lastAction = "Loaded SampleTerrainProject";
            }
        }
        else if (_keys.Remove(Key.P))
        {
            GodotExporter.Export(_world, Path.Combine(Environment.CurrentDirectory, "GodotExport"));
            _lastAction = "Exported GodotExport";
        }
    }

    private void DrawUi()
    {
        DrawHeaderBar();
        DrawToolRail();
        DrawSidePanel();
        DrawStatusBar();
    }

    private void DrawHeaderBar()
    {
        var width = Math.Max(1, _window.Size.X);
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(width, HeaderBarHeight), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14.0f, 0.0f));
        ImGui.Begin("##header", HudWindowFlags);

        ImGui.SetCursorPosY((HeaderBarHeight - ImGui.GetTextLineHeight()) * 0.5f);
        ImGui.Text("World Editor");
        ImGui.SameLine();
        ImGui.TextDisabled("/ Heightmap Terrain");

        var buttonSize = new Vector2(58.0f, 28.0f);
        var exportSize = new Vector2(72.0f, 28.0f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var undoSize = new Vector2(64.0f, 28.0f);
        var buttonsWidth = undoSize.X * 2.0f + buttonSize.X * 3.0f + exportSize.X + spacing * 5.0f;
        var buttonY = (HeaderBarHeight - buttonSize.Y) * 0.5f;

        ImGui.SameLine(width - buttonsWidth - 14.0f);
        ImGui.SetCursorPosY(buttonY);
        ImGui.BeginDisabled(_undoStack.Count == 0);
        if (ImGui.Button("Undo", undoSize))
        {
            Undo();
        }

        ImGui.EndDisabled();
        DrawHoverTooltip("Undo last terrain edit (Ctrl+Z)");

        ImGui.SameLine();
        ImGui.SetCursorPosY(buttonY);
        ImGui.BeginDisabled(_redoStack.Count == 0);
        if (ImGui.Button("Redo", undoSize))
        {
            Redo();
        }

        ImGui.EndDisabled();
        DrawHoverTooltip("Redo last undone terrain edit (Ctrl+Y)");

        ImGui.SameLine();
        ImGui.SetCursorPosY(buttonY);
        if (ImGui.Button("New", buttonSize))
        {
            PushUndoState(_world);
            _world = new TerrainWorld();
            _redoStack.Clear();
            _meshDirty = true;
            _tileOverlayDirty = true;
            _lastAction = "Created new flat terrain";
        }

        DrawHoverTooltip("Replace the world with a new flat terrain");

        ImGui.SameLine();
        ImGui.SetCursorPosY(buttonY);
        if (ImGui.Button("Save", buttonSize))
        {
            TerrainProjectStore.Save(_world, Path.Combine(Environment.CurrentDirectory, "SampleTerrainProject"));
            _lastAction = "Saved SampleTerrainProject";
        }

        DrawHoverTooltip("Save SampleTerrainProject (Ctrl+S)");

        ImGui.SameLine();
        ImGui.SetCursorPosY(buttonY);
        if (ImGui.Button("Load", buttonSize))
        {
            var path = Path.Combine(Environment.CurrentDirectory, "SampleTerrainProject");
            if (Directory.Exists(path))
            {
                PushUndoState(_world);
                _world = TerrainProjectStore.LoadWorld(path);
                _redoStack.Clear();
                _meshDirty = true;
                _tileOverlayDirty = true;
                _lastAction = "Loaded SampleTerrainProject";
            }
            else
            {
                _lastAction = "No SampleTerrainProject folder";
            }
        }

        DrawHoverTooltip("Load SampleTerrainProject (Ctrl+O)");

        ImGui.SameLine();
        ImGui.SetCursorPosY(buttonY);
        if (DrawAccentButton("Export", exportSize))
        {
            GodotExporter.Export(_world, Path.Combine(Environment.CurrentDirectory, "GodotExport"));
            _lastAction = "Exported GodotExport";
        }

        DrawHoverTooltip("Export to GodotExport (Ctrl+P)");

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawToolRail()
    {
        ImGui.SetNextWindowPos(new Vector2(12.0f, HeaderBarHeight + 12.0f), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8.0f, 8.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6.0f, 6.0f));
        ImGui.Begin("##toolrail", HudWindowFlags | ImGuiWindowFlags.AlwaysAutoResize);

        DrawRailToolButton("^", "Raise / Lower", "Left-drag raises, Ctrl lowers", TerrainTool.RaiseLower);
        DrawRailToolButton("~", "Smooth", "Left-drag smooths height changes", TerrainTool.Smooth);
        DrawRailToolButton("=", "Flatten", "Left-drag flattens to the stroke-start height", TerrainTool.Flatten);
        DrawRailToolButton("@", "Paint", "Left-drag blends color into the terrain", TerrainTool.Paint);

        ImGui.Separator();

        var tileSelected = _tileMode;
        if (tileSelected) PushAccentButtonColors();
        if (ImGui.Button("#", new Vector2(36.0f, 36.0f)))
        {
            _tileMode = !_tileMode;
            _tileOverlayDirty = true;
            _lastAction = _tileMode ? "Tile Mode enabled" : "Sculpt mode enabled";
        }

        if (tileSelected) ImGui.PopStyleColor(4);
        DrawHoverTooltip("Tile Mode\nClick ghost tiles to add, Ctrl-click to remove");

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawRailToolButton(string icon, string label, string help, TerrainTool tool)
    {
        var selected = !_tileMode && _terrainTool == tool;
        if (selected) PushAccentButtonColors();
        if (ImGui.Button(icon, new Vector2(36.0f, 36.0f)))
        {
            _terrainTool = tool;
            _hasFlattenHeight = false;
            if (_tileMode)
            {
                _tileMode = false;
                _tileOverlayDirty = true;
            }

            _lastAction = $"{GetToolName(tool)} tool selected";
        }

        if (selected) ImGui.PopStyleColor(4);
        DrawHoverTooltip($"{label}\n{help}");
    }

    private void DrawSidePanel()
    {
        var size = _window.Size;
        var height = Math.Max(1.0f, size.Y - HeaderBarHeight - StatusBarHeight);
        ImGui.SetNextWindowPos(new Vector2(size.X - SidePanelWidth, HeaderBarHeight), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(SidePanelWidth, height), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.Begin("##sidepanel", HudWindowFlags & ~ImGuiWindowFlags.NoScrollbar);

        if (_tileMode)
        {
            DrawSectionHeader("Tile Mode", true);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped("Click a ghost tile to add terrain. Ctrl-click a tile to remove it while the terrain stays connected.");
            ImGui.PopStyleColor();
        }
        else
        {
            DrawSectionHeader(GetToolName(_terrainTool), true);
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextWrapped(GetToolHelpText());
            ImGui.PopStyleColor();

            DrawSectionHeader("Brush");
            DrawFieldLabel("Shape");
            var brushShape = (int)_brushShape;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##shape", ref brushShape, "Circle\0Square\0Noise\0"))
            {
                _brushShape = (TerrainBrushShape)brushShape;
            }

            DrawFieldLabel("Radius");
            ImGui.SetNextItemWidth(-1);
            ImGui.SliderFloat("##radius", ref _brushRadius, 0.1f, 50.0f, "%.1f m");
            DrawHoverTooltip("Shortcut: 1 / 2");

            if (_brushShape == TerrainBrushShape.Noise)
            {
                DrawFieldLabel("Noise Scale");
                ImGui.SetNextItemWidth(-1);
                ImGui.SliderFloat("##noisescale", ref _noiseScale, 0.01f, 1.0f, "%.2f");
                DrawFieldLabel("Noise Amount");
                ImGui.SetNextItemWidth(-1);
                ImGui.SliderFloat("##noiseamount", ref _noiseAmount, 0.0f, 1.0f, "%.2f");
            }

            if (_terrainTool == TerrainTool.Paint)
            {
                DrawFieldLabel("Color");
                ImGui.SetNextItemWidth(-1);
                ImGui.ColorEdit3("##paintcolor", ref _paintColor);
                DrawFieldLabel("Paint Strength");
                ImGui.SetNextItemWidth(-1);
                ImGui.SliderFloat("##paintstrength", ref _paintStrength, 0.1f, 10.0f, "%.1f /s");
                DrawHoverTooltip("Shortcut: 3 / 4");
            }
            else
            {
                DrawFieldLabel("Strength");
                ImGui.SetNextItemWidth(-1);
                ImGui.SliderFloat("##strength", ref _brushStrength, 0.1f, 20.0f, "%.1f m/s");
                DrawHoverTooltip("Shortcut: 3 / 4");
            }

            DrawFieldLabel("Falloff");
            var falloff = _brushFalloff == BrushFalloff.Smooth ? 1 : 0;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##falloff", ref falloff, "Linear\0Smooth\0"))
            {
                _brushFalloff = falloff == 1 ? BrushFalloff.Smooth : BrushFalloff.Linear;
            }
        }

        DrawSectionHeader("Display");
        DrawFieldLabel("View Mode");
        var viewMode = (int)_viewMode;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##viewmode", ref viewMode, "Wireframe\0Albedo\0Height\0"))
        {
            _viewMode = (TerrainViewMode)viewMode;
            _lastAction = $"View mode: {GetViewModeName(_viewMode)}";
        }

        DrawSectionHeader("Camera");
        DrawFieldLabel("Speed");
        var cameraSpeedRatio = (_cameraSpeed - MinCameraSpeed) / (MaxCameraSpeed - MinCameraSpeed);
        ImGui.ProgressBar(cameraSpeedRatio, new Vector2(-1, 16), $"{_cameraSpeed:0} m/s");
        DrawHoverTooltip("Mouse wheel adjusts speed, Shift boosts");

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawStatusBar()
    {
        var size = _window.Size;
        ImGui.SetNextWindowPos(new Vector2(0.0f, size.Y - StatusBarHeight), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(size.X, StatusBarHeight), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14.0f, (StatusBarHeight - ImGui.GetTextLineHeight()) * 0.5f));
        ImGui.Begin("##statusbar", HudWindowFlags);

        ImGui.Text($"X {_cursor.X:0.0}  Y {_cursor.Y:0.0}  Z {_cursor.Z:0.0}");
        DrawStatusDivider();
        ImGui.Text($"Tile {_cursorTileCoord}");
        DrawStatusDivider();
        ImGui.Text(_tileMode
            ? "Tile Mode"
            : $"{GetToolName(_terrainTool)}  {_brushRadius:0.0} m");
        DrawStatusDivider();
        ImGui.Text($"Cam {_cameraSpeed:0} m/s");
        DrawStatusDivider();
        ImGui.Text($"Undo {_undoStack.Count}  Redo {_redoStack.Count}");

        var actionWidth = ImGui.CalcTextSize(_lastAction).X;
        ImGui.SameLine(Math.Max(0.0f, size.X - actionWidth - 14.0f));
        ImGui.TextDisabled(_lastAction);

        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private static void DrawStatusDivider()
    {
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
    }

    private static void DrawSectionHeader(string title, bool first = false)
    {
        if (!first)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        ImGui.TextDisabled(title.ToUpperInvariant());
        ImGui.Spacing();
    }

    private static void DrawFieldLabel(string label)
    {
        ImGui.TextDisabled(label);
    }

    private static void DrawHoverTooltip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
        {
            ImGui.SetTooltip(text);
        }
    }

    private static void PushAccentButtonColors()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, AccentColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AccentHoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AccentActiveColor);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
    }

    private static bool DrawAccentButton(string label, Vector2 size)
    {
        PushAccentButtonColors();
        var pressed = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return pressed;
    }

    private int ApplyActiveTerrainTool(float deltaSeconds)
    {
        return _terrainTool switch
        {
            TerrainTool.Smooth => ApplySmoothTool(deltaSeconds),
            TerrainTool.Flatten => ApplyFlattenTool(deltaSeconds),
            TerrainTool.Paint => ApplyPaintTool(deltaSeconds),
            _ => ApplyRaiseLowerTool(deltaSeconds)
        };
    }

    private int ApplyRaiseLowerTool(float deltaSeconds)
    {
        var lowered = _keys.Contains(Key.ControlLeft) || _keys.Contains(Key.ControlRight);
        var changed = TerrainBrush.ApplyRaiseLower(_world, _cursor.X, _cursor.Z, CreateBrushProfile(), _brushStrength, deltaSeconds, lowered);
        if (changed > 0)
        {
            _lastAction = lowered ? $"Lowered {changed} samples" : $"Raised {changed} samples";
        }

        return changed;
    }

    private int ApplySmoothTool(float deltaSeconds)
    {
        var changed = TerrainBrush.ApplySmooth(_world, _cursor.X, _cursor.Z, CreateBrushProfile(), _brushStrength, deltaSeconds);
        if (changed > 0)
        {
            _lastAction = $"Smoothed {changed} samples";
        }

        return changed;
    }

    private int ApplyFlattenTool(float deltaSeconds)
    {
        if (_leftMousePressed || !_hasFlattenHeight)
        {
            _flattenHeight = _cursor.Y;
            _hasFlattenHeight = true;
        }

        var changed = TerrainBrush.ApplyFlatten(_world, _cursor.X, _cursor.Z, CreateBrushProfile(), _flattenHeight, _brushStrength, deltaSeconds);
        if (changed > 0)
        {
            _lastAction = $"Flattened {changed} samples to {_flattenHeight:0.00} m";
        }

        return changed;
    }

    private int ApplyPaintTool(float deltaSeconds)
    {
        var changed = TerrainBrush.ApplyPaint(_world, _cursor.X, _cursor.Z, CreateBrushProfile(), _paintColor, _paintStrength, deltaSeconds);
        if (changed > 0)
        {
            _lastAction = $"Painted {changed} samples";
        }

        return changed;
    }

    private TerrainBrushProfile CreateBrushProfile()
    {
        return new TerrainBrushProfile(_brushShape, _brushRadius, _brushFalloff, _noiseScale, _noiseAmount);
    }

    private string GetToolHelpText() => _terrainTool switch
    {
        TerrainTool.Smooth => "Left-drag smooths terrain. Larger radius blends broader shapes.",
        TerrainTool.Flatten => "Left-drag flattens to the height sampled at stroke start.",
        TerrainTool.Paint => "Left-drag paints terrain color with the selected brush color.",
        _ => "Left-drag raises. Hold Ctrl to lower."
    };

    private void UpdateViewport()
    {
        if (_gl is null) return;

        var size = _window.FramebufferSize;
        _gl.Viewport(0, 0, (uint)Math.Max(1, size.X), (uint)Math.Max(1, size.Y));
    }

    private void UpdateCursor()
    {
        var size = _window.Size;
        if (size.X <= 0 || size.Y <= 0) return;

        var aspect = size.X / (float)size.Y;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, aspect, 0.1f, 1000.0f);
        var view = _camera.View;
        Matrix4x4.Invert(view * projection, out var inverse);

        var ndcX = (2.0f * _mouse.X / size.X) - 1.0f;
        var ndcY = 1.0f - (2.0f * _mouse.Y / size.Y);
        var near = Vector4.Transform(new Vector4(ndcX, ndcY, -1.0f, 1.0f), inverse);
        var far = Vector4.Transform(new Vector4(ndcX, ndcY, 1.0f, 1.0f), inverse);
        near /= near.W;
        far /= far.W;
        var origin = new Vector3(near.X, near.Y, near.Z);
        var direction = Vector3.Normalize(new Vector3(far.X, far.Y, far.Z) - origin);
        if (MathF.Abs(direction.Y) < 0.0001f) return;

        var t = -origin.Y / direction.Y;
        if (t < 0) return;
        var hit = origin + direction * t;
        var template = _world.Tiles.Values.First();
        var coord = GetTileCoord(hit.X, hit.Z, template);
        var tileOriginX = coord.X * template.WidthMetres;
        var tileOriginZ = coord.Z * template.DepthMetres;
        var localX = Math.Clamp(hit.X - tileOriginX, 0.0f, template.WidthMetres);
        var localZ = Math.Clamp(hit.Z - tileOriginZ, 0.0f, template.DepthMetres);

        _cursorTileCoord = coord;
        _cursorLocal = new Vector2(localX, localZ);
        _cursorHasTile = _world.TryGetTile(coord, out var tile);
        _cursorCanAddTile = _world.CanAddTile(coord);

        var y = 0.0f;
        if (tile is not null)
        {
            var sx = Math.Clamp((int)MathF.Round(localX / tile.ResolutionMetres), 0, tile.HeightmapWidth - 1);
            var sz = Math.Clamp((int)MathF.Round(localZ / tile.ResolutionMetres), 0, tile.HeightmapHeight - 1);
            y = tile.GetHeight(sx, sz);
        }

        _cursor = new Vector3(hit.X, y, hit.Z);
    }

    private void RebuildTerrainMesh()
    {
        if (_gl is null) return;

        var template = _world.Tiles.Values.First();
        var previewWidth = ((template.HeightmapWidth - 1) / PreviewStride) + 1;
        var previewHeight = ((template.HeightmapHeight - 1) / PreviewStride) + 1;
        var vertices = new List<float>(_world.TileCount * previewWidth * previewHeight * 10);
        var indices = new List<uint>(_world.TileCount * (previewWidth - 1) * (previewHeight - 1) * 6);

        foreach (var (coord, tile) in _world.Tiles.OrderBy(entry => entry.Key.X).ThenBy(entry => entry.Key.Z))
        {
            var tileVertexStart = (uint)(vertices.Count / 10);
            var offsetX = coord.X * tile.WidthMetres;
            var offsetZ = coord.Z * tile.DepthMetres;

            for (var z = 0; z < tile.HeightmapHeight; z += PreviewStride)
            {
                for (var x = 0; x < tile.HeightmapWidth; x += PreviewStride)
                {
                    var normal = EstimatePreviewNormal(coord, tile, x, z);
                    vertices.Add(offsetX + x * tile.ResolutionMetres);
                    vertices.Add(tile.GetHeight(x, z));
                    vertices.Add(offsetZ + z * tile.ResolutionMetres);
                    vertices.Add(normal.X);
                    vertices.Add(normal.Y);
                    vertices.Add(normal.Z);
                    AddAlbedo(vertices, tile, x, z);
                }
            }

            for (var z = 0; z < previewHeight - 1; z++)
            {
                for (var x = 0; x < previewWidth - 1; x++)
                {
                    var a = tileVertexStart + (uint)(z * previewWidth + x);
                    var b = tileVertexStart + (uint)(z * previewWidth + x + 1);
                    var c = tileVertexStart + (uint)((z + 1) * previewWidth + x);
                    var d = tileVertexStart + (uint)((z + 1) * previewWidth + x + 1);
                    indices.Add(a);
                    indices.Add(c);
                    indices.Add(b);
                    indices.Add(b);
                    indices.Add(c);
                    indices.Add(d);
                }
            }
        }

        _terrainMesh?.Dispose();
        _terrainMesh = MeshBuffer.Create(_gl, CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
        _meshDirty = false;
        _tileOverlayDirty = true;
    }

    private void RebuildBrushMesh()
    {
        if (_gl is null) return;

        if (_brushShape == TerrainBrushShape.Square)
        {
            RebuildSquareBrushMesh();
            return;
        }

        const int segments = 96;
        var vertices = new List<float>(segments * 10);
        var indices = new List<uint>(segments * 2);
        for (var i = 0; i < segments; i++)
        {
            var angle = i / (float)segments * MathF.Tau;
            vertices.Add(_cursor.X + MathF.Cos(angle) * _brushRadius);
            vertices.Add(_cursor.Y + 0.08f);
            vertices.Add(_cursor.Z + MathF.Sin(angle) * _brushRadius);
            vertices.Add(0.0f);
            vertices.Add(1.0f);
            vertices.Add(0.0f);
            vertices.Add(1.0f);
            vertices.Add(0.85f);
            vertices.Add(0.25f);
            vertices.Add(1.0f);
            indices.Add((uint)i);
            indices.Add((uint)((i + 1) % segments));
        }

        _brushMesh?.Dispose();
        _brushMesh = MeshBuffer.Create(_gl, CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
    }

    private void RebuildSquareBrushMesh()
    {
        if (_gl is null) return;

        var vertices = new List<float>(4 * 10);
        var indices = new List<uint>(8);
        var y = _cursor.Y + 0.08f;
        var color = new Vector4(1.0f, 0.85f, 0.25f, 1.0f);
        AddLineVertex(vertices, _cursor.X - _brushRadius, y, _cursor.Z - _brushRadius, color);
        AddLineVertex(vertices, _cursor.X + _brushRadius, y, _cursor.Z - _brushRadius, color);
        AddLineVertex(vertices, _cursor.X + _brushRadius, y, _cursor.Z + _brushRadius, color);
        AddLineVertex(vertices, _cursor.X - _brushRadius, y, _cursor.Z + _brushRadius, color);
        indices.Add(0);
        indices.Add(1);
        indices.Add(1);
        indices.Add(2);
        indices.Add(2);
        indices.Add(3);
        indices.Add(3);
        indices.Add(0);

        _brushMesh?.Dispose();
        _brushMesh = MeshBuffer.Create(_gl, CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
    }

    private void RebuildTileOverlayMesh()
    {
        if (_gl is null) return;

        var template = _world.Tiles.Values.First();
        var addable = _world.GetAddableTileCoords();
        var lineCoords = _world.Coordinates.Concat(addable).Distinct().ToList();
        var vertices = new List<float>(lineCoords.Count * 4 * 10);
        var indices = new List<uint>(lineCoords.Count * 8);

        foreach (var coord in lineCoords)
        {
            var isAddable = addable.Contains(coord) && !_world.ContainsTile(coord);
            var color = isAddable ? new Vector4(0.24f, 0.86f, 1.0f, 1.0f) : new Vector4(1.0f, 0.95f, 0.34f, 1.0f);
            AddTileRectangle(vertices, indices, coord, template.WidthMetres, template.DepthMetres, isAddable ? 0.18f : 0.12f, color);
        }

        _tileOverlayMesh?.Dispose();
        _tileOverlayMesh = MeshBuffer.Create(_gl, CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
        _tileOverlayDirty = false;
    }

    private static void AddTileRectangle(List<float> vertices, List<uint> indices, TerrainCoord coord, float width, float depth, float y, Vector4 color)
    {
        var start = (uint)(vertices.Count / 10);
        var minX = coord.X * width;
        var minZ = coord.Z * depth;
        var maxX = minX + width;
        var maxZ = minZ + depth;

        AddLineVertex(vertices, minX, y, minZ, color);
        AddLineVertex(vertices, maxX, y, minZ, color);
        AddLineVertex(vertices, maxX, y, maxZ, color);
        AddLineVertex(vertices, minX, y, maxZ, color);

        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start + 2);
        indices.Add(start + 3);
        indices.Add(start + 3);
        indices.Add(start);
    }

    private static void AddLineVertex(List<float> vertices, float x, float y, float z, Vector4 color)
    {
        vertices.Add(x);
        vertices.Add(y);
        vertices.Add(z);
        vertices.Add(0.0f);
        vertices.Add(1.0f);
        vertices.Add(0.0f);
        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
        vertices.Add(color.W);
    }

    private void HandleTileModeClick()
    {
        var ctrl = _keys.Contains(Key.ControlLeft) || _keys.Contains(Key.ControlRight);
        if (ctrl)
        {
            if (!_cursorHasTile)
            {
                _lastAction = $"No tile at {_cursorTileCoord}";
                return;
            }

            var before = _world.Clone();
            if (_world.RemoveTile(_cursorTileCoord))
            {
                PushUndoState(before);
                _redoStack.Clear();
                _meshDirty = true;
                _tileOverlayDirty = true;
                _lastAction = $"Removed tile {_cursorTileCoord}";
            }
            else
            {
                _lastAction = "Cannot remove tile: terrain must stay connected";
            }

            return;
        }

        if (_cursorCanAddTile)
        {
            var before = _world.Clone();
            if (_world.AddTile(_cursorTileCoord))
            {
                PushUndoState(before);
                _redoStack.Clear();
                _meshDirty = true;
                _tileOverlayDirty = true;
                _lastAction = $"Added tile {_cursorTileCoord}";
            }
        }
        else if (_cursorHasTile)
        {
            _lastAction = $"Tile {_cursorTileCoord} already exists";
        }
        else
        {
            _lastAction = "Click an exposed edge tile to add terrain";
        }
    }

    private void BeginTerrainStroke()
    {
        _strokeBeforeWorld ??= _world.Clone();
        _strokeChanged = false;
    }

    private void EndTerrainStroke()
    {
        if (_strokeBeforeWorld is not null && _strokeChanged)
        {
            PushUndoState(_strokeBeforeWorld);
            _redoStack.Clear();
        }

        _strokeBeforeWorld = null;
        _strokeChanged = false;
    }

    private void PushUndoState(TerrainWorld state)
    {
        _undoStack.Add(state.Clone());
        if (_undoStack.Count > MaxHistoryStates)
        {
            _undoStack.RemoveAt(0);
        }
    }

    private void Undo()
    {
        EndTerrainStroke();
        if (_undoStack.Count == 0)
        {
            _lastAction = "Nothing to undo";
            return;
        }

        var previous = PopHistoryState(_undoStack);
        _redoStack.Add(_world.Clone());
        _world = previous.Clone();
        MarkWorldChanged();
        _lastAction = "Undo";
    }

    private void Redo()
    {
        EndTerrainStroke();
        if (_redoStack.Count == 0)
        {
            _lastAction = "Nothing to redo";
            return;
        }

        var next = PopHistoryState(_redoStack);
        PushUndoState(_world);
        _world = next.Clone();
        MarkWorldChanged();
        _lastAction = "Redo";
    }

    private static TerrainWorld PopHistoryState(List<TerrainWorld> history)
    {
        var index = history.Count - 1;
        var state = history[index];
        history.RemoveAt(index);
        return state;
    }

    private void MarkWorldChanged()
    {
        _meshDirty = true;
        _tileOverlayDirty = true;
        _hasFlattenHeight = false;
        UpdateCursor();
    }

    private void RebuildCharacterPreviewMesh()
    {
        if (_gl is null) return;

        if (!_keys.Contains(Key.T))
        {
            _characterPreviewMesh?.Dispose();
            _characterPreviewMesh = null;
            return;
        }

        const float radius = 0.25f;
        const float height = 1.8f;
        const int segments = 32;
        var cylinderHeight = height - radius * 2.0f;
        var bottomCentreY = _cursor.Y + radius;
        var topCentreY = bottomCentreY + cylinderHeight;
        var vertices = new List<float>((segments * 6 + 36) * 10);
        var indices = new List<uint>((segments * 10 + 32) * 2);

        for (var i = 0; i < segments; i++)
        {
            var angle = i / (float)segments * MathF.Tau;
            var x = _cursor.X + MathF.Cos(angle) * radius;
            var z = _cursor.Z + MathF.Sin(angle) * radius;
            AddPreviewVertex(vertices, x, bottomCentreY, z);
            AddPreviewVertex(vertices, x, topCentreY, z);
        }

        for (var i = 0; i < segments; i++)
        {
            var bottom = (uint)(i * 2);
            var top = bottom + 1;
            var nextBottom = (uint)(((i + 1) % segments) * 2);
            var nextTop = nextBottom + 1;
            indices.Add(bottom);
            indices.Add(nextBottom);
            indices.Add(top);
            indices.Add(nextTop);
            if (i % 8 == 0)
            {
                indices.Add(bottom);
                indices.Add(top);
            }
        }

        AddCapsuleArc(vertices, indices, topCentreY, radius, 0.0f);
        AddCapsuleArc(vertices, indices, topCentreY, radius, MathF.PI * 0.5f);
        AddCapsuleArc(vertices, indices, bottomCentreY, -radius, 0.0f);
        AddCapsuleArc(vertices, indices, bottomCentreY, -radius, MathF.PI * 0.5f);

        _characterPreviewMesh?.Dispose();
        _characterPreviewMesh = MeshBuffer.Create(_gl, CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
    }

    private void AddCapsuleArc(List<float> vertices, List<uint> indices, float centreY, float verticalRadius, float rotation)
    {
        const float radius = 0.25f;
        const int arcSteps = 8;
        var capSign = MathF.Sign(verticalRadius);
        var start = (uint)(vertices.Count / 10);
        for (var i = 0; i <= arcSteps; i++)
        {
            var t = i / (float)arcSteps * MathF.PI;
            var horizontal = MathF.Cos(t) * radius;
            var y = centreY + MathF.Sin(t) * radius * capSign;
            var x = _cursor.X + MathF.Cos(rotation) * horizontal;
            var z = _cursor.Z + MathF.Sin(rotation) * horizontal;
            AddPreviewVertex(vertices, x, y, z);
            if (i > 0)
            {
                indices.Add(start + (uint)i - 1);
                indices.Add(start + (uint)i);
            }
        }
    }

    private static void AddPreviewVertex(List<float> vertices, float x, float y, float z)
    {
        vertices.Add(x);
        vertices.Add(y);
        vertices.Add(z);
        vertices.Add(0.0f);
        vertices.Add(1.0f);
        vertices.Add(0.0f);
        vertices.Add(0.45f);
        vertices.Add(0.82f);
        vertices.Add(1.0f);
        vertices.Add(1.0f);
    }

    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec4 aAlbedo;
        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;
        out vec3 vNormal;
        out vec4 vAlbedo;
        void main()
        {
            vNormal = normalize(aNormal);
            vAlbedo = aAlbedo;
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec3 vNormal;
        in vec4 vAlbedo;
        out vec4 FragColor;
        uniform vec4 uColor;
        uniform vec4 uLightDirection;
        uniform int uUseLighting;
        uniform int uViewMode;
        void main()
        {
            if (uUseLighting == 0)
            {
                FragColor = uColor;
                return;
            }

            vec3 normal = normalize(vNormal);
            vec3 light = normalize(uLightDirection.xyz);
            float diffuse = max(dot(normal, light), 0.0);
            float slope = 1.0 - clamp(normal.y, 0.0, 1.0);
            vec3 baseColor = vAlbedo.rgb;
            if (uViewMode == 0)
            {
                baseColor = uColor.rgb;
            }
            else if (uViewMode == 2)
            {
                vec3 shallow = vec3(0.18, 0.58, 0.38);
                vec3 rolling = vec3(0.86, 0.72, 0.28);
                vec3 steep = vec3(0.74, 0.24, 0.18);
                baseColor = mix(shallow, rolling, smoothstep(0.05, 0.36, slope));
                baseColor = mix(baseColor, steep, smoothstep(0.36, 0.76, slope));
            }

            vec3 lit = baseColor * (0.38 + diffuse * 0.66);
            lit += slope * vec3(0.04, 0.035, 0.03);
            FragColor = vec4(lit, uColor.a);
        }
        """;

    private static string GetViewModeName(TerrainViewMode viewMode) => viewMode switch
    {
        TerrainViewMode.Wireframe => "Wireframe",
        TerrainViewMode.Albedo => "Albedo",
        TerrainViewMode.Height => "Height",
        _ => "Unknown"
    };

    private static string GetToolName(TerrainTool tool) => tool switch
    {
        TerrainTool.RaiseLower => "Raise / Lower",
        TerrainTool.Smooth => "Smooth",
        TerrainTool.Flatten => "Flatten",
        TerrainTool.Paint => "Paint",
        _ => "Unknown"
    };

    private void AddAlbedo(List<float> vertices, TerrainTile tile, int x, int z)
    {
        var index = tile.GetIndex(x, z) * 4;
        vertices.Add(tile.Albedo[index] / 255.0f);
        vertices.Add(tile.Albedo[index + 1] / 255.0f);
        vertices.Add(tile.Albedo[index + 2] / 255.0f);
        vertices.Add(tile.Albedo[index + 3] / 255.0f);
    }

    private Vector3 EstimatePreviewNormal(TerrainCoord coord, TerrainTile tile, int x, int z)
    {
        var worldX = coord.X * tile.WidthMetres + x * tile.ResolutionMetres;
        var worldZ = coord.Z * tile.DepthMetres + z * tile.ResolutionMetres;
        var offset = PreviewStride * tile.ResolutionMetres;
        var left = SampleWorldHeight(worldX - offset, worldZ, tile.GetHeight(Math.Max(0, x - PreviewStride), z));
        var right = SampleWorldHeight(worldX + offset, worldZ, tile.GetHeight(Math.Min(tile.HeightmapWidth - 1, x + PreviewStride), z));
        var down = SampleWorldHeight(worldX, worldZ - offset, tile.GetHeight(x, Math.Max(0, z - PreviewStride)));
        var up = SampleWorldHeight(worldX, worldZ + offset, tile.GetHeight(x, Math.Min(tile.HeightmapHeight - 1, z + PreviewStride)));
        return Vector3.Normalize(new Vector3(left - right, 2.0f * tile.ResolutionMetres * PreviewStride, down - up));
    }

    private float SampleWorldHeight(float worldX, float worldZ, float fallback)
    {
        var template = _world.Tiles.Values.First();
        var coord = GetTileCoord(worldX, worldZ, template);
        if (!_world.TryGetTile(coord, out var tile) || tile is null) return fallback;

        var localX = Math.Clamp(worldX - coord.X * tile.WidthMetres, 0.0f, tile.WidthMetres);
        var localZ = Math.Clamp(worldZ - coord.Z * tile.DepthMetres, 0.0f, tile.DepthMetres);
        var sampleX = Math.Clamp((int)MathF.Round(localX / tile.ResolutionMetres), 0, tile.HeightmapWidth - 1);
        var sampleZ = Math.Clamp((int)MathF.Round(localZ / tile.ResolutionMetres), 0, tile.HeightmapHeight - 1);
        return tile.GetHeight(sampleX, sampleZ);
    }

    private static TerrainCoord GetTileCoord(float worldX, float worldZ, TerrainTile template)
    {
        return new TerrainCoord(
            (int)MathF.Floor(worldX / template.WidthMetres),
            (int)MathF.Floor(worldZ / template.DepthMetres));
    }

    private enum TerrainViewMode
    {
        Wireframe,
        Albedo,
        Height
    }

    private enum TerrainTool
    {
        RaiseLower,
        Smooth,
        Flatten,
        Paint
    }
}
