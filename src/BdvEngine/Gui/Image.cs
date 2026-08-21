namespace BdvEngine.Gui;

/// <summary>
/// Textured rectangle: an icon, portrait, sprite-sheet cell, or full texture rendered
/// inside any panel. Pulls from a Material (registered with MaterialManager) and
/// supports sampling either the whole texture or a grid sub-cell.
///
/// Optional 9-slice mode (<see cref="WithSlice"/>) keeps corner pixels at their source
/// size and stretches only the edges/center — the standard trick for chunky button
/// graphics that need to scale cleanly.
/// </summary>
public sealed class Image : Element
{
    public Material Material;
    public Color Tint = Color.White;
    /// <summary>UV rect inside the texture. (0,0)-(1,1) = full texture.</summary>
    public float U0 = 0f, V0 = 0f, U1 = 1f, V1 = 1f;

    /// <summary>9-slice insets in *source pixels* (relative to the U0..U1 / V0..V1 rect).
    /// Zero = no slicing (single stretched quad).</summary>
    public float SliceL, SliceT, SliceR, SliceB;
    public bool IsSliced => SliceL + SliceT + SliceR + SliceB > 0f;

    public Image(float x, float y, float w, float h, Material material)
    {
        X = x; Y = y; Width = w; Height = h; Material = material;
        Pickable = false;
    }

    public Image(float x, float y, float w, float h, string materialName)
        : this(x, y, w, h, MaterialManager.Get(materialName)) { }

    public Image WithSubRect(int srcCol, int srcRow, int gridCols, int gridRows)
    {
        U0 = srcCol / (float)gridCols;
        V0 = srcRow / (float)gridRows;
        U1 = (srcCol + 1) / (float)gridCols;
        V1 = (srcRow + 1) / (float)gridRows;
        return this;
    }

    public Image WithUV(float u0, float v0, float u1, float v1)
    { U0 = u0; V0 = v0; U1 = u1; V1 = v1; return this; }

    public Image WithTint(Color tint) { Tint = tint; return this; }
    public Image Pick(bool pickable) { Pickable = pickable; return this; }

    /// <summary>Configure 9-slice insets in source pixels.</summary>
    public Image WithSlice(float left, float top, float right, float bottom)
    { SliceL = left; SliceT = top; SliceR = right; SliceB = bottom; return this; }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (rx, ry, rw, rh) = RenderRect();
        float invZoom = 1f / ctx.Camera.Zoom;
        var topLeft = ctx.Camera.ScreenToWorld(rx, ry, ctx.ViewportW, ctx.ViewportH);
        Color tint = GuiHelpers.Apply(Tint, this);

        if (IsSliced) RenderSliced(topLeft.X, topLeft.Y, rw * invZoom, rh * invZoom, tint);
        else SpriteBatcher.DrawTextureUV(Material, U0, V0, U1, V1,
            topLeft.X, topLeft.Y, rw * invZoom, rh * invZoom, tint, SpriteLayer.UI);
        base.Render(ctx);
    }

    private void RenderSliced(float wx, float wy, float ww, float wh, Color tint)
    {
        var tex = Material.DiffuseTexture;
        if (tex == null || !tex.IsLoaded) return;

        // Source rect in pixels (within the U/V sub-region).
        float srcX = U0 * tex.Width;
        float srcY = V0 * tex.Height;
        float srcW = (U1 - U0) * tex.Width;
        float srcH = (V1 - V0) * tex.Height;

        float l = MathF.Min(SliceL, srcW * 0.5f);
        float r = MathF.Min(SliceR, srcW * 0.5f);
        float t = MathF.Min(SliceT, srcH * 0.5f);
        float b = MathF.Min(SliceB, srcH * 0.5f);

        // Destination insets are the same screen pixels (corners stay 1:1 with source).
        float dl = l, dr = r, dt = t, db = b;
        if (dl + dr > ww) { float k = ww / (dl + dr); dl *= k; dr *= k; }
        if (dt + db > wh) { float k = wh / (dt + db); dt *= k; db *= k; }

        // Compute UV slices.
        float u0 = srcX / tex.Width;
        float u1 = (srcX + l) / tex.Width;
        float u2 = (srcX + srcW - r) / tex.Width;
        float u3 = (srcX + srcW) / tex.Width;
        float v0 = srcY / tex.Height;
        float v1 = (srcY + t) / tex.Height;
        float v2 = (srcY + srcH - b) / tex.Height;
        float v3 = (srcY + srcH) / tex.Height;

        // Destination rects.
        float x0 = wx, x1 = wx + dl, x2 = wx + ww - dr, x3 = wx + ww;
        float y0 = wy, y1 = wy + dt, y2 = wy + wh - db, y3 = wy + wh;

        // 9 sub-quads in row-major order.
        Quad(u0, v0, u1, v1, x0, y0, x1, y1, tint); // TL
        Quad(u1, v0, u2, v1, x1, y0, x2, y1, tint); // T
        Quad(u2, v0, u3, v1, x2, y0, x3, y1, tint); // TR
        Quad(u0, v1, u1, v2, x0, y1, x1, y2, tint); // L
        Quad(u1, v1, u2, v2, x1, y1, x2, y2, tint); // C
        Quad(u2, v1, u3, v2, x2, y1, x3, y2, tint); // R
        Quad(u0, v2, u1, v3, x0, y2, x1, y3, tint); // BL
        Quad(u1, v2, u2, v3, x1, y2, x2, y3, tint); // B
        Quad(u2, v2, u3, v3, x2, y2, x3, y3, tint); // BR
    }

    private void Quad(float uA, float vA, float uB, float vB,
        float xA, float yA, float xB, float yB, Color tint)
    {
        if (xB <= xA || yB <= yA) return;
        SpriteBatcher.DrawTextureUV(Material, uA, vA, uB, vB,
            xA, yA, xB - xA, yB - yA, tint, SpriteLayer.UI);
    }
}
