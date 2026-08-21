using StbImageSharp;

namespace BdvEngine;

/// <summary>
/// One source sprite-sheet to bake into the atlas. <c>GridCols × GridRows</c>
/// is the source PNG's frame grid; per-frame pixel size is derived from the
/// loaded image's natural dimensions.
/// </summary>
public sealed class AtlasSource
{
    public string Id { get; init; } = "";
    /// <summary>Absolute or asset-relative path to the source PNG.</summary>
    public string Path { get; init; } = "";
    public int GridCols { get; init; } = 1;
    public int GridRows { get; init; } = 1;
}

/// <summary>
/// UV bounds of one source sheet inside the atlas + everything the draw
/// path needs to address a single frame inside it. Pre-computed at build
/// time so per-frame draws are pure add + mul with no atlas-dimension
/// dependency.
/// </summary>
public sealed class AtlasRegion
{
    /// <summary>Top-left of this sheet in atlas-pixel coords.</summary>
    public int AtlasX;
    public int AtlasY;
    /// <summary>Sheet's total pixel size inside the atlas.</summary>
    public int SheetW;
    public int SheetH;
    /// <summary>Per-frame pixel size (SheetW / GridCols × SheetH / GridRows).</summary>
    public int FrameW;
    public int FrameH;
    public int GridCols;
    public int GridRows;
    /// <summary>Atlas-relative UVs of the whole sheet.</summary>
    public float U0, V0, U1, V1;
    /// <summary>Atlas-relative UV span per frame — drawAtlasFrame resolves
    /// any frame's UV in two adds + two muls, no atlas dims needed.</summary>
    public float FrameUSpan;
    public float FrameVSpan;
}

/// <summary>
/// Result of <see cref="Atlas.Build"/>. Consumer stores <see cref="Regions"/>
/// and creates materials via <see cref="Atlas.MakeMaterial"/> (one per
/// shader variant).
/// </summary>
public sealed class AtlasResult
{
    public Texture Texture = null!;
    public string TextureName = "";
    public int Width;
    public int Height;
    public Dictionary<string, AtlasRegion> Regions = new();
}

/// <summary>
/// Runtime texture atlas. Takes a list of source PNGs, shelf-packs them
/// into one big texture at load time, and exposes per-source UV regions
/// for the consumer to sample. The whole point: collapse N materials
/// (one per PNG) down to 1 material (one for the whole atlas), so every
/// sprite drawn against the atlas batches together regardless of which
/// source PNG it came from.
/// </summary>
public static class Atlas
{
    /// <summary>Transparent border between sheets — absorbs the 1-texel
    /// neighbour samples performed by post-processing shaders (outline
    /// dilation, etc.) so effects don't leak between adjacent regions.</summary>
    private const int Padding = 2;

