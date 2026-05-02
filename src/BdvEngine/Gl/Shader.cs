using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

public abstract class Shader : IDisposable
{
    private readonly GL _gl;
    private uint _program;
    private readonly Dictionary<string, int> _uniforms = new();
    private readonly Dictionary<string, int> _attributes = new();

    public string Name { get; }

    protected Shader(string name)
    {
        Name = name;
        _gl = Gfx.Gl;
    }

    public void Use() => _gl.UseProgram(_program);

    public int GetAttribLocation(string name)
    {
        if (_attributes.TryGetValue(name, out var loc)) return loc;
        loc = _gl.GetAttribLocation(_program, name);
        if (loc < 0) throw new InvalidOperationException(
            $"Shader '{Name}': attribute '{name}' not found.");
        _attributes[name] = loc;
        return loc;
    }

    public int GetUniformLocation(string name)
    {
        if (_uniforms.TryGetValue(name, out var loc)) return loc;
        loc = _gl.GetUniformLocation(_program, name);
        if (loc < 0) throw new InvalidOperationException(
            $"Shader '{Name}': uniform '{name}' not found.");
        _uniforms[name] = loc;
        return loc;
    }

    public void SetUniform(string name, int v) => _gl.Uniform1(GetUniformLocation(name), v);
    public void SetUniform(string name, float v) => _gl.Uniform1(GetUniformLocation(name), v);
    public void SetUniform(string name, Vector2 v) => _gl.Uniform2(GetUniformLocation(name), v.X, v.Y);
    public void SetUniform(string name, Vector3 v) => _gl.Uniform3(GetUniformLocation(name), v.X, v.Y, v.Z);
    public void SetUniform(string name, Vector4 v) => _gl.Uniform4(GetUniformLocation(name), v.X, v.Y, v.Z, v.W);
    public unsafe void SetUniform(string name, Matrix4x4 m)
    {
        // System.Numerics row-vector matrices in row-major memory are byte-equivalent
        // to GLSL column-vector matrices in column-major memory. No transpose needed.
        _gl.UniformMatrix4(GetUniformLocation(name), 1, false, (float*)&m);
    }

    protected void Load(string vertexSource, string fragmentSource)
    {
        uint vs = Compile(ShaderType.VertexShader, vertexSource);
        uint fs = Compile(ShaderType.FragmentShader, fragmentSource);

        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, vs);
        _gl.AttachShader(_program, fs);
        _gl.LinkProgram(_program);
        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
            throw new Exception($"Shader '{Name}' link error: {_gl.GetProgramInfoLog(_program)}");

        _gl.DetachShader(_program, vs);
        _gl.DetachShader(_program, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
    }

    private uint Compile(ShaderType type, string source)
    {
        uint id = _gl.CreateShader(type);
        _gl.ShaderSource(id, source);
        _gl.CompileShader(id);
        _gl.GetShader(id, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = _gl.GetShaderInfoLog(id);
            _gl.DeleteShader(id);
            throw new Exception($"Shader '{Name}' {type} compile error: {log}");
        }
        return id;
    }

    public void Dispose()
    {
        if (_program != 0) _gl.DeleteProgram(_program);
        _program = 0;
    }
}
