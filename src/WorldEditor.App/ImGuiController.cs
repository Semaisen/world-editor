using System.Numerics;
using ImGuiNET;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace WorldEditor.App;

internal sealed unsafe class ImGuiController : IDisposable
{
    private readonly GL _gl;
    private readonly IWindow _window;
    private readonly ShaderProgram _shader;
    private readonly uint _vertexArray;
    private readonly uint _vertexBuffer;
    private readonly uint _indexBuffer;
    private readonly uint _fontTexture;
    private int _vertexBufferSize = 10000;
    private int _indexBufferSize = 2000;

    public ImGuiController(GL gl, IWindow window)
    {
        _gl = gl;
        _window = window;
        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.Fonts.AddFontDefault();
        ImGui.StyleColorsDark();
        ApplyStyle();

        _shader = new ShaderProgram(_gl, VertexShaderSource, FragmentShaderSource);
        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();
        _indexBuffer = _gl.GenBuffer();

        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)_vertexBufferSize, null, BufferUsageARB.DynamicDraw);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)_indexBufferSize, null, BufferUsageARB.DynamicDraw);

        var stride = sizeof(ImDrawVert);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)8);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, (uint)stride, (void*)16);
        _gl.EnableVertexAttribArray(0);
        _gl.EnableVertexAttribArray(1);
        _gl.EnableVertexAttribArray(2);
        _gl.BindVertexArray(0);

        _fontTexture = CreateFontTexture();
    }

    public bool WantsMouse => ImGui.GetIO().WantCaptureMouse;

    public void Update(float deltaSeconds, Vector2 mousePosition, bool leftDown, bool rightDown, bool middleDown, float mouseWheel)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(Math.Max(1, _window.Size.X), Math.Max(1, _window.Size.Y));
        io.DisplayFramebufferScale = new Vector2(
            _window.FramebufferSize.X / Math.Max(1.0f, _window.Size.X),
            _window.FramebufferSize.Y / Math.Max(1.0f, _window.Size.Y));
        io.DeltaTime = Math.Max(1.0f / 240.0f, deltaSeconds);
        io.MousePos = mousePosition;
        io.MouseDown[0] = leftDown;
        io.MouseDown[1] = rightDown;
        io.MouseDown[2] = middleDown;
        io.MouseWheel = mouseWheel;

        ImGui.NewFrame();
    }

    public void Render()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    public void Dispose()
    {
        _gl.DeleteTexture(_fontTexture);
        _gl.DeleteBuffer(_indexBuffer);
        _gl.DeleteBuffer(_vertexBuffer);
        _gl.DeleteVertexArray(_vertexArray);
        _shader.Dispose();
        ImGui.DestroyContext();
    }

    private uint CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out var width, out var height, out _);

        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        io.Fonts.SetTexID((nint)texture);
        io.Fonts.ClearTexData();
        return texture;
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        var framebufferWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        var framebufferHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0) return;

        drawData.ScaleClipRects(drawData.FramebufferScale);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.ActiveTexture(TextureUnit.Texture0);

        _shader.Use();
        var projection = Matrix4x4.CreateOrthographicOffCenter(
            0.0f,
            drawData.DisplaySize.X,
            drawData.DisplaySize.Y,
            0.0f,
            -1.0f,
            1.0f);
        _shader.SetMatrix("uProjection", projection);
        _shader.SetInt("uTexture", 0);
        _gl.BindVertexArray(_vertexArray);

        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            var vertexBytes = cmdList.VtxBuffer.Size * sizeof(ImDrawVert);
            var indexBytes = cmdList.IdxBuffer.Size * sizeof(ushort);

            if (vertexBytes > _vertexBufferSize)
            {
                _vertexBufferSize = (int)(vertexBytes * 1.5f);
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)_vertexBufferSize, null, BufferUsageARB.DynamicDraw);
            }

            if (indexBytes > _indexBufferSize)
            {
                _indexBufferSize = (int)(indexBytes * 1.5f);
                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)_indexBufferSize, null, BufferUsageARB.DynamicDraw);
            }

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)vertexBytes, cmdList.VtxBuffer.Data.ToPointer());
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
            _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, (nuint)indexBytes, cmdList.IdxBuffer.Data.ToPointer());

            for (var cmdIndex = 0; cmdIndex < cmdList.CmdBuffer.Size; cmdIndex++)
            {
                var pcmd = cmdList.CmdBuffer[cmdIndex];
                _gl.BindTexture(TextureTarget.Texture2D, (uint)pcmd.TextureId);
                _gl.Scissor(
                    (int)pcmd.ClipRect.X,
                    (int)(framebufferHeight - pcmd.ClipRect.W),
                    (uint)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                    (uint)(pcmd.ClipRect.W - pcmd.ClipRect.Y));
                _gl.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    pcmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (void*)(pcmd.IdxOffset * sizeof(ushort)),
                    (int)pcmd.VtxOffset);
            }
        }

        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
        _gl.BindVertexArray(0);
    }

    private static void ApplyStyle()
    {
        var style = ImGui.GetStyle();
        style.WindowRounding = 10.0f;
        style.ChildRounding = 8.0f;
        style.PopupRounding = 8.0f;
        style.FrameRounding = 6.0f;
        style.GrabRounding = 6.0f;
        style.TabRounding = 6.0f;
        style.ScrollbarRounding = 8.0f;
        style.WindowBorderSize = 1.0f;
        style.FrameBorderSize = 0.0f;
        style.PopupBorderSize = 1.0f;
        style.ItemSpacing = new Vector2(8, 8);
        style.ItemInnerSpacing = new Vector2(6, 6);
        style.FramePadding = new Vector2(10, 6);
        style.WindowPadding = new Vector2(14, 14);
        style.ScrollbarSize = 10.0f;
        style.GrabMinSize = 12.0f;

        // Charcoal panels with a vivid orange accent.
        var panel = new Vector4(0.157f, 0.157f, 0.171f, 1.0f);
        var panelLight = new Vector4(0.196f, 0.196f, 0.212f, 1.0f);
        var frame = new Vector4(0.224f, 0.224f, 0.243f, 1.0f);
        var frameHover = new Vector4(0.271f, 0.271f, 0.294f, 1.0f);
        var frameActive = new Vector4(0.318f, 0.318f, 0.345f, 1.0f);
        var accent = new Vector4(0.949f, 0.329f, 0.114f, 1.0f);
        var accentSoft = new Vector4(0.949f, 0.329f, 0.114f, 0.35f);
        var accentHover = new Vector4(1.0f, 0.42f, 0.20f, 1.0f);

        var colors = style.Colors;
        colors[(int)ImGuiCol.WindowBg] = panel;
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.137f, 0.137f, 0.149f, 0.98f);
        colors[(int)ImGuiCol.Border] = new Vector4(1.0f, 1.0f, 1.0f, 0.06f);
        colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        colors[(int)ImGuiCol.FrameBg] = frame;
        colors[(int)ImGuiCol.FrameBgHovered] = frameHover;
        colors[(int)ImGuiCol.FrameBgActive] = frameActive;
        colors[(int)ImGuiCol.TitleBg] = panel;
        colors[(int)ImGuiCol.TitleBgActive] = panel;
        colors[(int)ImGuiCol.MenuBarBg] = panel;
        colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        colors[(int)ImGuiCol.ScrollbarGrab] = frameHover;
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = frameActive;
        colors[(int)ImGuiCol.ScrollbarGrabActive] = frameActive;
        colors[(int)ImGuiCol.CheckMark] = accent;
        colors[(int)ImGuiCol.SliderGrab] = accent;
        colors[(int)ImGuiCol.SliderGrabActive] = accentHover;
        colors[(int)ImGuiCol.Button] = frame;
        colors[(int)ImGuiCol.ButtonHovered] = frameHover;
        colors[(int)ImGuiCol.ButtonActive] = frameActive;
        colors[(int)ImGuiCol.Header] = accentSoft;
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.949f, 0.329f, 0.114f, 0.55f);
        colors[(int)ImGuiCol.HeaderActive] = accent;
        colors[(int)ImGuiCol.Separator] = new Vector4(1.0f, 1.0f, 1.0f, 0.08f);
        colors[(int)ImGuiCol.SeparatorHovered] = accentSoft;
        colors[(int)ImGuiCol.SeparatorActive] = accent;
        colors[(int)ImGuiCol.PlotHistogram] = accent;
        colors[(int)ImGuiCol.PlotHistogramHovered] = accentHover;
        colors[(int)ImGuiCol.Text] = new Vector4(0.925f, 0.925f, 0.933f, 1.0f);
        colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.58f, 0.58f, 0.62f, 1.0f);
        colors[(int)ImGuiCol.TextSelectedBg] = accentSoft;
        colors[(int)ImGuiCol.Tab] = panelLight;
        colors[(int)ImGuiCol.TabHovered] = accentHover;
        colors[(int)ImGuiCol.TabSelected] = accent;
    }

    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec2 aTexCoord;
        layout (location = 2) in vec4 aColor;
        uniform mat4 uProjection;
        out vec2 vTexCoord;
        out vec4 vColor;
        void main()
        {
            vTexCoord = aTexCoord;
            vColor = aColor;
            gl_Position = uProjection * vec4(aPosition, 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec2 vTexCoord;
        in vec4 vColor;
        uniform sampler2D uTexture;
        out vec4 FragColor;
        void main()
        {
            FragColor = vColor * texture(uTexture, vTexCoord);
        }
        """;
}
