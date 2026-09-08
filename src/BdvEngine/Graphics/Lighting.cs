using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

/// <summary>
/// Deferred-ish 2D lighting via a single fullscreen multiply pass, with
/// optional occlusion (walls block light + enclosed tiles stay dark).
///
/// After the scene is rendered (via SpriteBatcher.Flush()), call
/// <see cref="Render"/> with the projection matrix + the visible world
/// rectangle. The pass draws ONE world-space quad with blend
/// <c>(DST_COLOR, ZERO)</c> — i.e. <c>framebuffer *= lightColor</c>.
/// The fragment shader computes light as
///
/// <code>
/// light = ambient * skyExposure(tile) + Σ pointLight(i) * notShadowed(i)
/// </code>
///
/// where
/// <list type="bullet">
///   <item><c>ambient &lt; 1</c> → night darkening</item>
///   <item><c>skyExposure</c> → 0 for enclosed tiles (caves) so they
///   stay dark even in full daylight</item>
///   <item>per-light shadow ray-march → a light's contribution is cut
///   if a wall lies between the pixel and the light (no bleeding
///   through mountains)</item>
/// </list>
///
/// Occlusion data is an RGBA texture (one texel per world tile):
/// <list type="bullet">
///   <item>R = 1 → wall (blocks light rays)</item>
///   <item>G = sky exposure (1 = open, 0 = enclosed)</item>
/// </list>
/// Supplied via <see cref="SetOccluder"/>. Without it, lighting is
/// unoccluded.
/// </summary>
public static class Lighting
{
    /// <summary>Max simultaneous dynamic lights uploaded to the shader.</summary>
    public const int MaxLights = 32;

    private static LightingShader? _shader;
    private static uint _vbo;
    private static uint _vao;

    private static float _ambient = 1f;
    private static int _count;
    private static readonly float[] _posXY  = new float[MaxLights * 2];
    private static readonly float[] _radius = new float[MaxLights];
    private static readonly float[] _colRGB = new float[MaxLights * 3];
    private static readonly float[] _dirXY   = new float[MaxLights * 2];   // spot facing (unit)
    private static readonly float[] _coneCos = new float[MaxLights];       // cos(half-angle); -1 = omni
    private static readonly float[] _quad   = new float[8];

    private static uint _occTex;
    private static bool _hasOccluder;
    private static int _worldW = 1;
    private static int _worldH = 1;

    public static int LightCount => _count;

    /// <summary>How far above the ground plane point lights float when a
    /// forward-lit (normal-mapped) sprite shades against them. A larger
    /// value flattens the gradient (more top-down); smaller rakes light
    /// harder across sprite faces. Purely a look knob for the 2.5D pass.</summary>
    public static float LightHeight = 48f;

    /// <summary>World-space direction TO the sun for normal-mapped "form"
    /// shading (see <see cref="NormalLitSpriteShader"/>). Need not be
    /// normalized — <see cref="UploadForward"/> normalizes it. A positive
    /// Z keeps it partly camera-facing so top-down sprites still round.</summary>
    public static Vector3 SunDir = new(0.35f, 0.45f, 0.82f);
    /// <summary>Strength of the sun form shading (0 = flat, ~0.5 = clear
    /// rounded form, 1 = strong).</summary>
    public static float FormAmount = 0.55f;
    /// <summary>Strength of the additive point-light rim on normal-mapped
    /// sprites (the "torch rakes the wall" highlight).</summary>
    public static float PointAmount = 0.7f;

    /// <summary>
    /// Push the current ambient + point-light set into a FORWARD sprite
    /// shader (e.g. <see cref="NormalLitSpriteShader"/>) so it can do
    /// per-pixel <c>N·L</c> lighting at draw time — as opposed to
    /// <see cref="Render"/>, which lights the whole framebuffer by world
    /// position in one multiply pass. Uploads the same arrays
    /// <see cref="AddLight"/> populated this frame. The shader must be
    /// bound (Use()) before calling. Uniforms it doesn't declare are
    /// silently skipped, so this is safe to call speculatively.
    /// </summary>
    public static unsafe void UploadForward(Shader shader)
    {
        var gl = Gfx.Gl;
        TrySet(shader, "u_ambient", _ambient);
        TrySet(shader, "u_lightHeight", LightHeight);
        TrySet(shader, "u_formAmount", FormAmount);
        TrySet(shader, "u_pointAmount", PointAmount);
        var sun = Vector3.Normalize(SunDir);
        try { shader.SetUniform("u_sunDir", sun); } catch { /* not declared */ }
        try { shader.SetUniform("u_lightCount", _count); } catch { /* not declared */ }
        if (_count > 0)
        {
            // GetUniformLocation throws when a uniform is absent, so guard
            // the whole array upload — a shader without these declared
            // simply skips them.
            try
            {
                int locPos    = shader.GetUniformLocation("u_lightPos[0]");
                int locRadius = shader.GetUniformLocation("u_lightRadius[0]");
                int locColor  = shader.GetUniformLocation("u_lightColor[0]");
                fixed (float* p = _posXY)  gl.Uniform2(locPos,    (uint)_count, p);
                fixed (float* p = _radius) gl.Uniform1(locRadius, (uint)_count, p);
                fixed (float* p = _colRGB) gl.Uniform3(locColor,  (uint)_count, p);
            }
            catch { /* shader has no light arrays; nothing to upload */ }
        }
    }

