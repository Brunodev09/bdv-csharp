using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

/// <summary>
/// Draws every live particle of one <see cref="ParticleSystem3D"/> in a single instanced call.
///
/// <para>There is no quad mesh. The four corners come from <c>gl_VertexID</c> in the vertex shader
/// and the strip is expanded in view space against the camera's right/up vectors, so the only
/// buffer is the per-particle instance record and the only geometry is
/// <c>DrawArraysInstanced(TriangleStrip, 0, 4, n)</c>.</para>
/// </summary>
internal sealed class ParticleRenderer : IDisposable
{
    private readonly GL _gl = Gfx.Gl;
    private readonly ParticleShader _shader = new();

    private uint _vao, _vbo;
    private float[] _data = new float[1024];
    private int[] _order = new int[256];
    private Texture? _defaultTexture;

    /// <summary>Soft round dot, generated once so a particle system renders with no art at all.
    /// Alpha falls off smoothly to the rim; a hard-edged disc reads as confetti, not smoke.</summary>
    private Texture DefaultTexture
    {
        get
        {
            if (_defaultTexture != null) return _defaultTexture;

            const int N = 64;
            var px = new byte[N * N * 4];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f;
                float v = (y + 0.5f) / N * 2f - 1f;
                float r = MathF.Sqrt(u * u + v * v);
                // smoothstep(1, 0, r) — 1 at the centre, 0 at the rim, with no hard edge.
                float t = Math.Clamp(1f - r, 0f, 1f);
                float a = t * t * (3f - 2f * t);

                int o = (y * N + x) * 4;
                px[o + 0] = 255; px[o + 1] = 255; px[o + 2] = 255;
                px[o + 3] = (byte)(a * 255f);
            }

            _defaultTexture = Texture.CreateBlank("__particle_dot", N, N);
            _defaultTexture.UploadRgba(N, N, px);
            return _defaultTexture;
        }
    }

    private unsafe void EnsureBuffers()
    {
        if (_vao != 0) return;

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        uint stride = ParticleSystem3D.FloatsPerParticle * sizeof(float);
        // Every attribute is per-instance (divisor 1) — there are no per-vertex attributes at all,
        // because the corner comes from gl_VertexID.
        Attrib(0, 3, 0);    // centre
        Attrib(1, 2, 3);    // size
        Attrib(2, 1, 5);    // rotation
        Attrib(3, 4, 6);    // colour

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);

        void Attrib(uint index, int size, int offsetFloats)
        {
            _gl.EnableVertexAttribArray(index);
            _gl.VertexAttribPointer(index, size, VertexAttribPointerType.Float, false, stride,
                                    (void*)(offsetFloats * sizeof(float)));
            _gl.VertexAttribDivisor(index, 1);
        }
    }

    /// <summary>Draw the given systems, already sorted far-to-near by the caller.</summary>
    public unsafe void Draw(List<ParticleSystem3D> systems, Matrix4x4 proj, Matrix4x4 view,
                            Vector3 camPos, Vector3 camRight, Vector3 camUp)
    {
        if (systems.Count == 0) return;
        EnsureBuffers();

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(false);           // particles blend with each other; they must not occlude
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);

        _shader.Use();
        _shader.SetUniform("u_proj", proj);
        _shader.SetUniform("u_view", view);
        _shader.SetUniform("u_camRight", camRight);
        _shader.SetUniform("u_camUp", camUp);

        _gl.BindVertexArray(_vao);

        foreach (var ps in systems)
        {
            int count = ps.BuildInstances(ref _data, camPos, ref _order);
            if (count == 0) continue;

            _gl.BlendFunc(BlendingFactor.SrcAlpha,
                          ps.Blend == ParticleBlend.Additive
                              ? BlendingFactor.One                       // light adds
                              : BlendingFactor.OneMinusSrcAlpha);        // paint over

            var tex = ResolveTexture(ps);
            tex.Activate(0);
            _shader.SetUniform("u_diffuse", 0);

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (float* p = _data)
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                               (nuint)(count * ParticleSystem3D.FloatsPerParticle * sizeof(float)),
                               p, BufferUsageARB.StreamDraw);

            _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)count);
            GLStats.IncDrawCalls(4 * count);
        }

        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        // Leave the pipeline as we found it — the next frame's opaque pass assumes standard alpha
        // blending and a writable depth buffer.
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.CullFace);
    }

    private Texture ResolveTexture(ParticleSystem3D ps)
    {
        if (string.IsNullOrEmpty(ps.Texture)) return DefaultTexture;
        return TextureManager.Get(ps.Texture) ?? DefaultTexture;
    }

    public void Dispose()
    {
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}

/// <summary>Instanced camera-facing quads. The corner comes from <c>gl_VertexID</c>, so a particle
/// costs one 10-float instance record and no vertex buffer.</summary>
internal sealed class ParticleShader : Shader
{
    public ParticleShader() : base("particle3d") => Load(Vert, Frag);

    private const string Vert = @"#version 410 core
layout(location = 0) in vec3  i_center;
layout(location = 1) in vec2  i_size;
layout(location = 2) in float i_rot;
layout(location = 3) in vec4  i_color;

uniform mat4 u_proj, u_view;
uniform vec3 u_camRight, u_camUp;

out vec2 v_uv;
out vec4 v_color;

void main() {
    // Triangle-strip corners from the vertex index: (0,0) (1,0) (0,1) (1,1), recentred to
    // [-0.5, 0.5] so the quad is centred on the particle.
    vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1)) - 0.5;

    float s = sin(i_rot), c = cos(i_rot);
    vec2 r = vec2(corner.x * c - corner.y * s, corner.x * s + corner.y * c);

    vec3 world = i_center + u_camRight * (r.x * i_size.x) + u_camUp * (r.y * i_size.y);
    gl_Position = u_proj * u_view * vec4(world, 1.0);

    // UVs use the UNROTATED corner, so the texture turns with the quad rather than sliding
    // across a spinning frame.
    v_uv = corner + 0.5;
    v_color = i_color;
}";

    private const string Frag = @"#version 410 core
in vec2 v_uv;
in vec4 v_color;
uniform sampler2D u_diffuse;
out vec4 fragColor;
void main() {
    vec4 t = texture(u_diffuse, v_uv) * v_color;
    if (t.a <= 0.001) discard;   // additive blending still costs fill for fully faded particles
    fragColor = t;
}";
}
