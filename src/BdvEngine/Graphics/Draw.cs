using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

public static class Draw
{
    private const int FloatsPerVert = 7; // x,y,z, r,g,b,a

    private static float[] _triData = new float[FloatsPerVert * 6 * 1024];
    private static int _triCount;
    private static float[] _lineData = new float[FloatsPerVert * 2 * 512];
    private static int _lineCount;

    private static uint _triVao, _triVbo, _lineVao, _lineVbo;
    private static BatchColorShader? _batchShader;
    private static bool _initialized;

    public static Matrix4x4 Projection { get; internal set; } = Matrix4x4.Identity;

    private static void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        var gl = Gfx.Gl;

        _batchShader = new BatchColorShader();

        unsafe
        {
            uint stride = (uint)(FloatsPerVert * sizeof(float));

            _triVao = gl.GenVertexArray();
            _triVbo = gl.GenBuffer();
            gl.BindVertexArray(_triVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _triVbo);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

            _lineVao = gl.GenVertexArray();
            _lineVbo = gl.GenBuffer();
            gl.BindVertexArray(_lineVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

            gl.BindVertexArray(0);
        }
    }

    private static void EnsureTriCapacity(int needed)
    {
        if (_triData.Length >= needed) return;
        int n = _triData.Length;
        while (n < needed) n *= 2;
        Array.Resize(ref _triData, n);
    }

    private static void EnsureLineCapacity(int needed)
    {
        if (_lineData.Length >= needed) return;
        int n = _lineData.Length;
        while (n < needed) n *= 2;
        Array.Resize(ref _lineData, n);
    }

    public static void Rect(float x, float y, float w, float h, Color color)
    {
        float r = color.RFloat, g = color.GFloat, b = color.BFloat, a = color.AFloat;
        float x2 = x + w, y2 = y + h;

        int i = _triCount;
        EnsureTriCapacity(i + 42);
        var d = _triData;
        d[i] = x;     d[i+1] = y;  d[i+2] = 0; d[i+3] = r; d[i+4] = g; d[i+5] = b; d[i+6] = a;
        d[i+7]  = x;  d[i+8] = y2; d[i+9] = 0; d[i+10] = r; d[i+11] = g; d[i+12] = b; d[i+13] = a;
        d[i+14] = x2; d[i+15] = y2; d[i+16] = 0; d[i+17] = r; d[i+18] = g; d[i+19] = b; d[i+20] = a;
        d[i+21] = x2; d[i+22] = y2; d[i+23] = 0; d[i+24] = r; d[i+25] = g; d[i+26] = b; d[i+27] = a;
        d[i+28] = x2; d[i+29] = y;  d[i+30] = 0; d[i+31] = r; d[i+32] = g; d[i+33] = b; d[i+34] = a;
        d[i+35] = x;  d[i+36] = y;  d[i+37] = 0; d[i+38] = r; d[i+39] = g; d[i+40] = b; d[i+41] = a;
        _triCount = i + 42;
    }

    public static void RectOutline(float x, float y, float w, float h, Color color)
    {
        Line(x, y, x + w, y, color);
        Line(x + w, y, x + w, y + h, color);
        Line(x + w, y + h, x, y + h, color);
        Line(x, y + h, x, y, color);
    }

    public static void Circle(float cx, float cy, float radius, Color color, int segments = 32)
    {
        float r = color.RFloat, g = color.GFloat, b = color.BFloat, a = color.AFloat;
        int needed = _triCount + segments * 21;
        EnsureTriCapacity(needed);
        var d = _triData;
        int idx = _triCount;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * MathF.Tau;
            float a1 = (i + 1) / (float)segments * MathF.Tau;
            float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
            float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);