    private static void TrySet(Shader shader, string name, float value)
    {
        try { shader.SetUniform(name, value); } catch { /* uniform not active */ }
    }

    public static void Begin(float ambient)
    {
        _ambient = ambient;
        _count = 0;
    }

    /// <summary>Add an omnidirectional (radial) point light.</summary>
    public static void AddLight(float x, float y, float radius, float r, float g, float b)
        => AddSpot(x, y, radius, r, g, b, 0f, 0f, -1f);

    /// <summary>Add a light restricted to a cone facing (<paramref name="dirX"/>,<paramref name="dirY"/>)
    /// (roughly unit length). <paramref name="coneCos"/> is the cosine of the cone's half-angle;
    /// pass -1 for a full circle. Use it for a directional vision/FOV that follows the aim.</summary>
    public static void AddSpot(float x, float y, float radius, float r, float g, float b,
        float dirX, float dirY, float coneCos)
    {
        int i = _count;
        if (i >= MaxLights) return;
        _count = i + 1;
        _posXY[i * 2]     = x;
        _posXY[i * 2 + 1] = y;
        _radius[i]        = radius;
        _colRGB[i * 3]     = r;
        _colRGB[i * 3 + 1] = g;
        _colRGB[i * 3 + 2] = b;
        _dirXY[i * 2]     = dirX;
        _dirXY[i * 2 + 1] = dirY;
        _coneCos[i]       = coneCos;
    }

    /// <summary>
    /// Upload / refresh the occlusion map. <paramref name="pixels"/> is
    /// RGBA, <paramref name="texW"/>×<paramref name="texH"/> texels (one
    /// per world tile). <paramref name="worldW"/> / <paramref name="worldH"/>
    /// are the world's total pixel dimensions so the shader can map a
    /// world position to a texel. NEAREST-filtered + clamped (tile-precise,
    /// no bleed).
    /// </summary>
    public static unsafe void SetOccluder(ReadOnlySpan<byte> pixels, int texW, int texH,
        int worldW, int worldH)
    {
        var gl = Gfx.Gl;
        if (_occTex == 0) _occTex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _occTex);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* p = pixels)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba,
                (uint)texW, (uint)texH, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _hasOccluder = true;
        _worldW = worldW;
        _worldH = worldH;
    }

    public static void ClearOccluder() => _hasOccluder = false;

    public static unsafe void Render(Matrix4x4 proj,
        float minX, float minY, float maxX, float maxY)
    {
        // Skip only when there's truly nothing to do: full day, no
        // lights, and no occluder (so no caves to darken).
        if (_ambient >= 0.999f && _count == 0 && !_hasOccluder) return;

        var gl = Gfx.Gl;
        if (_shader == null)
        {
            _shader = new LightingShader();
            _vbo = gl.GenBuffer();
            _vao = gl.GenVertexArray();
        }
        _shader.Use();
        _shader.SetUniform("u_proj", proj);
        _shader.SetUniform("u_ambient", _ambient);
        _shader.SetUniform("u_lightCount", _count);
        if (_count > 0)
        {
            int locPos    = _shader.GetUniformLocation("u_lightPos[0]");
            int locRadius = _shader.GetUniformLocation("u_lightRadius[0]");
            int locColor  = _shader.GetUniformLocation("u_lightColor[0]");
            int locDir    = _shader.GetUniformLocation("u_lightDir[0]");
            int locCone   = _shader.GetUniformLocation("u_lightCone[0]");
            fixed (float* p = _posXY)   gl.Uniform2(locPos,    (uint)_count, p);
            fixed (float* p = _radius)  gl.Uniform1(locRadius, (uint)_count, p);
            fixed (float* p = _colRGB)  gl.Uniform3(locColor,  (uint)_count, p);
            fixed (float* p = _dirXY)   gl.Uniform2(locDir,    (uint)_count, p);
            fixed (float* p = _coneCos) gl.Uniform1(locCone,   (uint)_count, p);
        }

        // Occluder binding + world→UV transform.
        _shader.SetUniform("u_hasOccluder", _hasOccluder ? 1f : 0f);
        _shader.SetUniform("u_worldToUV", new Vector2(1f / _worldW, 1f / _worldH));
        if (_hasOccluder && _occTex != 0)
        {
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, _occTex);
            _shader.SetUniform("u_occluder", 0);
        }

        // Fullscreen-ish quad covering the visible world rectangle.
        _quad[0] = minX; _quad[1] = minY;
        _quad[2] = minX; _quad[3] = maxY;
        _quad[4] = maxX; _quad[5] = maxY;
        _quad[6] = maxX; _quad[7] = minY;
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = _quad)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_quad.Length * sizeof(float)),
                p, BufferUsageARB.DynamicDraw);
        const uint POS_LOC = 0;
        gl.EnableVertexAttribArray(POS_LOC);
        gl.VertexAttribPointer(POS_LOC, 2, VertexAttribPointerType.Float, false,
            2 * sizeof(float), (void*)0);

        // Multiply blend = framebuffer *= shaderOutput. Depth test OFF — this is a fullscreen pass
        // and must dim EVERY pixel (the SpriteBatcher object layer leaves depth-test on, which would
        // otherwise reject the multiply quad over sorted sprites and leave them undimmed).
        gl.Disable(EnableCap.DepthTest);
        gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.Zero);
        gl.DrawArrays(PrimitiveType.TriangleFan, 0, 4);
        // Restore standard alpha blend so downstream UI / text renders normally.
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        gl.DisableVertexAttribArray(POS_LOC);
        gl.BindVertexArray(0);
    }
}

