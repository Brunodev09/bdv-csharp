using StbTrueTypeSharp;

namespace BdvEngine;

/// <summary>
/// A baked TTF/OTF font. Loads the font file once, rasterizes ASCII 32–126 into a
/// single grayscale atlas, uploads it as RGBA (white tint, alpha = coverage), and
/// caches per-glyph metrics for the renderer.
/// </summary>
public sealed class Font
{
    public const int FIRST_CHAR = 32;
    public const int CHAR_COUNT = 96;

    public string Name { get; }
    public int PixelHeight { get; }
    public Material Material { get; }
    public int AtlasWidth { get; }
    public int AtlasHeight { get; }
    /// <summary>Distance from one baseline to the next in pixels at this size.</summary>
    public float LineAdvance { get; }
    /// <summary>Pixels above the baseline a typical glyph occupies (used to position the cursor).</summary>
    public float Ascent { get; }

    private readonly Glyph[] _glyphs = new Glyph[CHAR_COUNT];

    private struct Glyph
    {
        public ushort X0, Y0, X1, Y1;
        public float Xoff, Yoff, Xadvance;
        public bool Present;
    }

    public unsafe Font(string name, string ttfPath, int pixelHeight, int atlasW = 1024, int atlasH = 1024)
    {
        if (!File.Exists(ttfPath))
            throw new FileNotFoundException($"Font file not found: {ttfPath}");

        Name = name;
        PixelHeight = pixelHeight;
        AtlasWidth = atlasW;
        AtlasHeight = atlasH;

        byte[] ttf = File.ReadAllBytes(ttfPath);
        byte[] gray = new byte[atlasW * atlasH];
        var baked = new StbTrueType.stbtt_bakedchar[CHAR_COUNT];

        fixed (byte* fontPtr = ttf)
        fixed (byte* atlasPtr = gray)
        fixed (StbTrueType.stbtt_bakedchar* bakedPtr = baked)
        {
            int rows = StbTrueType.stbtt_BakeFontBitmap(
                fontPtr, 0, pixelHeight, atlasPtr, atlasW, atlasH,
                FIRST_CHAR, CHAR_COUNT, bakedPtr);
            if (rows <= 0)
                throw new InvalidOperationException(
                    $"Font atlas {atlasW}×{atlasH} too small to bake {CHAR_COUNT} glyphs at {pixelHeight}px");

            // Pull line metrics from the font directly so multi-line layout is correct.
            var info = new StbTrueType.stbtt_fontinfo();
            StbTrueType.stbtt_InitFont(info, fontPtr, 0);
            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, pixelHeight);
            int asc, desc, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &asc, &desc, &lineGap);
            Ascent = asc * scale;
            LineAdvance = (asc - desc + lineGap) * scale;
        }

        for (int i = 0; i < CHAR_COUNT; i++)
        {
            ref var b = ref baked[i];
            _glyphs[i] = new Glyph
            {
                X0 = b.x0, Y0 = b.y0, X1 = b.x1, Y1 = b.y1,
                Xoff = b.xoff, Yoff = b.yoff, Xadvance = b.xadvance,
                Present = b.x1 > b.x0 && b.y1 > b.y0,
            };
        }

        // Expand grayscale → RGBA (white text, alpha = coverage). Cheap one-shot.
        byte[] rgba = new byte[atlasW * atlasH * 4];
        for (int i = 0; i < gray.Length; i++)
        {
            int o = i * 4;
            rgba[o] = 255; rgba[o + 1] = 255; rgba[o + 2] = 255; rgba[o + 3] = gray[i];
        }

        string texName = "__font_atlas:" + name;
        var tex = Texture.CreateBlank(texName, atlasW, atlasH);
        tex.UploadRgba(atlasW, atlasH, rgba);
        TextureManager.Register(texName, tex);
        Material = new Material("__font_mat:" + name, texName, Color.White);
        MaterialManager.Register(Material);
    }

    /// <summary>Lay out one glyph and advance the cursor. Returns false for unsupported chars.</summary>
    public bool TryGetQuad(char c, ref float xCursor, float yBaseline,
        out float x0, out float y0, out float x1, out float y1,
        out float u0, out float v0, out float u1, out float v1)
    {
        int i = c - FIRST_CHAR;
        if ((uint)i >= CHAR_COUNT)
        {
            x0 = y0 = x1 = y1 = u0 = v0 = u1 = v1 = 0;
            return false;
        }
        var g = _glyphs[i];
        x0 = xCursor + g.Xoff;
        y0 = yBaseline + g.Yoff;
        x1 = x0 + (g.X1 - g.X0);
        y1 = y0 + (g.Y1 - g.Y0);
        u0 = g.X0 / (float)AtlasWidth;
        v0 = g.Y0 / (float)AtlasHeight;
        u1 = g.X1 / (float)AtlasWidth;
        v1 = g.Y1 / (float)AtlasHeight;
        xCursor += g.Xadvance;
        return g.Present || g.Xadvance > 0;
    }

    /// <summary>Width of a string in pixels at this font's size, no animation.</summary>
    public float Measure(string text)
    {
        float x = 0;
        foreach (char c in text)
        {
            int i = c - FIRST_CHAR;
            if ((uint)i < CHAR_COUNT) x += _glyphs[i].Xadvance;
        }
        return x;
    }

    /// <summary>
    /// Try a list of TTF paths and load the first one that exists. Use for examples
    /// that want a font without shipping one — falls back to a system font on macOS.
    /// </summary>
    public static Font LoadDefault(string name = "default", int pixelHeight = 64)
    {
        string[] paths =
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "font.ttf"),
            "/System/Library/Fonts/Supplemental/Andale Mono.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/Library/Fonts/Arial Unicode.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "C:/Windows/Fonts/consola.ttf",
        };
        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            var f = new Font(name, p, pixelHeight);
            FontManager.Register(f);
            return f;
        }
        throw new FileNotFoundException(
            "Font.LoadDefault: no font file found. Drop a TTF at assets/font.ttf.");
    }
}

public static class FontManager
{
    private static readonly Dictionary<string, Font> _fonts = new();

    public static void Register(Font font) => _fonts[font.Name] = font;

    public static Font Get(string name) =>
        _fonts.TryGetValue(name, out var f)
            ? f
            : throw new InvalidOperationException($"FontManager: '{name}' not registered.");

    public static bool TryGet(string name, out Font font) => _fonts.TryGetValue(name, out font!);
}
