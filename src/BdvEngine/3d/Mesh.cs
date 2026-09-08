using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

public sealed class Mesh : IDisposable
{
    /// <summary>Static vertex layout: pos(3) + normal(3) + uv(2).</summary>
    public const int FloatsPerVertex = 8;

    /// <summary>Skinned vertex layout: the static 8 + joints(4) + weights(4). Joint indices ride
    /// as floats — exact for any joint count we'd ever hit, and it keeps one interleaved buffer
    /// instead of a second VBO just for two attributes.</summary>
    public const int SkinnedFloatsPerVertex = 16;

    /// <summary>True when this mesh carries JOINTS_0/WEIGHTS_0 and must be drawn by a skinning
    /// shader with a joint-matrix palette (see <see cref="Skin"/>).</summary>
    public bool IsSkinned { get; }

    /// <summary>Floats per vertex in THIS mesh — 8 static, 16 skinned.</summary>
    public int Stride { get; }

    /// <summary>Local-space axis-aligned bounds (from the vertex positions) — used by ray picking.</summary>
    public Vector3 BoundsMin { get; private set; }
    public Vector3 BoundsMax { get; private set; }

    /// <summary>Draw primitive — Triangles by default; Lines for helpers like a ground grid.</summary>
    public PrimitiveType Primitive { get; set; } = PrimitiveType.Triangles;

    /// <summary>How this mesh was built, as a serialisable spec — e.g. <c>"cube"</c>,
    /// <c>"sphere:24,16"</c>, <c>"plane:20"</c>. Set by the primitive factories below (so a game
    /// calling <c>Mesh.Cube()</c> directly is serialisable too); null for meshes assembled by hand
    /// from vertices or imported from a model (those serialise via the owner's model path instead). <see cref="SceneSerializer"/> uses it to write a mesh back out and to share one
    /// GPU buffer across every node with the same spec.</summary>
    public string? Source { get; set; }

    private readonly GL _gl = Gfx.Gl;
    private readonly float[] _vertexData;
    private readonly ushort[]? _indexData;
    private readonly uint[]? _indexData32;   // 32-bit index path for meshes with > 65 535 vertices (glTF)
    private readonly int _vertexCount;
    private readonly int _indexCount;

    private uint _vao;
    private uint _vbo;
    private uint _ibo;
    private bool _initialized;

    public Mesh(float[] vertices, ushort[]? indices = null, bool skinned = false)
    {
        IsSkinned = skinned;
        Stride = skinned ? SkinnedFloatsPerVertex : FloatsPerVertex;
        _vertexData = vertices;
        _vertexCount = vertices.Length / Stride;
        _indexData = indices;
        _indexCount = indices?.Length ?? 0;
        ComputeBounds();
    }

    /// <summary>32-bit indexed mesh — for imported models (glTF) whose primitives exceed the
    /// 65 535-vertex limit of the ushort path.</summary>
    public Mesh(float[] vertices, uint[] indices32, bool skinned = false)
    {
        IsSkinned = skinned;
        Stride = skinned ? SkinnedFloatsPerVertex : FloatsPerVertex;
        _vertexData = vertices;
        _vertexCount = vertices.Length / Stride;
        _indexData32 = indices32;
        _indexCount = indices32.Length;
        ComputeBounds();
    }

    private void ComputeBounds()
    {
        if (_vertexCount == 0) return;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < _vertexCount; i++)
        {
            int o = i * Stride;
            var p = new Vector3(_vertexData[o], _vertexData[o + 1], _vertexData[o + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        BoundsMin = min;
        BoundsMax = max;
    }

    private unsafe void EnsureGl()
    {
        if (_initialized) return;
        _initialized = true;

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = _vertexData)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertexData.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);

        uint stride = (uint)Stride * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        if (IsSkinned)
        {
            _gl.EnableVertexAttribArray(3);   // joint indices (as floats)
            _gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
            _gl.EnableVertexAttribArray(4);   // joint weights
            _gl.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, stride, (void*)(12 * sizeof(float)));
        }