/// <summary>
/// Fullscreen-quad shader for the 2D lighting multiply pass. Computes
/// ambient + per-light contribution (with optional ray-march shadows
/// against an occluder texture). Output goes straight into the colour
/// buffer with multiply blend, dimming pixels by how lit they are.
/// </summary>
internal sealed class LightingShader : Shader
{
    /// <summary>Ray-march steps per light for the shadow test. Higher =
    /// crisper shadows but more texture samples per lit pixel.</summary>
    private const int ShadowSteps = 16;

    public LightingShader() : base("lighting")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
layout(location = 0) in vec2 a_pos;
uniform mat4 u_proj;
out vec2 v_world;
void main() {
    gl_Position = u_proj * vec4(a_pos, 0.0, 1.0);
    v_world = a_pos;
}";

    private static readonly string FragmentSource = $@"#version 410 core
#define MAX_LIGHTS {Lighting.MaxLights}
#define SHADOW_STEPS {ShadowSteps}
uniform float u_ambient;
uniform int   u_lightCount;
uniform vec2  u_lightPos[MAX_LIGHTS];
uniform float u_lightRadius[MAX_LIGHTS];
uniform vec3  u_lightColor[MAX_LIGHTS];
uniform vec2  u_lightDir[MAX_LIGHTS];
uniform float u_lightCone[MAX_LIGHTS];
uniform float u_hasOccluder;
uniform vec2  u_worldToUV;
uniform sampler2D u_occluder;
in vec2 v_world;
out vec4 fragColor;

void main() {{
    vec2 fragUV = v_world * u_worldToUV;
    // Sky exposure dims enclosed tiles (caves) even in daylight.
    float sky = 1.0;
    if (u_hasOccluder > 0.5) sky = texture(u_occluder, fragUV).g;
    vec3 light = vec3(u_ambient * sky);

    for (int i = 0; i < MAX_LIGHTS; i++) {{
        if (i >= u_lightCount) break;
        vec2 lp = u_lightPos[i];
        float d = distance(v_world, lp);
        float r = u_lightRadius[i];
        if (d >= r) continue;

        // Cone / FOV restriction (u_lightCone = cos(half-angle); -1 = full circle).
        float cone = 1.0;
        if (u_lightCone[i] > -0.999) {{
            vec2 nd = (v_world - lp) / max(d, 1e-4);
            float cs = dot(nd, u_lightDir[i]);
            cone = smoothstep(u_lightCone[i], u_lightCone[i] + 0.15, cs);
            if (cone <= 0.0) continue;
        }}

        // Shadow test: march from the fragment toward the light,
        // sampling the occluder. If a wall is hit, the light is
        // blocked for this pixel.
        float shadow = 0.0;
        if (u_hasOccluder > 0.5) {{
            vec2 luv = lp * u_worldToUV;
            vec2 stepv = (luv - fragUV) / float(SHADOW_STEPS);
            vec2 p = fragUV;
            for (int s = 1; s < SHADOW_STEPS; s++) {{
                p += stepv;
                if (texture(u_occluder, p).r > 0.5) {{ shadow = 1.0; break; }}
            }}
        }}
        if (shadow > 0.5) continue;

        float a = clamp(1.0 - d / r, 0.0, 1.0);
        light += u_lightColor[i] * (a * a) * cone;
    }}
    fragColor = vec4(min(light, vec3(1.0)), 1.0);
}}";
}
