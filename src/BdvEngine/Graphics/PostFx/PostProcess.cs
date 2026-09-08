using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

/// <summary>
/// The 3D post-process stack: an HDR scene target, a bloom chain, and a resolve that tonemaps and
/// grades on the way to the screen.
///
/// <para>Used by the engine around the scene pass, and inert unless
/// <see cref="PostFxSettings.Enabled"/> is set:</para>
/// <code>
/// if (post.Begin(env, fbW, fbH)) { RenderScene(); post.End(env, fbW, fbH); }
/// else                           { RenderScene(); }
/// </code>
///
/// <para><b>Why HDR is the whole point.</b> Rendering to an 8-bit buffer clamps every pixel at 1
/// before anything can look at it, so a bright highlight and a merely-white surface become the same
/// number and the information needed to bloom or tonemap is already gone. A half-float target keeps
/// values above 1, which is what makes a luminance threshold meaningful and what lets the tonemap
/// roll highlights off instead of clipping them flat.</para>
///
/// <para>Every pass is a full-screen triangle generated from <c>gl_VertexID</c> — no quad mesh, no
/// vertex buffer, and one fewer draw than a two-triangle quad.</para>
/// </summary>
public sealed class PostProcess : IDisposable
{
    private readonly GL _gl = Gfx.Gl;

    private Framebuffer? _hdr;
    private Framebuffer? _pingA;
    private Framebuffer? _pingB;

    private BrightPassShader? _bright;
    private BlurPassShader? _blur;
    private ResolveShader? _resolve;
    private uint _vao;

    /// <summary>True while the HDR target is bound — the engine uses it to decide whether the
    /// resolve is owed.</summary>
    public bool Active { get; private set; }

    /// <summary>Bind the HDR target and clear it. Returns false when post-processing is off, in
    /// which case the caller renders straight to the window exactly as before.</summary>
    public bool Begin(WorldEnvironment env, int fbWidth, int fbHeight)
    {
        var cfg = env.PostFx;
        if (!cfg.Enabled || fbWidth <= 0 || fbHeight <= 0)
        {
            Active = false;
            return false;
        }

        EnsureTargets(fbWidth, fbHeight, cfg);

        _hdr!.Bind();
        var sky = env.Sky;
        _gl.ClearColor(sky.X, sky.Y, sky.Z, 1f);
        _gl.DepthMask(true);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        Active = true;
        return true;
    }

