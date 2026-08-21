using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

// Flush order: Ground → Object → UIBack → UI → Overlay.
// Overlay is the always-on-top tier — flushed dead last so nothing in the
// regular UI can overpaint it. Use it for tooltips / transient hover hints
// that must stay readable regardless of which panels are on screen.
/// <summary>Hard back-to-front tiers for sprite submissions. Order is
/// fixed: every tier renders completely before the next starts, so
/// you can rely on layer alone to keep things from punching through
/// each other regardless of UI-tree depth or world position.
/// <list type="bullet">
/// <item><b>Ground</b> — terrain. Insertion-order within the tier.</item>
/// <item><b>Object</b> — world sprites (trees, units, buildings). Y-sorted via depth buffer.</item>
/// <item><b>WorldOverlay</b> — game features above the world but BELOW any UI: territory borders,
///     speech balloons, emotes, sleep ZZZs, hover outlines. This is the right tier whenever
///     "draws on top of the map but the player's HUD must still cover it" applies.</item>
/// <item><b>UIBack</b> — UI panel backgrounds + scissored interiors.</item>
/// <item><b>UI</b> — UI text + foreground widgets.</item>
/// <item><b>Overlay</b> — always-on-top UI: tooltips. Above every other tier.</item>
/// </list></summary>
public enum SpriteLayer { Ground, Object, WorldOverlay, UIBack, UI, Overlay }

/// <summary>
/// Layered sprite batcher.
///
/// Layers flush in order: Ground → Object → UI.
///   • Ground / UI: per-texture batch, insertion order. One draw per (shader × texture).
///   • Object: per-quad entries with sortY; sorted by Y on flush, run-length batched by
///     texture to produce stable depth ordering for trees/units/buildings.
///
/// All batches use indexed quads (4 verts + 6 indices per quad).
/// Vertex layout: x, y, z, u, v, r, g, b, a (9 floats / 36 bytes).
/// </summary>
public static class SpriteBatcher
{
    private const int FLOATS_PER_VERT = 9;
    private const int FLOATS_PER_QUAD = 4 * FLOATS_PER_VERT; // 36

    private sealed class Batch
    {
        public List<float> Verts = new();
        public List<uint>  Indices = new();
        public uint NextBase;
        public Texture Texture = null!;
        public Material? Material;
    }

    private static readonly Dictionary<string, Batch> _groundBatches = new();
    private static readonly List<Batch> _groundOrder = new();
    private static readonly Dictionary<string, Batch> _worldOverlayBatches = new();
    private static readonly List<Batch> _worldOverlayOrder = new();
    private static readonly Dictionary<string, Batch> _uiBackBatches = new();
    private static readonly List<Batch> _uiBackOrder = new();
    private static readonly Dictionary<string, Batch> _uiBatches = new();
    private static readonly List<Batch> _uiOrder = new();
    private static readonly Dictionary<string, Batch> _overlayBatches = new();
    private static readonly List<Batch> _overlayOrder = new();

    // ── Object-layer storage — parallel arrays, zero per-quad allocation ──
    // The Object layer used to wrap every quad in an `ObjectEntry` record
    // and CPU Y-sort the list each frame. Replaced with parallel typed-
    // array storage + GPU depth-buffer sorting (per-quad sortY packed into
    // the vertex z slot — see BatchSpriteShader.u_zScale). Bucket-by-key
    // flush emits one draw per (shader:texture[:material]) batch.
    private const int InitialObjCapacity = 1024;
    private static float[] _objVerts = new float[InitialObjCapacity * FLOATS_PER_QUAD];
    private static string[] _objKey = new string[InitialObjCapacity];
    private static Texture[] _objTexture = new Texture[InitialObjCapacity];
    private static Material?[] _objMaterial = new Material?[InitialObjCapacity];
    private static int _objCount = 0;
    private static int _objCapacity = InitialObjCapacity;

    // Persistent flush buffers — reused every frame so FlushObjectLayer
    // doesn't allocate a Dictionary + List<int> per bucket +
    // List<float>(_objCount*36) + List<uint>(_objCount*6) PER FRAME.
    // At ~5000 trees that's hundreds of KB/frame of GC pressure; the
    // resulting collections were the dominant cost at zoomed-out views
    // and visibly halved framerate. Cleared (not reallocated) each
    // flush; capacity grows naturally as the world scales.
    private static readonly Dictionary<string, List<int>> _objBuckets = new();
    private static readonly List<float> _objFlushVerts = new(InitialObjCapacity * FLOATS_PER_QUAD);
    private static readonly List<uint>  _objFlushIdx   = new(InitialObjCapacity * 6);

