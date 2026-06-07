using Silk.NET.OpenGL;

namespace WorldEditor.App;

internal sealed unsafe class MeshBuffer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly uint _indexCount;

    private MeshBuffer(GL gl, uint vao, uint vbo, uint ebo, uint indexCount)
    {
        _gl = gl;
        _vao = vao;
        _vbo = vbo;
        _ebo = ebo;
        _indexCount = indexCount;
    }

    public static MeshBuffer Create(GL gl, ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices)
    {
        var vao = gl.GenVertexArray();
        var vbo = gl.GenBuffer();
        var ebo = gl.GenBuffer();

        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (float* vertexPtr = vertices)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), vertexPtr, BufferUsageARB.DynamicDraw);
        }

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
        fixed (uint* indexPtr = indices)
        {
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), indexPtr, BufferUsageARB.DynamicDraw);
        }

        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 10 * sizeof(float), (void*)0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 10 * sizeof(float), (void*)(3 * sizeof(float)));
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 10 * sizeof(float), (void*)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(0);
        gl.EnableVertexAttribArray(1);
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);

        return new MeshBuffer(gl, vao, vbo, ebo, (uint)indices.Length);
    }

    public void DrawTriangles()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, null);
        _gl.BindVertexArray(0);
    }

    public void DrawLines()
    {
        _gl.BindVertexArray(_vao);
        _gl.LineWidth(2.0f);
        _gl.DrawElements(PrimitiveType.Lines, _indexCount, DrawElementsType.UnsignedInt, null);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
