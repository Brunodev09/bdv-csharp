namespace BdvEngine.Gui;

/// <summary>
/// Plain rectangular container with optional fill and border. Use it to group related
/// widgets, add backdrops, draw card-style frames. Pickable by default so it absorbs
/// clicks that would otherwise fall through to whatever is behind the UI.
/// </summary>
public class Panel : Element
{
    public Color? Background;
    public Color? Border;
    public float BorderThickness = 1f;
    /// <summary>Clip child rendering to this panel's bounds via glScissor.
    /// OPT-IN (default false): each Scissor Push/Pop forces a SpriteBatcher
    /// flush (≥2 draw calls per clipped container), so a HUD of nested panels
    /// used to cost ~30 draw calls. Only containers whose content can truly
    /// overflow (ScrollView, scrollable modals) turn it on via <see cref="Clip"/>;
    /// bounded panels render unclipped and free.</summary>
    public bool ClipChildren = false;

    public Panel(float x, float y, float w, float h)
    { X = x; Y = y; Width = w; Height = h; }

    public Panel WithBackground(Color c) { Background = c; return this; }
    public Panel WithBorder(Color c, float thickness = 1f) { Border = c; BorderThickness = thickness; return this; }
    public Panel NotPickable() { Pickable = false; return this; }
    /// <summary>Opt this panel in to glScissor clipping (costs ≥2 draw calls).
    /// Use only when children can overflow the panel (scroll views).</summary>
    public Panel Clip() { ClipChildren = true; return this; }
    public Panel NoClip() { ClipChildren = false; return this; }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (rx, ry, rw, rh) = RenderRect();
        var w = ctx.ToWorld(rx, ry);
        float ws = ctx.WorldScale;
        if (Background.HasValue) SpriteBatcher.DrawSolid(w.X, w.Y, rw * ws, rh * ws, GuiHelpers.Apply(Background.Value, this), SpriteLayer.UIBack);

        if (ClipChildren)
        {
            // Clip to absolute (anchor-resolved) rect, not RenderScale'd — behaviors
            // that pulse a panel shouldn't change which children are visible.
            var (ax, ay, aw, ah) = AbsoluteRect();
            Scissor.Push(ax, ay, aw, ah);
            base.Render(ctx);
            Scissor.Pop();
        }
        else
        {
            base.Render(ctx);
        }

        if (Border.HasValue) Draw.RectOutline(w.X, w.Y, rw * ws, rh * ws, GuiHelpers.Apply(Border.Value, this));
    }
}