    /// <summary>World-Y → clip-space-Z scale for the Object layer. Default
    /// is a permissive `1/16384` so overlaps still resolve sensibly before
    /// the host calls <see cref="SetObjectZRange"/> with its actual world
    /// Y-span (typically <c>mapH * tileH</c>).</summary>
    private static float _objZScale = 1f / 16384f;
    // Per-frame depth OFFSET: the min sortY of this frame's Object quads. The
    // shader packs (sortY - bias) * scale into clip-Z, so an ABSOLUTE sortY of
    // any magnitude (a colony out at world row 100000) still lands in [-1,1]
    // instead of being depth-clipped. Recomputed each FlushObjectLayer.
    private static float _objZBias;
    // Set true by SetObjectDepthWindow for the current frame; FlushObjectLayer
    // then uses the app-supplied stable window instead of self-calibrating.
    private static bool _objDepthWindowSet;

    /// <summary>Supply a STABLE, viewport-relative Object-layer depth window for
    /// this frame: <paramref name="loSortY"/>..<paramref name="hiSortY"/> should
    /// bracket the sortY of everything on screen (camera top → bottom + a sprite-
    /// height margin). Call once per frame BEFORE the batcher flushes. This keeps
    /// depth precision tight AND frame-stable, so on-screen sprites don't z-fight
    /// when a far-off Object quad would otherwise stretch/jitter the range. Objects
    /// outside the window clip in Z — fine, they're off screen. Not calling this
    /// falls back to per-frame self-calibration over the whole submitted set.</summary>
    public static void SetObjectDepthWindow(float loSortY, float hiSortY)
    {
        _objZBias  = loSortY;
        _objZScale = hiSortY > loSortY ? 1f / (hiSortY - loSortY) : 0f;
        _objDepthWindowSet = true;
    }

    private static uint _vao;
    private static uint _vbo;
    private static uint _ebo;
    private static BatchSpriteShader? _batchShader;
    private static bool _initialized;
    private static Material? _solidMat;

    /// <summary>1×1 white material — useful for solid colored quads pushed into the
    /// regular batcher (so they share insertion-order with sprites/text).</summary>
    private static Material GetSolidMaterial()
    {
        if (_solidMat != null) return _solidMat;
        const string texName = "__sprite_solid__";
        var tex = Texture.CreateBlank(texName, 1, 1);
        Span<byte> white = stackalloc byte[] { 255, 255, 255, 255 };
        tex.UploadRgba(1, 1, white);
        TextureManager.Register(texName, tex);
        _solidMat = new Material("__sprite_solid_mat__", texName, Color.White);
        MaterialManager.Register(_solidMat);
        return _solidMat;
    }

    /// <summary>Push a solid-color quad through the batcher. Layer/sortY behave the
    /// same as DrawTexture. Use for UI fills that must respect SpriteBatcher draw
    /// order (i.e. panels that sit *behind* their child labels/images).</summary>
    public static void DrawSolid(float x, float y, float width, float height, Color color,
        SpriteLayer layer = SpriteLayer.UI, float sortY = 0f)
        => DrawTextureUV(GetSolidMaterial(), 0f, 0f, 1f, 1f, x, y, width, height, color, layer, sortY);

