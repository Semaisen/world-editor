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
    private readonly IWindow _window;
    private readonly Camera _camera = new();
    private readonly HashSet<Key> _keys = [];
    private TerrainTile _tile = TerrainTile.CreateDefault();
    private GL? _gl;
    private IInputContext? _input;
    private ShaderProgram? _shader;
    private ImGuiController? _imgui;
    private MeshBuffer? _terrainMesh;
    private MeshBuffer? _brushMesh;
    private Vector2 _lastMouse;
    private Vector2 _mouse;
    private bool _leftMouseDown;
    private bool _rightMouseDown;
    private bool _middleMouseDown;
    private bool _meshDirty = true;
    private float _brushRadius = 2.0f;
    private float _brushStrength = 1.5f;
    private BrushFalloff _brushFalloff = BrushFalloff.Smooth;
    private Vector3 _cursor = new(64, 0, 64);
    private string _lastAction = "Ready";
    private float _mouseWheel;
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
        _gl.ClearColor(0.09f, 0.11f, 0.13f, 1.0f);
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
        DrawToolbar();

        var uiHasMouse = _imgui?.WantsMouse ?? false;
        var moveSpeed = 35.0f * deltaSeconds;
        if (_keys.Contains(Key.W)) _camera.Position += _camera.Forward * moveSpeed;
        if (_keys.Contains(Key.S)) _camera.Position -= _camera.Forward * moveSpeed;
        if (_keys.Contains(Key.A)) _camera.Position -= _camera.Right * moveSpeed;
        if (_keys.Contains(Key.D)) _camera.Position += _camera.Right * moveSpeed;
        if (_keys.Contains(Key.Q)) _camera.Position -= Vector3.UnitY * moveSpeed;
        if (_keys.Contains(Key.E)) _camera.Position += Vector3.UnitY * moveSpeed;
        if (!uiHasMouse && _mouseWheel != 0) _camera.Position += _camera.Forward * _mouseWheel * 4.0f;
        if (!uiHasMouse && _keys.Contains(Key.Number1)) _brushRadius = Math.Max(0.1f, _brushRadius - 8.0f * deltaSeconds);
        if (!uiHasMouse && _keys.Contains(Key.Number2)) _brushRadius = Math.Min(50.0f, _brushRadius + 8.0f * deltaSeconds);
        if (!uiHasMouse && _keys.Contains(Key.Number3)) _brushStrength = Math.Max(0.1f, _brushStrength - 4.0f * deltaSeconds);
        if (!uiHasMouse && _keys.Contains(Key.Number4)) _brushStrength = Math.Min(20.0f, _brushStrength + 4.0f * deltaSeconds);

        var mouseDelta = _mouse - _lastMouse;
        if (!uiHasMouse && _rightMouseDown) _camera.Rotate(mouseDelta.X, mouseDelta.Y);
        if (!uiHasMouse && _middleMouseDown) _camera.Pan(mouseDelta.X, mouseDelta.Y);

        if (!uiHasMouse && _leftMouseDown)
        {
            var lowered = _keys.Contains(Key.ControlLeft) || _keys.Contains(Key.ControlRight);
            var changed = TerrainBrush.ApplyRaiseLower(_tile, _cursor.X, _cursor.Z, _brushRadius, _brushStrength, deltaSeconds, lowered, _brushFalloff);
            if (changed > 0)
            {
                _meshDirty = true;
                _lastAction = lowered ? $"Lowered {changed} samples" : $"Raised {changed} samples";
            }
        }

        if (_meshDirty) RebuildTerrainMesh();
        RebuildBrushMesh();
        HandleShortcuts();
        UpdateTitle();
        _lastMouse = _mouse;
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

        _shader.SetVector("uColor", new Vector4(0.34f, 0.50f, 0.29f, 1.0f));
        _shader.SetVector("uLightDirection", new Vector4(-0.45f, 0.85f, -0.28f, 0.0f));
        _shader.SetInt("uUseLighting", 1);
        _terrainMesh.DrawTriangles();
        _shader.SetVector("uColor", new Vector4(1.0f, 0.85f, 0.25f, 1.0f));
        _shader.SetInt("uUseLighting", 0);
        _brushMesh.DrawLines();
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
                _leftMouseDown = pressed;
                break;
        }
    }

    private void HandleShortcuts()
    {
        var ctrl = _keys.Contains(Key.ControlLeft) || _keys.Contains(Key.ControlRight);
        if (!ctrl) return;

        if (_keys.Remove(Key.S))
        {
            TerrainProjectStore.Save(_tile, Path.Combine(Environment.CurrentDirectory, "SampleTerrainProject"));
            _lastAction = "Saved SampleTerrainProject";
        }
        else if (_keys.Remove(Key.O))
        {
            var path = Path.Combine(Environment.CurrentDirectory, "SampleTerrainProject");
            if (Directory.Exists(path))
            {
                _tile = TerrainProjectStore.Load(path);
                _meshDirty = true;
                _lastAction = "Loaded SampleTerrainProject";
            }
        }
        else if (_keys.Remove(Key.P))
        {
            GodotExporter.Export(_tile, Path.Combine(Environment.CurrentDirectory, "GodotExport"));
            _lastAction = "Exported GodotExport";
        }
    }

    private void DrawToolbar()
    {
        var height = Math.Max(1, _window.Size.Y);
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(280, height), ImGuiCond.Always);
        ImGui.Begin(
            "Tools",
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoSavedSettings);

        ImGui.Text("World Editor");
        ImGui.TextDisabled("Heightmap terrain");
        ImGui.Separator();

        ImGui.Text("Tool");
        ImGui.BeginDisabled();
        ImGui.Button("Raise / Lower", new Vector2(-1, 34));
        ImGui.EndDisabled();
        ImGui.TextWrapped("Left-drag raises. Hold Ctrl to lower.");
        ImGui.Separator();

        ImGui.Text("Brush");
        ImGui.SliderFloat("Radius (m)", ref _brushRadius, 0.1f, 50.0f, "%.1f");
        ImGui.SliderFloat("Strength", ref _brushStrength, 0.1f, 20.0f, "%.1f m/s");
        var falloff = _brushFalloff == BrushFalloff.Smooth ? 1 : 0;
        if (ImGui.Combo("Falloff", ref falloff, "Linear\0Smooth\0"))
        {
            _brushFalloff = falloff == 1 ? BrushFalloff.Smooth : BrushFalloff.Linear;
        }

        ImGui.Separator();
        ImGui.Text("Project");
        if (ImGui.Button("New Flat Terrain", new Vector2(-1, 32)))
        {
            _tile = TerrainTile.CreateDefault();
            _meshDirty = true;
            _lastAction = "Created new flat terrain";
        }

        if (ImGui.Button("Save", new Vector2(-1, 32)))
        {
            TerrainProjectStore.Save(_tile, Path.Combine(Environment.CurrentDirectory, "SampleTerrainProject"));
            _lastAction = "Saved SampleTerrainProject";
        }

        if (ImGui.Button("Load", new Vector2(-1, 32)))
        {
            var path = Path.Combine(Environment.CurrentDirectory, "SampleTerrainProject");
            if (Directory.Exists(path))
            {
                _tile = TerrainProjectStore.Load(path);
                _meshDirty = true;
                _lastAction = "Loaded SampleTerrainProject";
            }
            else
            {
                _lastAction = "No SampleTerrainProject folder";
            }
        }

        if (ImGui.Button("Export Godot", new Vector2(-1, 32)))
        {
            GodotExporter.Export(_tile, Path.Combine(Environment.CurrentDirectory, "GodotExport"));
            _lastAction = "Exported GodotExport";
        }

        ImGui.Separator();
        ImGui.Text("Cursor");
        ImGui.Text($"X {_cursor.X:0.0} m");
        ImGui.Text($"Y {_cursor.Y:0.0} m");
        ImGui.Text($"Z {_cursor.Z:0.0} m");
        ImGui.Separator();
        ImGui.Text("Display");
        ImGui.TextWrapped("Directional lighting and height tint are enabled for surface detail.");
        ImGui.Separator();
        ImGui.TextWrapped(_lastAction);
        ImGui.End();
    }

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
        var x = Math.Clamp(hit.X, 0.0f, _tile.WidthMetres);
        var z = Math.Clamp(hit.Z, 0.0f, _tile.DepthMetres);
        var sx = Math.Clamp((int)MathF.Round(x / _tile.ResolutionMetres), 0, _tile.HeightmapWidth - 1);
        var sz = Math.Clamp((int)MathF.Round(z / _tile.ResolutionMetres), 0, _tile.HeightmapHeight - 1);
        _cursor = new Vector3(x, _tile.GetHeight(sx, sz), z);
    }

    private void RebuildTerrainMesh()
    {
        if (_gl is null) return;

        var width = ((_tile.HeightmapWidth - 1) / PreviewStride) + 1;
        var height = ((_tile.HeightmapHeight - 1) / PreviewStride) + 1;
        var vertices = new List<float>(width * height * 6);
        var indices = new List<uint>((width - 1) * (height - 1) * 6);

        for (var z = 0; z < _tile.HeightmapHeight; z += PreviewStride)
        {
            for (var x = 0; x < _tile.HeightmapWidth; x += PreviewStride)
            {
                var normal = EstimatePreviewNormal(x, z);
                vertices.Add(x * _tile.ResolutionMetres);
                vertices.Add(_tile.GetHeight(x, z));
                vertices.Add(z * _tile.ResolutionMetres);
                vertices.Add(normal.X);
                vertices.Add(normal.Y);
                vertices.Add(normal.Z);
            }
        }

        for (var z = 0; z < height - 1; z++)
        {
            for (var x = 0; x < width - 1; x++)
            {
                var a = (uint)(z * width + x);
                var b = (uint)(z * width + x + 1);
                var c = (uint)((z + 1) * width + x);
                var d = (uint)((z + 1) * width + x + 1);
                indices.Add(a);
                indices.Add(c);
                indices.Add(b);
                indices.Add(b);
                indices.Add(c);
                indices.Add(d);
            }
        }

        _terrainMesh?.Dispose();
        _terrainMesh = MeshBuffer.Create(_gl, CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
        _meshDirty = false;
    }

    private void RebuildBrushMesh()
    {
        if (_gl is null) return;

        const int segments = 96;
        var vertices = new List<float>(segments * 6);
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
            indices.Add((uint)i);
            indices.Add((uint)((i + 1) % segments));
        }

        _brushMesh?.Dispose();
        _brushMesh = MeshBuffer.Create(_gl, CollectionsMarshal.AsSpan(vertices), CollectionsMarshal.AsSpan(indices));
    }

    private void UpdateTitle()
    {
        _window.Title = $"World Editor | X {_cursor.X:0.0}m Y {_cursor.Y:0.0}m Z {_cursor.Z:0.0}m | Radius {_brushRadius:0.0}m Strength {_brushStrength:0.0}m/s | Ctrl+S save Ctrl+O load Ctrl+P export | {_lastAction}";
    }

    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;
        out vec3 vNormal;
        out float vHeight;
        void main()
        {
            vNormal = normalize(aNormal);
            vHeight = aPosition.y;
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec3 vNormal;
        in float vHeight;
        out vec4 FragColor;
        uniform vec4 uColor;
        uniform vec4 uLightDirection;
        uniform int uUseLighting;
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
            float heightBand = clamp(vHeight / 12.0, -1.0, 1.0);
            vec3 heightTint = mix(vec3(0.78, 0.88, 0.68), vec3(0.42, 0.55, 0.35), heightBand * 0.5 + 0.5);
            vec3 baseColor = mix(uColor.rgb, heightTint, 0.22);
            vec3 lit = baseColor * (0.34 + diffuse * 0.72);
            lit += slope * vec3(0.08, 0.07, 0.05);
            FragColor = vec4(lit, uColor.a);
        }
        """;

    private Vector3 EstimatePreviewNormal(int x, int z)
    {
        var left = _tile.GetHeight(Math.Max(0, x - PreviewStride), z);
        var right = _tile.GetHeight(Math.Min(_tile.HeightmapWidth - 1, x + PreviewStride), z);
        var down = _tile.GetHeight(x, Math.Max(0, z - PreviewStride));
        var up = _tile.GetHeight(x, Math.Min(_tile.HeightmapHeight - 1, z + PreviewStride));
        return Vector3.Normalize(new Vector3(left - right, 2.0f * _tile.ResolutionMetres * PreviewStride, down - up));
    }
}
