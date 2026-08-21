using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

/// <summary>
/// Post-process bloom. Owns three framebuffers — one full-res
/// <em>emissive</em> target where glow content is drawn, and two
/// half-res <em>ping-pong</em> targets for the separable Gaussian
/// blur. Game code:
///
/// <list type="number">
///   <item><see cref="Begin"/> — bind emissive FBO, clear black.
///   Anything drawn to the framebuffer while this is bound
///   contributes to the halo.</item>
///   <item>Push emissive content via <see cref="AddPoint"/> (radial
///   soft glow around a world point) or by drawing via
///   <see cref="SpriteBatcher.Flush"/> while the target is bound.</item>
///   <item><see cref="Composite"/> — downsample the emissive to
///   ping-pong A, blur horizontally into B, blur vertically back
///   into A, then additively blit A onto the main framebuffer.</item>
/// </list>
///
/// The intensity + iterations knobs are static so game code can
/// tune them per scene (torch-lit village vs. neon magical trials)
/// without touching the engine.
/// </summary>
public static class Bloom
{
    /// <summary>Multiplier applied at composite. 0 = off, 1 = subtle,
    /// 2-3 = strong halo. Game code sets this per frame or per scene.</summary>
    public static float Intensity = 1.4f;

    /// <summary>Extra blur passes on top of the built-in H+V pair.
    /// One iteration is a 9-tap 2D Gaussian; two makes the halo
    /// wider + softer without a bigger kernel. Range 1..4 is sane.</summary>
    public static int Iterations = 2;

    /// <summary>Downsample factor for the ping-pong targets. Bigger
    /// = wider halo per iteration + cheaper blur. 2 or 4 keeps the
    /// halo crisp; 8 gets very soft.</summary>
    public static int DownsampleFactor = 2;

    private static Framebuffer? _emissive;
    private static Framebuffer? _pingA;
    private static Framebuffer? _pingB;
    private static BlurShader?           _blur;
    private static BloomCompositeShader? _composite;
    private static SolidGlowShader?      _pointGlow;
    private static uint _emptyVao;
    private static uint _pointVbo;
    private static bool _hostBound;
    /// <summary>Snapshot of the caller's viewport (in physical
    /// pixels — what glViewport actually cares about) taken in
    /// <see cref="Begin"/>. Restored in <see cref="Composite"/> so
    /// downstream UI passes render at full framebuffer size on
    /// Retina displays. Without this, we'd restore to the LOGICAL
    /// size and UI would render into a smaller sub-rect of the
    /// framebuffer.</summary>
    private static int  _savedVpX, _savedVpY, _savedVpW, _savedVpH;
    private static Matrix4x4 _hostProj;

    /// <summary>Emissive point glows queued between <see cref="Begin"/>
    /// and <see cref="Composite"/>. Stored as (cx, cy, radius, r, g, b, a)
    /// so a single glDrawArrays fires them all in one call.</summary>
    private static readonly List<float> _points = new();

    /// <summary>Start a new bloom frame. Snapshots the current GL
    /// viewport (in PHYSICAL pixels — what glViewport actually
    /// operates on; on Retina this is typically 2× the logical
    /// window size) so it can be restored precisely when the pass
    /// hands the framebuffer back. Without this snapshot, we'd
    /// restore to the caller's logical dimensions and every
    /// downstream draw (composite, UI, HUD) would render into a
    /// shrunken top-left sub-rect of the framebuffer.
    /// <paramref name="proj"/> is the same projection your scene
    /// uses so world coords map to the emissive target correctly.</summary>
    public static void Begin(Matrix4x4 proj, int viewportW, int viewportH)
    {
        EnsureResources(viewportW, viewportH);
        _hostProj = proj;

        // Save the current viewport in physical pixels. glGetInteger4
        // reads the 4-tuple {x, y, w, h}.
        var gl = Gfx.Gl;
        Span<int> vp = stackalloc int[4];
        unsafe { fixed (int* p = vp) gl.GetInteger(GetPName.Viewport, p); }
        _savedVpX = vp[0]; _savedVpY = vp[1];
        _savedVpW = vp[2]; _savedVpH = vp[3];

        // Clear ALL three targets — the ping-pong buffers hold last
        // frame's blurred content, and the blur passes below run
        // with blending DISABLED (replace), but a stray driver
        // that leaves state dirty would otherwise accumulate. Cheap
        // safety net.
        _emissive!.Clear(0f, 0f, 0f, 1f);
        _pingA!  .Clear(0f, 0f, 0f, 1f);
        _pingB!  .Clear(0f, 0f, 0f, 1f);
        _emissive.Bind();
        _points.Clear();
        _hostBound = true;
    }