    /// <summary>Push an arbitrary solid-color convex quad (4 corners, in
    /// winding order a→b→c→d) through the batcher — for rotated/diagonal
    /// fills (e.g. border bands) that DrawSolid's axis-aligned rect can't do.
    /// Goes through the normal layer pipeline so it respects draw order
    /// (Ground sits under the GUI, unlike Draw.* which flushes last).</summary>
    public static void DrawQuad(
        float ax, float ay, float bx, float by,
        float cx, float cy, float dx, float dy,
        Color color, SpriteLayer layer = SpriteLayer.Ground, float sortY = 0f)
    {
        var mat = GetSolidMaterial();
        var tex = mat.DiffuseTexture;
        if (tex == null) return;
        string key = mat.BatchKey;
        float r = color.RFloat, g = color.GFloat, b = color.BFloat, a = color.AFloat;
        Span<float> quad = stackalloc float[FLOATS_PER_QUAD];
        // 4 verts (x,y,z,u,v,r,g,b,a) — solid mat is 1×1 white so UV is moot.
        quad[0]  = ax; quad[1]  = ay; quad[2]  = 0; quad[3]  = 0; quad[4]  = 0; quad[5]  = r; quad[6]  = g; quad[7]  = b; quad[8]  = a;
        quad[9]  = bx; quad[10] = by; quad[11] = 0; quad[12] = 0; quad[13] = 0; quad[14] = r; quad[15] = g; quad[16] = b; quad[17] = a;
        quad[18] = cx; quad[19] = cy; quad[20] = 0; quad[21] = 0; quad[22] = 0; quad[23] = r; quad[24] = g; quad[25] = b; quad[26] = a;
        quad[27] = dx; quad[28] = dy; quad[29] = 0; quad[30] = 0; quad[31] = 0; quad[32] = r; quad[33] = g; quad[34] = b; quad[35] = a;
        EmitQuad(quad, key, tex, null, layer, sortY);
    }