    /// <summary>
    /// Load every source PNG, shelf-pack them into one image, upload the
    /// composited pixels as a single procedural Texture, and return a
    /// <c>{texture, regions}</c> record so callers can sample per-source UVs.
    ///
    /// Shelf packing: sort sources by descending height, lay out
    /// left-to-right on rows ("shelves"). Each shelf is as tall as its
    /// tallest entry. Wraps to a new shelf when the current row runs out.
    /// Good enough for the few dozen sheets a typical game ships.
    /// </summary>
    public static AtlasResult Build(string name, IList<AtlasSource> sources, int maxWidth = 2048)
    {
        // ── Load every source PNG synchronously via stb_image_sharp. We
        //    don't go through AssetManager because Atlas builds happen at
        //    init time and need every pixel buffer in hand at once.
        var entries = new Entry[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            using var fs = File.OpenRead(s.Path);
            var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
            entries[i] = new Entry { Src = s, Pixels = img.Data, W = img.Width, H = img.Height };
        }

        // ── Shelf-pack tallest-first. ─────────────────────────────────
        Array.Sort(entries, (a, b) => b.H.CompareTo(a.H));
        int cursorX = Padding, cursorY = Padding;
        int shelfH = 0;
        int usedW = 0;
        var placed = new List<Placed>(entries.Length);
        foreach (var e in entries)
        {
            int cellW = e.W + Padding;
            int cellH = e.H + Padding;
            if (cursorX + cellW > maxWidth)
            {
                cursorX = Padding;
                cursorY += shelfH + Padding;
                shelfH = 0;
            }
            placed.Add(new Placed { E = e, X = cursorX, Y = cursorY });
            cursorX += cellW;
            if (cellH > shelfH) shelfH = cellH;
            if (cursorX > usedW) usedW = cursorX;
        }
        int totalH = cursorY + shelfH + Padding;
        int atlasW = CeilPow2(Math.Max(16, usedW));
        int atlasH = CeilPow2(Math.Max(16, totalH));

        // ── Composite all source pixels into one big RGBA buffer. ─────
        byte[] atlasPixels = new byte[atlasW * atlasH * 4];   // zeroed = transparent
        foreach (var p in placed) Blit(atlasPixels, atlasW, p.X, p.Y, p.E.Pixels, p.E.W, p.E.H);

        // ── Upload as a procedural texture + register with the manager
        //    so any Material can reference it by name.
        var texture = Texture.CreateBlank(name, atlasW, atlasH);
        texture.UploadRgba(atlasW, atlasH, atlasPixels);
        TextureManager.Register(name, texture);

        var regions = new Dictionary<string, AtlasRegion>(placed.Count);
        foreach (var p in placed)
        {
            var e = p.E;
            int sheetW = e.W, sheetH = e.H;
            int frameW = sheetW / e.Src.GridCols;
            int frameH = sheetH / e.Src.GridRows;
            float u0 = (float)p.X / atlasW;
            float v0 = (float)p.Y / atlasH;
            float u1 = (float)(p.X + sheetW) / atlasW;
            float v1 = (float)(p.Y + sheetH) / atlasH;
            regions[e.Src.Id] = new AtlasRegion
            {
                AtlasX = p.X, AtlasY = p.Y, SheetW = sheetW, SheetH = sheetH,
                FrameW = frameW, FrameH = frameH,
                GridCols = e.Src.GridCols, GridRows = e.Src.GridRows,
                U0 = u0, V0 = v0, U1 = u1, V1 = v1,
                FrameUSpan = (u1 - u0) / e.Src.GridCols,
                FrameVSpan = (v1 - v0) / e.Src.GridRows,
            };
        }

        return new AtlasResult
        {
            Texture = texture, TextureName = name,
            Width = atlasW, Height = atlasH, Regions = regions,
        };
    }

    /// <summary>Wrap an atlas in a Material, optionally with a custom
    /// shader. Hands back a registered Material ready to feed to
    /// <see cref="SpriteBatcher"/>.</summary>
    public static Material MakeMaterial(string name, AtlasResult atlas, Shader? shader = null)
    {
        var m = new Material(name, atlas.TextureName, Color.White, shader);
        MaterialManager.Register(m);
        return m;
    }

    // ── Internals ──────────────────────────────────────────────────────

    private sealed class Entry
    {
        public AtlasSource Src = null!;
        public byte[] Pixels = null!;
        public int W;
        public int H;
    }

    private sealed class Placed
    {
        public Entry E = null!;
        public int X;
        public int Y;
    }

    /// <summary>Row-major RGBA blit — copy <paramref name="src"/> (srcW × srcH)
    /// into <paramref name="dst"/> (dstW × ?) at (dx, dy). Same byte layout
    /// stb_image returns: top-left origin, 4 bytes per pixel RGBA.</summary>
    private static void Blit(byte[] dst, int dstW, int dx, int dy,
                             byte[] src, int srcW, int srcH)
    {
        int srcStride = srcW * 4;
        int dstStride = dstW * 4;
        for (int y = 0; y < srcH; y++)
        {
            int srcOff = y * srcStride;
            int dstOff = (dy + y) * dstStride + dx * 4;
            Buffer.BlockCopy(src, srcOff, dst, dstOff, srcStride);
        }
    }

    private static int CeilPow2(int n)
    {
        int p = 1;
        while (p < n) p *= 2;
        return p;
    }
}