    /// <summary>Queue a soft radial glow at (<paramref name="cx"/>,
    /// <paramref name="cy"/>) in world coords. Flushed by
    /// <see cref="Composite"/>. Cheap — one quad per call, batched
    /// into a single draw.</summary>
    public static void AddPoint(float cx, float cy, float radius,
                                float r, float g, float b, float intensity = 1f)
    {
        if (radius <= 0f) return;
        // Emit a single quad — four corners in world coords, each
        // with a local-space [-1..1] uv for the fragment shader to
        // compute the soft radial falloff from. Two triangles.
        float x0 = cx - radius, y0 = cy - radius;
        float x1 = cx + radius, y1 = cy + radius;
        Push(x0, y0, -1, -1, r, g, b, intensity);
        Push(x1, y0,  1, -1, r, g, b, intensity);
        Push(x1, y1,  1,  1, r, g, b, intensity);
        Push(x0, y0, -1, -1, r, g, b, intensity);
        Push(x1, y1,  1,  1, r, g, b, intensity);
        Push(x0, y1, -1,  1, r, g, b, intensity);

        static void Push(float x, float y, float u, float v, float r, float g, float b, float i)
        {
            _points.Add(x); _points.Add(y);
            _points.Add(u); _points.Add(v);
            _points.Add(r); _points.Add(g); _points.Add(b); _points.Add(i);
        }
    }