            d[idx]   = cx; d[idx+1] = cy; d[idx+2] = 0; d[idx+3] = r; d[idx+4] = g; d[idx+5] = b; d[idx+6] = a;
            d[idx+7] = cx + c0*radius; d[idx+8] = cy + s0*radius; d[idx+9] = 0; d[idx+10] = r; d[idx+11] = g; d[idx+12] = b; d[idx+13] = a;
            d[idx+14] = cx + c1*radius; d[idx+15] = cy + s1*radius; d[idx+16] = 0; d[idx+17] = r; d[idx+18] = g; d[idx+19] = b; d[idx+20] = a;
            idx += 21;
        }
        _triCount = idx;
    }

    public static void CircleOutline(float cx, float cy, float radius, Color color, int segments = 32)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * MathF.Tau;
            float a1 = (i + 1) / (float)segments * MathF.Tau;
            Line(cx + MathF.Cos(a0) * radius, cy + MathF.Sin(a0) * radius,
                 cx + MathF.Cos(a1) * radius, cy + MathF.Sin(a1) * radius, color);
        }
    }

    public static void Triangle(float x1, float y1, float x2, float y2, float x3, float y3, Color color)
    {
        float r = color.RFloat, g = color.GFloat, b = color.BFloat, a = color.AFloat;
        int i = _triCount;
        EnsureTriCapacity(i + 21);
        var d = _triData;
        d[i]   = x1; d[i+1] = y1; d[i+2] = 0; d[i+3] = r; d[i+4] = g; d[i+5] = b; d[i+6] = a;
        d[i+7] = x2; d[i+8] = y2; d[i+9] = 0; d[i+10] = r; d[i+11] = g; d[i+12] = b; d[i+13] = a;
        d[i+14] = x3; d[i+15] = y3; d[i+16] = 0; d[i+17] = r; d[i+18] = g; d[i+19] = b; d[i+20] = a;
        _triCount = i + 21;
    }

    public static void Point(float x, float y, Color color, float size = 4f)
    {
        float h = size / 2f;
        Rect(x - h, y - h, size, size, color);
    }

    public static void Line(float x1, float y1, float x2, float y2, Color color)
    {
        float r = color.RFloat, g = color.GFloat, b = color.BFloat, a = color.AFloat;
        int i = _lineCount;
        EnsureLineCapacity(i + 14);
        var d = _lineData;
        d[i]   = x1; d[i+1] = y1; d[i+2] = 0; d[i+3] = r; d[i+4] = g; d[i+5] = b; d[i+6] = a;
        d[i+7] = x2; d[i+8] = y2; d[i+9] = 0; d[i+10] = r; d[i+11] = g; d[i+12] = b; d[i+13] = a;
        _lineCount = i + 14;
    }

    public static void Ray(float ox, float oy, float dirX, float dirY, float length, Color color)
    {
        float mag = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (mag == 0f) return;
        Line(ox, oy, ox + dirX / mag * length, oy + dirY / mag * length, color);
    }

    public static unsafe void Flush(Shader parentShader)
    {
        if (_triCount == 0 && _lineCount == 0) return;
        EnsureInit();

        var gl = Gfx.Gl;
        var shader = _batchShader!;
        shader.Use();
        shader.SetUniform("u_proj", Projection);

        if (_triCount > 0)
        {
            gl.BindVertexArray(_triVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _triVbo);
            fixed (float* p = _triData)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_triCount * sizeof(float)), p, BufferUsageARB.DynamicDraw);
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(_triCount / FloatsPerVert));
            GLStats.IncDrawCalls(_triCount / FloatsPerVert);
            _triCount = 0;
        }

        if (_lineCount > 0)
        {
            gl.BindVertexArray(_lineVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);
            fixed (float* p = _lineData)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_lineCount * sizeof(float)), p, BufferUsageARB.DynamicDraw);
            gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(_lineCount / FloatsPerVert));
            GLStats.IncDrawCalls(_lineCount / FloatsPerVert);
            _lineCount = 0;
        }

        gl.BindVertexArray(0);
        parentShader.Use();
    }
}
