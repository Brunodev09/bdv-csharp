using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

/// <summary>Per-world shadow settings, on <see cref="WorldEnvironment"/>. Tuning these is the
/// normal way to fix shadow problems, so they're plain public fields the inspector picks up.</summary>
public sealed class ShadowSettings
{
    /// <summary>Cast shadows from the sun. Off costs nothing — no depth pass, no extra samplers.</summary>
    public bool Enabled = true;

    /// <summary>Shadow map edge length. 2048 is a good default; 4096 buys sharper edges over a
    /// large <see cref="Distance"/> at 4x the memory.</summary>
    public int Resolution = 2048;

    /// <summary>Half-extent, in world units, of the area around the camera's focus that receives
    /// shadows. This is the resolution/coverage dial: 40 over a 2048 map is ~2.5cm per texel;
    /// stretching it to 400 for a whole island makes each texel 20cm and edges get chunky.</summary>
    public float Distance = 45f;

    /// <summary>Depth offset, in light-space units, applied when comparing — the fix for shadow
    /// acne (a surface shadowing itself in stripes). Too much instead causes peter-panning, where
    /// a shadow detaches from the object's feet.</summary>
    public float Bias = 0.0016f;

    /// <summary>Softening radius in shadow-map texels (PCF taps span this). 1 = a 3x3 kernel.</summary>
    public float SoftnessTexels = 1.2f;

    /// <summary>How dark a fully shadowed fragment gets. 0 = black, 1 = no shadow at all. Ambient
    /// still applies underneath, so 0 does not mean invisible.</summary>
    public float Strength = 0.75f;
}

/// <summary>
/// A single directional (sun) shadow map: one depth-only render target, plus the light-space
/// matrix the main pass uses to look up whether a fragment is occluded.
///
/// <para><b>Scope.</b> The sun only. Point-light shadows need cube maps (or six passes) and a much
/// bigger budget; a sun shadow is the one that makes a scene read as grounded, which is the whole
/// reason to start here.</para>
///
/// <para><b>Coverage.</b> An orthographic box centred on the camera's focus, sized by
/// <see cref="ShadowSettings.Distance"/>. The centre is snapped to the shadow map's texel grid —
/// without that, moving the camera makes shadow edges crawl and shimmer, which reads as a bug even
/// though every individual frame is correct.</para>
/// </summary>
public sealed class ShadowMap : IDisposable
{
    /// <summary>Texture unit the shadow map binds to in the lit/PBR shaders. 0 is the diffuse map,
    /// 1 is reserved for normal maps.</summary>
    public const int ShadowTextureUnit = 2;

    public uint DepthTexture { get; private set; }
    public int Resolution { get; private set; }

    /// <summary>World → light clip space, for the main pass's lookup.</summary>
    public Matrix4x4 LightViewProj { get; private set; } = Matrix4x4.Identity;

    private readonly GL _gl = Gfx.Gl;
    private uint _fbo;

    public ShadowMap(int resolution)
    {
        Resolution = resolution;
        _fbo = _gl.GenFramebuffer();
        DepthTexture = _gl.GenTexture();
        Allocate(resolution);
    }

    private unsafe void Allocate(int size)
    {
        Resolution = size;
        _gl.BindTexture(TextureTarget.Texture2D, DepthTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
                       (uint)size, (uint)size, 0, PixelFormat.DepthComponent, PixelType.Float, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        // Clamp to a border of depth 1.0 ("nothing occludes here"), so geometry outside the shadow
        // box is lit rather than picking up the edge texel and smearing a shadow across the world.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        Span<float> border = stackalloc float[] { 1f, 1f, 1f, 1f };
        fixed (float* b = border)
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, b);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                                 TextureTarget.Texture2D, DepthTexture, 0);
        // Depth-only: with no colour attachment the draw/read buffers must be explicitly None or
        // the FBO is incomplete.
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.Error.WriteLine($"[shadow] framebuffer incomplete: {status}");

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Resize(int size)
    {
        if (size == Resolution || size <= 0) return;
        Allocate(size);
    }

    /// <summary>Fit the light frustum around <paramref name="focus"/> and make this the draw target.
    /// Leaves the viewport at the shadow map's size; the caller restores it.</summary>
    public void BeginPass(Vector3 focus, Vector3 sunDirection, float distance)
    {
        LightViewProj = ComputeLightViewProj(focus, sunDirection, distance, Resolution);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)Resolution, (uint)Resolution);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(true);

        // Front-face culling during the depth pass pushes acne to surfaces the camera can't see.
        // It's the cheapest half of the acne fix; the depth bias is the other half.
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Front);
    }

    /// <summary>Restore the default framebuffer and its viewport.
    ///
    /// <para>The viewport is restored from <see cref="Gfx"/>'s FRAMEBUFFER size, not the window
    /// size. On a retina display those differ by the DPI scale, and restoring the window size
    /// renders the whole frame into one corner of the buffer.</para></summary>
    public void EndPass()
    {
        _gl.CullFace(TriangleFace.Back);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)Gfx.FramebufferWidth, (uint)Gfx.FramebufferHeight);
    }

    /// <summary>Bind the depth texture for sampling in the main pass. Leaves unit 0 active so the
    /// per-material diffuse binds land where the shaders expect them.</summary>
    public void BindForReading()
    {
        _gl.ActiveTexture(Silk.NET.OpenGL.TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, DepthTexture);
        _gl.ActiveTexture(Silk.NET.OpenGL.TextureUnit.Texture0);
    }

    /// <summary>
    /// Orthographic light frustum centred on <paramref name="focus"/>, looking along the sun.
    ///
    /// <para>The centre is quantised to whole shadow-map texels. Without that snap, sub-texel
    /// camera motion reshuffles which texel each surface lands in and the shadow edges visibly
    /// crawl — the classic "shimmering shadows" artefact.</para>
    /// </summary>
    public static Matrix4x4 ComputeLightViewProj(Vector3 focus, Vector3 sunDirection, float distance, int resolution)
    {
        var dir = sunDirection.LengthSquared() < 1e-8f ? new Vector3(0, -1, 0) : Vector3.Normalize(sunDirection);
        // A straight-down sun is parallel to the default up vector, which makes LookAt degenerate.
        var up = MathF.Abs(Vector3.Dot(dir, Vector3.UnitY)) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;

        float extent = MathF.Max(distance, 1f);
        float texelsPerUnit = resolution / (extent * 2f);

        // Snap along the light's own basis, which is the space the texel grid lives in.
        var right = Vector3.Normalize(Vector3.Cross(up, dir));
        var trueUp = Vector3.Cross(dir, right);
        float sx = MathF.Floor(Vector3.Dot(focus, right) * texelsPerUnit) / texelsPerUnit;
        float sy = MathF.Floor(Vector3.Dot(focus, trueUp) * texelsPerUnit) / texelsPerUnit;
        float sz = Vector3.Dot(focus, dir);
        var snapped = right * sx + trueUp * sy + dir * sz;

        var eye = snapped - dir * extent * 2f;
        var view = Matrix4x4.CreateLookAt(eye, snapped, up);
        var proj = Matrix4x4.CreateOrthographic(extent * 2f, extent * 2f, 0.05f, extent * 4f);
        return view * proj;
    }

    public void Dispose()
    {
        if (_fbo != 0) _gl.DeleteFramebuffer(_fbo);
        if (DepthTexture != 0) _gl.DeleteTexture(DepthTexture);
        _fbo = 0;
        DepthTexture = 0;
    }
}