    /// <summary>Pointy-top hex mask texture — opaque white inside the hex silhouette,
    /// transparent outside. Lets us draw solid-color hex fills through the batcher
    /// instead of via Draw.Triangle (which flushes after SpriteBatcher and would
    /// otherwise sit on top of UI panels).</summary>
    private static Material? _hexMaskMat;
    private static Material GetHexMaskMaterial()
    {
        if (_hexMaskMat != null) return _hexMaskMat;
        const int N = 256;
        var pixels = new byte[N * N * 4];
        float r = N / 2f;
        float halfWidth = MathF.Sqrt(3f) / 2f * r;     // flat-to-flat ≈ 0.866 * r
        float invSqrt3  = 1f / MathF.Sqrt(3f);         // ≈ 0.577 — diagonal slope's reciprocal
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                // Pointy-top hex inside test:
                //   |y| ≤ r                         (top/bottom bounding — points)
                //   |x| ≤ √3/2 · r                  (flat left/right edges)
                //   |y| + |x|/√3 ≤ r                (the four diagonal edges, slope ±1/√3)
                bool inside = MathF.Abs(dy) <= r
                           && MathF.Abs(dx) <= halfWidth
                           && MathF.Abs(dy) + MathF.Abs(dx) * invSqrt3 <= r;
                int o = (y * N + x) * 4;
                pixels[o] = pixels[o + 1] = pixels[o + 2] = 255;
                pixels[o + 3] = (byte)(inside ? 255 : 0);
            }
        }
        const string texName = "__sprite_hex_mask__";
        var tex = Texture.CreateBlank(texName, N, N);
        tex.UploadRgba(N, N, pixels);
        TextureManager.Register(texName, tex);
        _hexMaskMat = new Material("__sprite_hex_mat__", texName, Color.White);
        MaterialManager.Register(_hexMaskMat);
        return _hexMaskMat;
    }

    /// <summary>Push a solid-color pointy-top hex centered at (cx, cy) with width/height
    /// w × h. Goes through the batcher (so layer ordering applies) — use Ground for
    /// game-world overlays so UI panels still draw on top.</summary>
    public static void DrawHexPointyTop(float cx, float cy, float w, float h, Color color,
        SpriteLayer layer = SpriteLayer.Ground, float sortY = 0f)
        => DrawTextureUV(GetHexMaskMaterial(), 0f, 0f, 1f, 1f,
            cx - w * 0.5f, cy - h * 0.5f, w, h, color, layer, sortY);

    private static unsafe void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        var gl = Gfx.Gl;
        _batchShader = new BatchSpriteShader();
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        _ebo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        const uint stride = FLOATS_PER_VERT * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(5 * sizeof(float)));
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        gl.BindVertexArray(0);
    }

    // The batch-bucket key now lives precomputed on Material.BatchKey
    // (see Material.RecomputeBatchKey). Default-shader materials merge
    // by `shader:texture` so the same atlas with different tints stays
    // in one batch (tint travels per-vertex in the colour stream).
    // Custom-shader materials split per-MATERIAL because their uniforms
    // (Material.SetUniform) are applied ONCE per flushed batch via
    // Material.ApplyUniforms — two materials sharing shader+texture but
    // wanting different uniforms MUST land in separate batches.

    /// <summary>
    /// Queue a sprite quad. Expects 6 input vertices in the layout produced by Sprite.Load
    /// (BL, TL, TR, TR-dup, BR, BL-dup) and emits indexed 4-vert geometry.
    /// </summary>
    public static void Push(IList<Vertex> vertices, Material material, Matrix4x4 worldMatrix,
        SpriteLayer layer = SpriteLayer.Ground, float sortY = 0f)
    {
        var tex = material.DiffuseTexture;
        if (tex == null || vertices.Count < 4) return;

        string key = material.BatchKey;

        // Transform 4 unique corners (input verts 0, 1, 2, 4 — see Sprite.Load).
        float m0 = worldMatrix.M11, m1 = worldMatrix.M12, m2 = worldMatrix.M13;
        float m4 = worldMatrix.M21, m5 = worldMatrix.M22, m6 = worldMatrix.M23;
        float m8 = worldMatrix.M31, m9 = worldMatrix.M32, m10 = worldMatrix.M33;
        float m12 = worldMatrix.M41, m13 = worldMatrix.M42, m14 = worldMatrix.M43;
        var c = material.Color;
        float r = c.RFloat, g = c.GFloat, b = c.BFloat, a = c.AFloat;

        Span<float> quad = stackalloc float[FLOATS_PER_QUAD];
        int[] cornerIdx = { 0, 1, 2, 4 };
        for (int i = 0; i < 4; i++)
        {
            var v = vertices[cornerIdx[i]];
            float wx = m0 * v.X + m4 * v.Y + m8  * v.Z + m12;
            float wy = m1 * v.X + m5 * v.Y + m9  * v.Z + m13;
            float wz = m2 * v.X + m6 * v.Y + m10 * v.Z + m14;
            int o = i * FLOATS_PER_VERT;
            quad[o + 0] = wx; quad[o + 1] = wy; quad[o + 2] = wz;
            quad[o + 3] = v.U; quad[o + 4] = v.V;
            quad[o + 5] = r;  quad[o + 6] = g;  quad[o + 7] = b; quad[o + 8] = a;
        }

        EmitQuad(quad, key, tex, material.HasCustomShader ? material : null, layer, sortY);
    }

    /// <summary>Queue an axis-aligned textured quad sampling a sub-rect of a spritesheet.</summary>
    public static void DrawTexture(Material material,
        int srcCol, int srcRow, int gridCols, int gridRows,
        float x, float y, float width, float height,
        Color? tint = null, SpriteLayer layer = SpriteLayer.Ground, float sortY = 0f)
    {
        float u0 = srcCol / (float)gridCols;
        float v0 = srcRow / (float)gridRows;
        float u1 = (srcCol + 1) / (float)gridCols;
        float v1 = (srcRow + 1) / (float)gridRows;
        DrawTextureUV(material, u0, v0, u1, v1, x, y, width, height, tint, layer, sortY);
    }

    /// <summary>
    /// Queue one frame of an <see cref="AtlasRegion"/>. High-level draw
    /// call — game code says "frame (col, row) of this region at (x, y)"
    /// and the engine resolves the atlas UVs internally from the region's
    /// pre-computed <c>FrameUSpan / FrameVSpan</c>. No game-side UV math.
    ///
    /// <para><c>material</c> must wrap the atlas this region belongs to
    /// (typically created via <see cref="Atlas.MakeMaterial"/>).</para>
    ///
    /// <para><paramref name="flipX"/> / <paramref name="flipY"/> mirror the
    /// frame horizontally / vertically by swapping the sampled UV edges —
    /// use <c>flipX</c> to face a side-view sprite the other way (e.g. a
    /// creature walking left vs right) without a second sheet. The quad
    /// geometry is unchanged; only the texture mapping is mirrored.</para>
    /// </summary>
    public static void DrawAtlasFrame(Material material, AtlasRegion region,
        int frameCol, int frameRow,
        float x, float y, float width, float height,
        Color? tint = null, SpriteLayer layer = SpriteLayer.Ground, float sortY = 0f,
        bool flipX = false, bool flipY = false)
    {
        float u0 = region.U0 + frameCol * region.FrameUSpan;
        float v0 = region.V0 + frameRow * region.FrameVSpan;
        float u1 = u0 + region.FrameUSpan;
        float v1 = v0 + region.FrameVSpan;
        if (flipX) { var t = u0; u0 = u1; u1 = t; }
        if (flipY) { var t = v0; v0 = v1; v1 = t; }
        DrawTextureUV(material, u0, v0, u1, v1, x, y, width, height, tint, layer, sortY);
    }

    /// <summary>Queue an axis-aligned textured quad sampling an explicit UV rectangle.</summary>
    public static void DrawTextureUV(Material material,
        float u0, float v0, float u1, float v1,
        float x, float y, float width, float height,
        Color? tint = null, SpriteLayer layer = SpriteLayer.Ground, float sortY = 0f)
    {
        var tex = material.DiffuseTexture;
        if (tex == null) return;

        string key = material.BatchKey;
        var c = tint ?? Color.White;
        float r = c.RFloat, g = c.GFloat, b = c.BFloat, a = c.AFloat;
        float x2 = x + width, y2 = y + height;

        Span<float> quad = stackalloc float[FLOATS_PER_QUAD];
        // BL
        quad[0]  = x;  quad[1]  = y;  quad[2]  = 0; quad[3]  = u0; quad[4]  = v0; quad[5]  = r; quad[6]  = g; quad[7]  = b; quad[8]  = a;
        // TL
        quad[9]  = x;  quad[10] = y2; quad[11] = 0; quad[12] = u0; quad[13] = v1; quad[14] = r; quad[15] = g; quad[16] = b; quad[17] = a;
        // TR
        quad[18] = x2; quad[19] = y2; quad[20] = 0; quad[21] = u1; quad[22] = v1; quad[23] = r; quad[24] = g; quad[25] = b; quad[26] = a;
        // BR
        quad[27] = x2; quad[28] = y;  quad[29] = 0; quad[30] = u1; quad[31] = v0; quad[32] = r; quad[33] = g; quad[34] = b; quad[35] = a;

        // Pass the material through (not null) so custom-shader uniforms
        // (RecolorShader's u_swapColor / OutlineShader's u_outlineCol /
        // etc.) actually apply. Was a bug — the bucket key correctly
        // routed each kingdom's recolor material into its own batch, but
        // EmitQuad got null for the mat ref so FlushObjectLayer +
        // BindShaderForObject saw mat==null and fell back to the DEFAULT
        // sprite shader. Recolor templates rendered as their raw red.
        EmitQuad(quad, key, tex, material, layer, sortY);
    }

    private static void EmitQuad(ReadOnlySpan<float> quad, string key, Texture tex, Material? mat,
        SpriteLayer layer, float sortY)
    {
        if (layer == SpriteLayer.Object)
        {
            if (_objCount >= _objCapacity) GrowObjStorage();
            int idx = _objCount++;
            int off = idx * FLOATS_PER_QUAD;
            // Copy the quad verts into the parallel float store, then
            // overwrite the z slot of each of the 4 verts with sortY so
            // the depth buffer can sort overlapping Object-layer quads.
            quad.CopyTo(_objVerts.AsSpan(off, FLOATS_PER_QUAD));
            _objVerts[off + 2]  = sortY;        // BL.z
            _objVerts[off + 11] = sortY;        // TL.z
            _objVerts[off + 20] = sortY;        // TR.z
            _objVerts[off + 29] = sortY;        // BR.z
            _objKey[idx] = key;
            _objTexture[idx] = tex;
            _objMaterial[idx] = mat;
            return;
        }

        Dictionary<string, Batch> dict;
        List<Batch> order;
        switch (layer)
        {
            case SpriteLayer.Overlay:      dict = _overlayBatches;      order = _overlayOrder;      break;
            case SpriteLayer.UI:           dict = _uiBatches;           order = _uiOrder;           break;
            case SpriteLayer.UIBack:       dict = _uiBackBatches;       order = _uiBackOrder;       break;
            case SpriteLayer.WorldOverlay: dict = _worldOverlayBatches; order = _worldOverlayOrder; break;
            default:                  dict = _groundBatches; order = _groundOrder; break;
        }

        if (!dict.TryGetValue(key, out var batch))
        {
            batch = new Batch { Texture = tex, Material = mat };
            dict[key] = batch;
            order.Add(batch);
        }
        AppendQuadToBatch(batch, quad);
    }

    private static void AppendQuadToBatch(Batch batch, ReadOnlySpan<float> quad)
    {
        // Bulk write via CollectionsMarshal — avoids 36 individual
        // List<float>.Add calls per quad (each with bounds check + count
        // bookkeeping). At 100k+ quads/frame the Add overhead dominated
        // the CPU. SetCount grows the backing array once; AsSpan slice
        // is a direct memory destination; quad.CopyTo is a single memmove.
        var v = batch.Verts;
        int oldVertCount = v.Count;
        System.Runtime.InteropServices.CollectionsMarshal.SetCount(v, oldVertCount + FLOATS_PER_QUAD);
        quad.CopyTo(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(v).Slice(oldVertCount, FLOATS_PER_QUAD));
        var ix = batch.Indices;
        int oldIxCount = ix.Count;
        System.Runtime.InteropServices.CollectionsMarshal.SetCount(ix, oldIxCount + 6);
        var ixSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ix).Slice(oldIxCount, 6);
        uint b = batch.NextBase;
        ixSpan[0] = b + 0; ixSpan[1] = b + 1; ixSpan[2] = b + 2;
        ixSpan[3] = b + 2; ixSpan[4] = b + 3; ixSpan[5] = b + 0;
        batch.NextBase = b + 4;
    }

    /// <summary>Submit all queued sprites for the frame.</summary>
    public static unsafe void Flush()
    {
        bool anything = _groundOrder.Count > 0 || _objCount > 0
                        || _worldOverlayOrder.Count > 0
                        || _uiBackOrder.Count > 0 || _uiOrder.Count > 0
                        || _overlayOrder.Count > 0;
        if (!anything) return;
        EnsureInit();

        FlushBatchList(_groundOrder);
        FlushObjectLayer();
        // WorldOverlay — above the world but below every UI tier. Right
        // home for territory borders, speech balloons, emotes, ZZZs, the
        // hover outline: visually layered over the map yet always
        // covered by the player's HUD / panels / modals.
        FlushBatchList(_worldOverlayOrder);
        FlushBatchList(_uiBackOrder);
        FlushBatchList(_uiOrder);
        // Overlay last — always-on-top tier (tooltips). Nothing in the
        // regular UI can paint over it.
        FlushBatchList(_overlayOrder);

        Gfx.Gl.BindVertexArray(0);
    }

    private static unsafe void FlushBatchList(List<Batch> order)
    {
        var gl = Gfx.Gl;
        foreach (var batch in order)
        {
            if (batch.Indices.Count == 0) continue;
            BindShaderForBatch(batch);
            batch.Texture.Activate(0);

            UploadAndDraw(batch.Verts, batch.Indices);

            batch.Verts.Clear();
            batch.Indices.Clear();
            batch.NextBase = 0;
        }
    }

    private static unsafe void FlushObjectLayer()
    {
        if (_objCount == 0) { _objDepthWindowSet = false; return; }

        // Depth mapping: bias by a reference sortY + scale by a span so Object
        // quads at ANY world coordinate land inside the [-1,1] clip volume (the old
        // fixed scale blew past the clip at large world Y, hiding the whole layer).
        //
        // PREFERRED: the app supplies a STABLE, viewport-relative window via
        // SetObjectDepthWindow — the range covers only what's on screen and moves
        // smoothly with the camera. This is critical: self-calibrating to the whole
        // submitted set (below) lets a far-off object (e.g. a colonist 300 tiles
        // away at the origin colony while you're out in the wild) stretch the range,
        // compressing on-screen sprites into a tiny z-band that SHIFTS every frame
        // as that far object moves → overlapping trees z-fight in and out.
        //
        // FALLBACK (no window set this frame — other games): self-calibrate to the
        // frame's actual sortY span.
        if (!_objDepthWindowSet)
        {
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < _objCount; i++)
            {
                float z = _objVerts[i * FLOATS_PER_QUAD + 2];   // sortY packed into BL.z
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }
            _objZBias  = minZ;
            _objZScale = maxZ > minZ ? 1f / (maxZ - minZ) : 0f;
        }
        _objDepthWindowSet = false;   // window is per-frame; the app re-sets it each frame

        var gl = Gfx.Gl;

        // Depth buffer drives per-quad ordering — clear it fresh for this
        // pass so any junk from the previous frame doesn't occlude us.
        // The other layers (Ground / UI / Overlay) submit with u_zScale=0
        // → gl_Position.z = 1.0, so they all draw at the back regardless
        // of where we leave the depth buffer afterwards.
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.DepthMask(true);
        gl.Clear(ClearBufferMask.DepthBufferBit);

        // Bucket-by-key. We do NOT CPU Y-sort first — the depth buffer
        // resolves overlaps via the sortY packed into each vertex's z.
        // Persistent buffers (declared as statics, see below) get
        // Clear()'d each frame so we don't allocate a fresh
        // Dictionary + 5000-tree verts list per frame — that was
        // hundreds of KB/frame of GC pressure at zoomed-out views and
        // halved framerate as the GC thrashed.
        foreach (var bucket in _objBuckets.Values) bucket.Clear();
        for (int i = 0; i < _objCount; i++)
        {
            var key = _objKey[i];
            if (!_objBuckets.TryGetValue(key, out var list))
            {
                list = new List<int>(64);
                _objBuckets[key] = list;
            }
            list.Add(i);
        }

        foreach (var (_, list) in _objBuckets)
        {
            if (list.Count == 0) continue;
            int quadN = list.Count;
            // Pre-size the flush buffers to the exact count for this
            // bucket — one allocation per bucket instead of 36 + 6
            // per-quad List.Add calls. At 5k objects this drops
            // ~180k Add() calls/frame (each does capacity check +
            // counter bump) to two SetCount + bulk Span.CopyTo calls.
            // Matches the TS spriteBatcher.ts approach (upV.set +
            // upI.set per quad with a Float32Array.subarray view).
            System.Runtime.InteropServices.CollectionsMarshal.SetCount(_objFlushVerts, quadN * FLOATS_PER_QUAD);
            System.Runtime.InteropServices.CollectionsMarshal.SetCount(_objFlushIdx,   quadN * 6);
            var vSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_objFlushVerts);
            var iSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_objFlushIdx);
            uint nextBase = 0;
            for (int q = 0; q < quadN; q++)
            {
                int qi  = list[q];
                int src = qi * FLOATS_PER_QUAD;
                int dst = q  * FLOATS_PER_QUAD;
                _objVerts.AsSpan(src, FLOATS_PER_QUAD).CopyTo(vSpan.Slice(dst, FLOATS_PER_QUAD));
                int ixDst = q * 6;
                iSpan[ixDst    ] = nextBase + 0;
                iSpan[ixDst + 1] = nextBase + 1;
                iSpan[ixDst + 2] = nextBase + 2;
                iSpan[ixDst + 3] = nextBase + 2;
                iSpan[ixDst + 4] = nextBase + 3;
                iSpan[ixDst + 5] = nextBase + 0;
                nextBase += 4;
            }
            int sample = list[0];
            var tex = _objTexture[sample];
            var mat = _objMaterial[sample];
            BindShaderForObject(tex, mat);
            UploadAndDraw(_objFlushVerts, _objFlushIdx);
        }

        // Free the slot refs (no per-quad allocation of new ObjectEntry
        // anymore, but stale Texture / Material references on the slots
        // would still pin them; null them out before resetting count).
        for (int i = 0; i < _objCount; i++)
        {
            _objKey[i] = null!;
            _objTexture[i] = null!;
            _objMaterial[i] = null;
        }
        _objCount = 0;

        gl.Disable(EnableCap.DepthTest);
    }

    /// <summary>Grow every parallel-object-store array by 2× when the
    /// capacity is reached. Doubling keeps the amortised cost O(1) per
    /// quad while the steady-state cap is still O(max-quads-per-frame). */</summary>
    private static void GrowObjStorage()
    {
        int newCap = _objCapacity * 2;
        Array.Resize(ref _objVerts,    newCap * FLOATS_PER_QUAD);
        Array.Resize(ref _objKey,      newCap);
        Array.Resize(ref _objTexture,  newCap);
        Array.Resize(ref _objMaterial, newCap);
        _objCapacity = newCap;
    }

    /// <summary>Override the world-Y → clip-Z scale for the Object layer.
    /// Pass your world's total Y span (e.g. <c>mapH * tileH</c>) so the
    /// sortY values land in [0, 1] for proper depth-buffer resolution.</summary>
    public static void SetObjectZRange(float zRange)
    {
        if (zRange > 0) _objZScale = 1f / zRange;
    }

    private static void BindShaderForBatch(Batch batch)
    {
        Shader shader;
        if (batch.Material != null && batch.Material.HasCustomShader)
        {
            shader = batch.Material.CustomShader!;
            shader.Use();
            shader.SetUniform("u_proj", Draw.Projection);
            batch.Material.ApplyUniforms(shader);
        }
        else
        {
            shader = _batchShader!;
            shader.Use();
            shader.SetUniform("u_proj", Draw.Projection);
        }
        shader.SetUniform("u_diffuse", 0);
        // Non-Object layers use draw-order ordering — zero out the
        // Y→Z packing so gl_Position.z collapses to 1.0 in the shader.
        TrySetUniform(shader, "u_zScale", 0f);
        ApplyLighting(shader, batch.Material);
    }

    /// <summary>For a normal-mapped, lighting-enabled material: bind its
    /// normal map to unit 1 and push the scene's ambient + point lights
    /// into the shader. No-op for ordinary materials, so the fast path is
    /// untouched. Leaves texture unit 0 active for the caller's diffuse
    /// bind.</summary>
    private static void ApplyLighting(Shader shader, Material? mat)
    {
        if (mat is not { ReceivesLighting: true }) return;
        Lighting.UploadForward(shader);
        // Always bind SOMETHING to unit 1 — a real normal map if the
        // material has one, else the shared flat fallback — so u_normal
        // never accidentally samples the diffuse (unit 0).
        (mat.NormalTexture ?? NormalMapGenerator.FlatNormal()).Activate(1);
        TrySetUniformI(shader, "u_normal", 1);
        // Restore unit 0 as the active unit so the diffuse bind that
        // follows (tex.Activate(0)) lands where the shader expects it.
        Gfx.Gl.ActiveTexture(Silk.NET.OpenGL.TextureUnit.Texture0);
    }

    private static void BindShaderForObject(Texture tex, Material? mat)
    {
        Shader shader;
        if (mat != null && mat.HasCustomShader)
        {
            shader = mat.CustomShader!;
            shader.Use();
            shader.SetUniform("u_proj", Draw.Projection);
            mat.ApplyUniforms(shader);
        }
        else
        {
            shader = _batchShader!;
            shader.Use();
            shader.SetUniform("u_proj", Draw.Projection);
        }
        shader.SetUniform("u_diffuse", 0);
        // Object layer — enable the per-quad Y→Z packing. Custom shaders
        // that don't declare u_zScale (e.g. font / outline / recolor that
        // haven't been ported) silently skip via TrySetUniform.
        TrySetUniform(shader, "u_zScale", _objZScale);
        TrySetUniform(shader, "u_zBias", _objZBias);
        ApplyLighting(shader, mat);
        tex.Activate(0);
    }

    /// <summary>Set a uniform if the shader declares it; swallow the
    /// "uniform not active" exception otherwise. Used for cross-cutting
    /// uniforms (u_zScale) that not every custom shader needs.</summary>
    private static void TrySetUniform(Shader shader, string name, float value)
    {
        try { shader.SetUniform(name, value); }
        catch { /* uniform not active in this shader; ignore */ }
    }

    private static void TrySetUniformI(Shader shader, string name, int value)
    {
        try { shader.SetUniform(name, value); }
        catch { /* uniform not active in this shader; ignore */ }
    }

    private static unsafe void UploadAndDraw(List<float> verts, List<uint> indices)
    {
        // Zero-copy upload via CollectionsMarshal.AsSpan — previously
        // we called verts.ToArray() + indices.ToArray() PER FLUSH,
        // allocating + copying the full vertex buffer (14 MB at 100k+
        // quads/frame) just to hand it to glBufferData. The Spans
        // wrap the List's backing array directly so we pin and upload
        // in place; no allocation, no copy.
        var gl = Gfx.Gl;
        var vSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(verts);
        var iSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(indices);
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = vSpan)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vSpan.Length * sizeof(float)), p, BufferUsageARB.DynamicDraw);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* p = iSpan)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(iSpan.Length * sizeof(uint)), p, BufferUsageARB.DynamicDraw);
        gl.DrawElements(PrimitiveType.Triangles, (uint)iSpan.Length, DrawElementsType.UnsignedInt, (void*)0);
        GLStats.IncDrawCalls(iSpan.Length);
    }
}