        if (_indexData != null)
        {
            _ibo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
            fixed (ushort* p = _indexData)
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(_indexData.Length * sizeof(ushort)), p, BufferUsageARB.StaticDraw);
        }
        else if (_indexData32 != null)
        {
            _ibo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
            fixed (uint* p = _indexData32)
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(_indexData32.Length * sizeof(uint)), p, BufferUsageARB.StaticDraw);
        }

        _gl.BindVertexArray(0);
    }

    public unsafe void Draw()
    {
        EnsureGl();
        _gl.BindVertexArray(_vao);
        if (_ibo != 0)
        {
            var type = _indexData32 != null ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort;
            _gl.DrawElements(Primitive, (uint)_indexCount, type, null);
            GLStats.IncDrawCalls(_indexCount);
        }
        else
        {
            _gl.DrawArrays(Primitive, 0, (uint)_vertexCount);
            GLStats.IncDrawCalls(_vertexCount);
        }
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_ibo != 0) _gl.DeleteBuffer(_ibo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
    }

    public static Mesh Cube()
    {
        var v = new List<float>();
        var idx = new List<ushort>();

        void Face(float[] p0, float[] p1, float[] p2, float[] p3, float[] n)
        {
            ushort baseIdx = (ushort)(v.Count / FloatsPerVertex);
            void Push(float[] p, float u, float vv)
            {
                v.Add(p[0]); v.Add(p[1]); v.Add(p[2]);
                v.Add(n[0]); v.Add(n[1]); v.Add(n[2]);
                v.Add(u); v.Add(vv);
            }
            Push(p0, 0, 0); Push(p1, 1, 0); Push(p2, 1, 1); Push(p3, 0, 1);
            idx.Add(baseIdx); idx.Add((ushort)(baseIdx + 1)); idx.Add((ushort)(baseIdx + 2));
            idx.Add(baseIdx); idx.Add((ushort)(baseIdx + 2)); idx.Add((ushort)(baseIdx + 3));
        }

        Face(new[] { -0.5f, -0.5f,  0.5f }, new[] {  0.5f, -0.5f,  0.5f }, new[] {  0.5f,  0.5f,  0.5f }, new[] { -0.5f,  0.5f,  0.5f }, new[] { 0f, 0f, 1f });
        Face(new[] {  0.5f, -0.5f, -0.5f }, new[] { -0.5f, -0.5f, -0.5f }, new[] { -0.5f,  0.5f, -0.5f }, new[] {  0.5f,  0.5f, -0.5f }, new[] { 0f, 0f, -1f });
        Face(new[] { -0.5f,  0.5f,  0.5f }, new[] {  0.5f,  0.5f,  0.5f }, new[] {  0.5f,  0.5f, -0.5f }, new[] { -0.5f,  0.5f, -0.5f }, new[] { 0f, 1f, 0f });
        Face(new[] { -0.5f, -0.5f, -0.5f }, new[] {  0.5f, -0.5f, -0.5f }, new[] {  0.5f, -0.5f,  0.5f }, new[] { -0.5f, -0.5f,  0.5f }, new[] { 0f, -1f, 0f });
        Face(new[] {  0.5f, -0.5f,  0.5f }, new[] {  0.5f, -0.5f, -0.5f }, new[] {  0.5f,  0.5f, -0.5f }, new[] {  0.5f,  0.5f,  0.5f }, new[] { 1f, 0f, 0f });
        Face(new[] { -0.5f, -0.5f, -0.5f }, new[] { -0.5f, -0.5f,  0.5f }, new[] { -0.5f,  0.5f,  0.5f }, new[] { -0.5f,  0.5f, -0.5f }, new[] { -1f, 0f, 0f });

        return new Mesh(v.ToArray(), idx.ToArray()) { Source = "cube" };
    }

    public static Mesh Plane(float size = 1f)
    {
        float h = size / 2f;
        var v = new float[]
        {
            -h, 0, -h,  0, 1, 0,  0, 0,
             h, 0, -h,  0, 1, 0,  1, 0,
             h, 0,  h,  0, 1, 0,  1, 1,
            -h, 0,  h,  0, 1, 0,  0, 1,
            -h, 0,  h,  0, -1, 0,  0, 0,
             h, 0,  h,  0, -1, 0,  1, 0,
             h, 0, -h,  0, -1, 0,  1, 1,
            -h, 0, -h,  0, -1, 0,  0, 1,
        };
        var idx = new ushort[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        return new Mesh(v, idx) { Source = $"plane:{size.ToString(System.Globalization.CultureInfo.InvariantCulture)}" };
    }

    public static Mesh Sphere(int segments = 16, int rings = 12)
    {
        var v = new List<float>();
        var idx = new List<ushort>();

        for (int r = 0; r <= rings; r++)
        {
            float phi = r / (float)rings * MathF.PI;
            float sp = MathF.Sin(phi), cp = MathF.Cos(phi);
            for (int s = 0; s <= segments; s++)
            {
                float theta = s / (float)segments * MathF.Tau;
                float st = MathF.Sin(theta), ct = MathF.Cos(theta);
                float x = ct * sp, y = cp, z = st * sp;
                float u = s / (float)segments, vv = r / (float)rings;
                v.Add(x * 0.5f); v.Add(y * 0.5f); v.Add(z * 0.5f);
                v.Add(x); v.Add(y); v.Add(z);
                v.Add(u); v.Add(vv);
            }
        }

        for (int r = 0; r < rings; r++)
        for (int s = 0; s < segments; s++)
        {
            ushort a = (ushort)(r * (segments + 1) + s);
            ushort b = (ushort)(a + segments + 1);
            idx.Add(a); idx.Add(b); idx.Add((ushort)(a + 1));
            idx.Add((ushort)(a + 1)); idx.Add(b); idx.Add((ushort)(b + 1));
        }

        return new Mesh(v.ToArray(), idx.ToArray()) { Source = $"sphere:{segments},{rings}" };
    }
}