    /// <summary>Blur the emissive target, additively composite it
    /// onto the main framebuffer, and restore standard alpha blend
    /// for downstream UI passes.</summary>
    public static unsafe void Composite()
    {
        if (!_hostBound) return;
        var gl = Gfx.Gl;

        // 1. Flush queued point glows into the emissive target
        //    (this pass USES additive blending — multiple lights
        //    overlapping combine correctly).
        FlushPoints(gl);

        // 2. From here on the blur + copy passes REPLACE their
        //    destination, they don't add. Without disabling blend
        //    here, the still-additive state from FlushPoints would
        //    make the blur output accumulate onto whatever was in
        //    the ping-pong buffer from last frame — that's the
        //    "explosion covering the whole screen" bug.
        gl.Disable(EnableCap.Blend);

        // 3. Downsample-copy emissive into ping A.
        BlitCopy(gl, _emissive!, _pingA!);

        // 4. Iterated H + V Gaussian, ping-ponging between A and B.
        _blur!.Use();
        _blur.SetUniform("u_source", 0);
        gl.ActiveTexture(TextureUnit.Texture0);

        for (int i = 0; i < Iterations; i++)
        {
            _pingB!.Bind();
            gl.BindTexture(TextureTarget.Texture2D, _pingA!.ColorTex);
            _blur.SetUniform("u_direction", new Vector2(1f / _pingA.Width, 0f));
            DrawFullscreen(gl);

            _pingA.Bind();
            gl.BindTexture(TextureTarget.Texture2D, _pingB.ColorTex);
            _blur.SetUniform("u_direction", new Vector2(0f, 1f / _pingB.Height));
            DrawFullscreen(gl);
        }

        // 5. Bind the default framebuffer and additively composite
        //    the blurred emissive on top of the scene. Restore the
        //    caller's original viewport (physical pixels, snapshot
        //    in Begin) — NOT any logical size — so the composite
        //    triangle covers the full framebuffer and every
        //    downstream draw (UI, HUD, hover) does too.
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(_savedVpX, _savedVpY, (uint)_savedVpW, (uint)_savedVpH);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        _composite!.Use();
        _composite.SetUniform("u_bloom", 0);
        _composite.SetUniform("u_intensity", Intensity);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, _pingA!.ColorTex);
        DrawFullscreen(gl);
        // Restore standard alpha for downstream passes (UI text,
        // HUD, hover highlights).
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _hostBound = false;
    }

    // ── Internals ──────────────────────────────────────────────

    private static void EnsureResources(int w, int h)
    {
        var gl = Gfx.Gl;
        int dw = System.Math.Max(1, w / DownsampleFactor);
        int dh = System.Math.Max(1, h / DownsampleFactor);
        if (_emissive == null)
        {
            _emissive  = new Framebuffer(w,  h);
            _pingA     = new Framebuffer(dw, dh);
            _pingB     = new Framebuffer(dw, dh);
            _blur      = new BlurShader();
            _composite = new BloomCompositeShader();
            _pointGlow = new SolidGlowShader();
            _emptyVao  = gl.GenVertexArray();
            _pointVbo  = gl.GenBuffer();
            return;
        }
        _emissive.Resize(w, h);
        _pingA!.Resize(dw, dh);
        _pingB!.Resize(dw, dh);
    }

    private static unsafe void FlushPoints(GL gl)
    {
        if (_points.Count == 0) return;
        _emissive!.Bind();
        // Additive so overlapping glows combine correctly instead
        // of the last-drawn one clobbering.
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);

        _pointGlow!.Use();
        _pointGlow.SetUniform("u_proj", _hostProj);
        gl.BindVertexArray(_emptyVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _pointVbo);
        var arr = _points.ToArray();
        fixed (float* p = arr)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(float)),
                p, BufferUsageARB.StreamDraw);
        const int STRIDE = 8 * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, STRIDE, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, STRIDE, (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, STRIDE, (void*)(4 * sizeof(float)));
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(_points.Count / 8));
        gl.DisableVertexAttribArray(0);
        gl.DisableVertexAttribArray(1);
        gl.DisableVertexAttribArray(2);
        gl.BindVertexArray(0);
    }

    private static void BlitCopy(GL gl, Framebuffer src, Framebuffer dst)
    {
        dst.Bind();
        _blur!.Use();
        _blur.SetUniform("u_source", 0);
        _blur.SetUniform("u_direction", new Vector2(0f, 0f));
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, src.ColorTex);
        DrawFullscreen(gl);
    }

    private static void DrawFullscreen(GL gl)
    {
        gl.BindVertexArray(_emptyVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        gl.BindVertexArray(0);
    }
}

/// <summary>Fragment shader for <see cref="Bloom.AddPoint"/> quads —
/// a soft radial glow computed from the [-1..1] local coord. Same
/// falloff shape as <c>Lighting</c>: bright core, quadratic fade,
/// clean edge.</summary>
internal sealed class SolidGlowShader : Shader
{
    public SolidGlowShader() : base("bloom_glow")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec2 a_pos;
layout(location = 1) in vec2 a_local;
layout(location = 2) in vec4 a_color;   // rgb = tint, a = intensity

uniform mat4 u_proj;

out vec2 v_local;
out vec4 v_color;

void main() {
    gl_Position = u_proj * vec4(a_pos, 0.0, 1.0);
    v_local = a_local;
    v_color = a_color;
}";

    private const string FragmentSource = @"#version 410 core
in vec2 v_local;
in vec4 v_color;

out vec4 fragColor;

void main() {
    float d = length(v_local);           // 0 at centre, 1 at edge
    if (d >= 1.0) discard;
    float a = 1.0 - d;
    a = a * a;                           // quadratic fade
    fragColor = vec4(v_color.rgb * a * v_color.a, a);
}";
}
