using System.Numerics;
using Silk.NET.OpenGL;

namespace WorldEditor.App;

internal sealed unsafe class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        var vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        var fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);

        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);
        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
        {
            throw new InvalidOperationException(_gl.GetProgramInfoLog(_handle));
        }

        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    public void Use() => _gl.UseProgram(_handle);

    public void SetVector(string name, Vector4 value)
    {
        var location = _gl.GetUniformLocation(_handle, name);
        _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    public void SetInt(string name, int value)
    {
        var location = _gl.GetUniformLocation(_handle, name);
        _gl.Uniform1(location, value);
    }

    public void SetMatrix(string name, Matrix4x4 value)
    {
        var location = _gl.GetUniformLocation(_handle, name);
        _gl.UniformMatrix4(location, 1, false, (float*)&value);
    }

    public void Dispose() => _gl.DeleteProgram(_handle);

    private uint CompileShader(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
        {
            throw new InvalidOperationException(_gl.GetShaderInfoLog(shader));
        }

        return shader;
    }
}
