namespace BdvEngine.Gui;

/// <summary>
/// Textured rectangle: an icon, portrait, sprite-sheet cell, or full texture rendered
/// inside any panel. Pulls from a Material (registered with MaterialManager) and
/// supports sampling either the whole texture or a grid sub-cell.
/// </summary>
public sealed class Image : Element
{
    public Material Material;
    public Color Tint = Color.White;
    /// <summary>UV rect inside the texture. (0,0)-(1,1) = full texture.</summary>
    public float U0 = 0f, V0 = 0f, U1 = 1f, V1 = 1f;

    public Image(float x, float y, float w, float h, Material material)
    {
        X = x; Y = y; Width = w; Height = h; Material = material;
        Pickable = false; // images don't usually intercept clicks; flip via WithPickable() if wanted
    }

    public Image(float x, float y, float w, float h, string materialName)
        : this(x, y, w, h, MaterialManager.Get(materialName)) { }

    /// <summary>Sample one cell of a uniform spritesheet grid.</summary>
    public Image WithSubRect(int srcCol, int srcRow, int gridCols, int gridRows)
    {
        U0 = srcCol / (float)gridCols;
        V0 = srcRow / (float)gridRows;
        U1 = (srcCol + 1) / (float)gridCols;
        V1 = (srcRow + 1) / (float)gridRows;
        return this;
    }

    /// <summary>Sample an arbitrary UV rectangle.</summary>
    public Image WithUV(float u0, float v0, float u1, float v1)
    { U0 = u0; V0 = v0; U1 = u1; V1 = v1; return this; }

    public Image WithTint(Color tint) { Tint = tint; return this; }
    public Image Pick(bool pickable) { Pickable = pickable; return this; }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (rx, ry, rw, rh) = RenderRect();
        float invZoom = 1f / ctx.Camera.Zoom;
        var topLeft = ctx.Camera.ScreenToWorld(rx, ry, ctx.ViewportW, ctx.ViewportH);
        SpriteBatcher.DrawTextureUV(Material, U0, V0, U1, V1,
            topLeft.X, topLeft.Y, rw * invZoom, rh * invZoom, Tint, SpriteLayer.UI);
        base.Render(ctx);
    }
}