    /// <summary>Bloom, tonemap and grade the HDR target onto the default framebuffer.</summary>
    public void End(WorldEnvironment env, int fbWidth, int fbHeight)
    {
        if (!Active) return;
        Active = false;

        var cfg = env.PostFx;
        var bloom = cfg.Bloom;

        // Full-screen work never depth-tests and must not be culled by winding.
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.BindVertexArray(_vao);

        uint bloomTex = 0;
        if (bloom.Enabled && bloom.Intensity > 0f)
        {
            // ── bright pass: HDR -> half(ish)-res ping A, keeping only what exceeds the threshold
            _pingA!.Bind();
            _bright!.Use();
            _bright.SetUniform("u_threshold", bloom.Threshold);
            _bright.SetUniform("u_knee", MathF.Max(bloom.Knee, 1e-4f));
            Sample(_hdr!.ColorTex, 0, _bright, "u_source");
            DrawFullscreen();

            // ── separable blur, ping-ponging. Two 1D passes per iteration is O(2n) taps instead of
            //    the O(n^2) a single 2D kernel would cost for the same radius.
            var src = _pingA;
            var dst = _pingB!;
            int iterations = Math.Clamp(bloom.Iterations, 1, 6);
            for (int i = 0; i < iterations * 2; i++)
            {
                dst.Bind();
                _blur!.Use();
                _blur.SetUniform("u_texel", new Vector2(1f / src.Width, 1f / src.Height));
                // Alternate horizontal/vertical; the pair is what makes it a 2D Gaussian.
                _blur.SetUniform("u_direction", (i & 1) == 0 ? new Vector2(1, 0) : new Vector2(0, 1));
                Sample(src.ColorTex, 0, _blur, "u_source");
                DrawFullscreen();
                (src, dst) = (dst, src);
            }
            bloomTex = src.ColorTex;   // the last thing written is now `src` after the final swap
        }

        // ── resolve to the window
        Framebuffer.Unbind(fbWidth, fbHeight);
        _resolve!.Use();
        _resolve.SetUniform("u_exposure", cfg.Exposure);
        _resolve.SetUniform("u_tonemap", (int)cfg.Tonemap);
        _resolve.SetUniform("u_bloomIntensity", bloomTex != 0 ? bloom.Intensity : 0f);
        _resolve.SetUniform("u_contrast", cfg.Contrast);
        _resolve.SetUniform("u_saturation", cfg.Saturation);
        _resolve.SetUniform("u_tint", cfg.Tint);
        _resolve.SetUniform("u_vignette", Math.Clamp(cfg.Vignette, 0f, 1f));
        _resolve.SetUniform("u_gamma", MathF.Max(cfg.Gamma, 0.01f));
        Sample(_hdr!.ColorTex, 0, _resolve, "u_scene");
        // Bind the scene again when bloom is off, so the sampler points at a real texture rather
        // than at whatever happened to be left on unit 1.
        Sample(bloomTex != 0 ? bloomTex : _hdr.ColorTex, 1, _resolve, "u_bloom");
        DrawFullscreen();

        _gl.BindVertexArray(0);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
    }

    private void Sample(uint tex, uint unit, Shader shader, string uniform)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)unit);
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        shader.SetUniform(uniform, (int)unit);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>One triangle that covers the screen. Larger than the viewport on purpose: a single
    /// oversized triangle has no diagonal seam and rasterises marginally better than two.</summary>
    private void DrawFullscreen()
    {
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GLStats.IncDrawCalls(3);
    }

    private void EnsureTargets(int w, int h, PostFxSettings cfg)
    {
        if (_vao == 0) _vao = _gl.GenVertexArray();
        _bright ??= new BrightPassShader();
        _blur ??= new BlurPassShader();
        _resolve ??= new ResolveShader();

        // Half-float, not 8-bit: the whole stack depends on values above 1 surviving the scene pass.
        _hdr ??= new Framebuffer(w, h, InternalFormat.Rgba16f, withDepth: true);
        _hdr.Resize(w, h);

        int div = Math.Clamp(cfg.Bloom.Downsample, 1, 8);
        int bw = Math.Max(w / div, 1), bh = Math.Max(h / div, 1);
        _pingA ??= new Framebuffer(bw, bh, InternalFormat.Rgba16f);
        _pingB ??= new Framebuffer(bw, bh, InternalFormat.Rgba16f);
        _pingA.Resize(bw, bh);
        _pingB.Resize(bw, bh);
    }

    public void Dispose()
    {
        _hdr?.Dispose();
        _pingA?.Dispose();
        _pingB?.Dispose();
        _bright?.Dispose();
        _blur?.Dispose();
        _resolve?.Dispose();
        if (_vao != 0) { _gl.DeleteVertexArray(_vao); _vao = 0; }
    }
}

/// <summary>Shared vertex stage: a full-screen triangle from <c>gl_VertexID</c>, no buffers.</summary>
internal static class FullscreenVert
{
    public const string Source = @"#version 410 core
out vec2 v_uv;
void main() {
    // (0,0) (2,0) (0,2) in UV -> a triangle that covers the [0,1] screen and overhangs the rest.
    v_uv = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(v_uv * 2.0 - 1.0, 0.0, 1.0);
}";
}

/// <summary>Keeps only what is brighter than the threshold, with a soft knee so highlights fade in
/// rather than switching on.</summary>
internal sealed class BrightPassShader : Shader
{
    public BrightPassShader() : base("postfx_bright") => Load(FullscreenVert.Source, Frag);

