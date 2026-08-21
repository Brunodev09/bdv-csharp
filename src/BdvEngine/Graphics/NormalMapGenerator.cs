namespace BdvEngine;

/// <summary>
/// Generates a tangent-space normal map from an existing pixel-art sprite
/// so the Candide-style <see cref="NormalLitSpriteShader"/> has something
/// to light against WITHOUT hand-authoring a map per sprite.
///
/// <para>The heuristic: treat the sprite's per-texel brightness (or its
/// alpha silhouette) as a <b>height field</b>, run a Sobel gradient over
/// it, and encode the resulting surface direction as an RGB normal. It's
/// an approximation — hand-painted maps (the real Blender→Aseprite Candide
/// workflow) look better — but it gives every sprite instant, believable
/// directional shading to iterate from.</para>
///
/// <para>Fully transparent texels encode a flat, camera-facing normal
/// (0.5, 0.5, 1.0) so sprite silhouette edges don't glow. The result is
/// registered in <see cref="TextureManager"/> under
/// <paramref name="normalTexName"/> and returned; attach it to a material
/// via <see cref="Material.SetNormalTexture"/>.</para>
/// </summary>
public static class NormalMapGenerator
{
    /// <summary>
    /// Build a normal map from a loaded image asset.
    /// </summary>
    /// <param name="albedoAssetName">Name of the source sprite asset
    ///   (the same name you'd pass to a <see cref="Material"/>).</param>
    /// <param name="normalTexName">Registry name for the generated normal
    ///   texture. Pass something stable+unique, e.g. <c>"foo__n"</c>.</param>
    /// <param name="strength">Bump strength. Higher = deeper apparent
    ///   relief (steeper normals). 1–4 is a sane range.</param>
    /// <param name="fromAlpha">When true, derive height from the alpha
    ///   channel (silhouette relief — good for flat-coloured sprites);
    ///   otherwise from luminance (interior detail).</param>
    /// <param name="invertG">Flip the green channel. Whether you need this
    ///   depends on your texture's row orientation vs. the shader's UV
    ///   convention — if lighting looks vertically inverted, toggle it.</param>
    /// <returns>The generated + registered normal <see cref="Texture"/>, or
    ///   null if the source asset isn't loaded yet.</returns>
    public static Texture? FromAsset(string albedoAssetName, string normalTexName,
        float strength = 2f, bool fromAlpha = false, bool invertG = false)
    {
        var img = AssetManager.Get<ImageAsset>(albedoAssetName);
        if (img == null || img.Width <= 0 || img.Height <= 0) return null;

        int w = img.Width, h = img.Height;
        var src = img.Pixels;                 // RGBA, row-major
        var height = new float[w * h];
        var alpha  = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int o = i * 4;
            float a = src[o + 3] / 255f;
            alpha[i] = a;
            if (fromAlpha)
            {
                height[i] = a;
            }
            else
            {
                // Rec. 601 luma, weighted by coverage so transparent
                // texels read as zero height (flat) rather than black.
                float lum = (0.299f * src[o] + 0.587f * src[o + 1] + 0.114f * src[o + 2]) / 255f;
                height[i] = lum * a;
            }
        }

        var outPixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            // Transparent texel → flat normal, so the silhouette edge
            // stays camera-facing and doesn't catch a stray highlight.
            if (alpha[i] < 0.5f)
            {
                WriteFlat(outPixels, i);
                continue;
            }

            // Sobel gradient of the height field (clamped at edges).
            float hl = height[i - (x > 0 ? 1 : 0)];
            float hr = height[i + (x < w - 1 ? 1 : 0)];
            float hu = height[i - (y > 0 ? w : 0)];
            float hd = height[i + (y < h - 1 ? w : 0)];
            float dx = (hr - hl) * strength;
            float dy = (hd - hu) * strength;
            if (invertG) dy = -dy;

            // Surface normal from the gradient: (-dx, -dy, 1) normalized.
            float nx = -dx, ny = -dy, nz = 1f;
            float inv = 1f / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            nx *= inv; ny *= inv; nz *= inv;

            int o = i * 4;
            outPixels[o + 0] = Encode(nx);
            outPixels[o + 1] = Encode(ny);
            outPixels[o + 2] = Encode(nz);
            outPixels[o + 3] = 255;
        }

        var tex = Texture.CreateBlank(normalTexName, w, h);
        tex.UploadRgba(w, h, outPixels);
        TextureManager.Register(normalTexName, tex);
        return tex;
    }

    /// <summary>A shared 2×2 "flat" normal map — every texel encodes
    /// (0,0,1), i.e. facing the camera. Bound as the unit-1 fallback for a
    /// lighting-enabled material that hasn't got a real normal map yet, so
    /// the shader always samples a valid normal (and renders as if unlit /
    /// flat) instead of accidentally reading the diffuse as a normal.</summary>
    public static Texture FlatNormal()
    {
        const string name = "__flat_normal__";
        if (_flat != null) return _flat;
        var px = new byte[2 * 2 * 4];
        for (int i = 0; i < 4; i++) WriteFlat(px, i);
        var tex = Texture.CreateBlank(name, 2, 2);
        tex.UploadRgba(2, 2, px);
        TextureManager.Register(name, tex);
        _flat = tex;
        return tex;
    }
    private static Texture? _flat;

    private static void WriteFlat(byte[] px, int i)
    {
        int o = i * 4;
        px[o + 0] = 128;   // nx = 0
        px[o + 1] = 128;   // ny = 0
        px[o + 2] = 255;   // nz = 1  (facing camera)
        px[o + 3] = 255;
    }

    // Map a signed component in [-1, 1] to a byte in [0, 255].
    private static byte Encode(float v) => (byte)MathF.Round((v * 0.5f + 0.5f) * 255f);
}
