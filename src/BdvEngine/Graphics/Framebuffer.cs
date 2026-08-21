using System;
using Silk.NET.OpenGL;

namespace BdvEngine;

/// <summary>
/// Minimal off-screen render target — one FBO + one colour attachment
/// texture at a given (Width, Height). Post-effects bind one of these
/// as the current draw target, render into it, then feed the texture
/// into a subsequent pass.
///
/// The colour attachment is <c>RGBA8</c> and the sampler is
/// <c>Linear/ClampToEdge</c> — right for the additive bloom pipeline
/// (bilinear smoothing on the up-sample, no wraparound seams). For
/// HDR later, swap the internal format to <c>RGBA16F</c> without any
/// caller change.
///
/// Not a full G-buffer / MRT abstraction — just enough for the bloom
/// path (one target, one texture). Add a depth attachment / MRT
/// support when the first case that needs it lands.
/// </summary>
public sealed class Framebuffer : IDisposable
{
    public uint Fbo     { get; private set; }
    public uint ColorTex { get; private set; }
    public int  Width    { get; private set; }
    public int  Height   { get; private set; }
    private readonly InternalFormat _colorFormat;

    /// <param name="colorFormat">Colour-attachment internal format.
    /// Defaults to <see cref="InternalFormat.Rgba8"/>; use
    /// <see cref="InternalFormat.Rgba16f"/> for HDR later.</param>
    public Framebuffer(int width, int height,
                       InternalFormat colorFormat = InternalFormat.Rgba8)
    {
        _colorFormat = colorFormat;
        Width  = width;
        Height = height;
        var gl = Gfx.Gl;

        Fbo      = gl.GenFramebuffer();
        ColorTex = gl.GenTexture();
        Allocate(gl, width, height);
    }

    /// <summary>Reallocate the colour attachment for a new size.
    /// Cheap enough to call on every viewport resize; the GPU
    /// discards old storage.</summary>
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height) return;
        Width  = width;
        Height = height;
        Allocate(Gfx.Gl, width, height);
    }

    private unsafe void Allocate(GL gl, int w, int h)
    {
        PixelFormat pxFmt = _colorFormat == InternalFormat.Rgba16f ? PixelFormat.Rgba : PixelFormat.Rgba;
        PixelType   pxTy  = _colorFormat == InternalFormat.Rgba16f ? PixelType.HalfFloat : PixelType.UnsignedByte;

        gl.BindTexture(TextureTarget.Texture2D, ColorTex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, _colorFormat,
            (uint)w, (uint)h, 0, pxFmt, pxTy, (void*)0);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,     (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,     (int)TextureWrapMode.ClampToEdge);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, ColorTex, 0);

        // Sanity check — surface the error at construction time
        // instead of during a mysterious black frame later.
        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"Framebuffer incomplete: {status}");

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>Bind this framebuffer + set the viewport to its
    /// size. Callers usually pair with <see cref="Unbind"/> or a
    /// direct <c>BindFramebuffer(0)</c>.</summary>
    public void Bind()
    {
        var gl = Gfx.Gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>Restore the default framebuffer + a viewport of the
    /// caller's choice (usually the actual window size).</summary>
    public static void Unbind(int viewportW, int viewportH)
    {
        var gl = Gfx.Gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)viewportW, (uint)viewportH);
    }

    /// <summary>Clear the colour attachment to the given RGBA.</summary>
    public void Clear(float r, float g, float b, float a)
    {
        var gl = Gfx.Gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        gl.Viewport(0, 0, (uint)Width, (uint)Height);
        gl.ClearColor(r, g, b, a);
        gl.Clear((uint)ClearBufferMask.ColorBufferBit);
    }

    public void Dispose()
    {
        var gl = Gfx.Gl;
        if (Fbo != 0)      { gl.DeleteFramebuffer(Fbo);      Fbo = 0; }
        if (ColorTex != 0) { gl.DeleteTexture(ColorTex);     ColorTex = 0; }
    }
}