    private const string Frag = @"#version 410 core
in vec2 v_uv;
uniform sampler2D u_source;
uniform float u_threshold, u_knee;
out vec4 fragColor;

void main() {
    vec3 c = texture(u_source, v_uv).rgb;
    float luma = dot(c, vec3(0.2126, 0.7152, 0.0722));

    // Quadratic ramp across [threshold-knee, threshold+knee], flat 0 below and linear above.
    float soft = clamp(luma - u_threshold + u_knee, 0.0, 2.0 * u_knee);
    soft = soft * soft / (4.0 * u_knee);
    float contribution = max(soft, luma - u_threshold) / max(luma, 1e-5);

    fragColor = vec4(c * contribution, 1.0);
}";
}

/// <summary>One axis of a separable Gaussian. Run twice per iteration for a 2D blur.</summary>
internal sealed class BlurPassShader : Shader
{
    public BlurPassShader() : base("postfx_blur") => Load(FullscreenVert.Source, Frag);

    private const string Frag = @"#version 410 core
in vec2 v_uv;
uniform sampler2D u_source;
uniform vec2 u_texel, u_direction;
out vec4 fragColor;

// 9-tap Gaussian collapsed to 5 samples: linear filtering fetches two texels per sample when the
// coordinate sits between them, so the hardware does half the work for free.
const float OFFSETS[3] = float[](0.0, 1.3846153846, 3.2307692308);
const float WEIGHTS[3] = float[](0.2270270270, 0.3162162162, 0.0702702703);

void main() {
    vec2 step = u_texel * u_direction;
    vec3 sum = texture(u_source, v_uv).rgb * WEIGHTS[0];
    for (int i = 1; i < 3; i++) {
        vec2 o = step * OFFSETS[i];
        sum += texture(u_source, v_uv + o).rgb * WEIGHTS[i];
        sum += texture(u_source, v_uv - o).rgb * WEIGHTS[i];
    }
    fragColor = vec4(sum, 1.0);
}";
}

/// <summary>Combine scene + bloom, expose, tonemap, grade, vignette, gamma.</summary>
internal sealed class ResolveShader : Shader
{
    public ResolveShader() : base("postfx_resolve") => Load(FullscreenVert.Source, Frag);

    private const string Frag = @"#version 410 core
in vec2 v_uv;
uniform sampler2D u_scene, u_bloom;
uniform float u_exposure, u_bloomIntensity, u_contrast, u_saturation, u_vignette, u_gamma;
uniform vec3  u_tint;
uniform int   u_tonemap;
out vec4 fragColor;

vec3 reinhard(vec3 x) { return x / (1.0 + x); }

// Narkowicz's ACES fit: a filmic S-curve in five constants. Holds colour in highlights, where a
// naive clamp would drive everything to white.
vec3 aces(vec3 x) {
    const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() {
    vec3 color = texture(u_scene, v_uv).rgb;
    color += texture(u_bloom, v_uv).rgb * u_bloomIntensity;

    color *= u_exposure;

    if      (u_tonemap == 1) color = reinhard(color);
    else if (u_tonemap == 2) color = aces(color);
    else                     color = clamp(color, 0.0, 1.0);

    // Grade in display space, after the tonemap: contrast pivots on mid-grey so it opens shadows
    // and highlights symmetrically instead of just scaling the image.
    color = (color - 0.5) * u_contrast + 0.5;
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luma), color, u_saturation);
    color *= u_tint;

    if (u_vignette > 0.0) {
        vec2 d = v_uv - 0.5;
        // smoothstep on squared radius: cheap, and the falloff reads as a lens rather than a ring.
        float v = smoothstep(0.75, 0.25, dot(d, d) * 2.0);
        color *= mix(1.0, v, u_vignette);
    }

    color = max(color, 0.0);
    fragColor = vec4(pow(color, vec3(1.0 / u_gamma)), 1.0);
}";
}
