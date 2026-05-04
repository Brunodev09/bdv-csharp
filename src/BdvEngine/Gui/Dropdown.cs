namespace BdvEngine.Gui;

/// <summary>
/// Single-select dropdown. Closed: shows the current option in a button-like rect
/// with a ▼ indicator. Open: a vertical list pops down below; clicking an option
/// commits it and closes; clicking outside cancels.
///
/// The popup list renders *after* the dropdown's own content so it draws on top
/// of sibling widgets in the same panel, but it does NOT escape its parent's
/// scissor — keep the dropdown in a panel tall enough to hold the open list, or
/// place it as a direct child of Root.
/// </summary>
public sealed class Dropdown : Element
{
    public List<string> Options;
    public int SelectedIndex;
    public bool Open;
    public Action<int>? OnChangeCallback;
    public Font? Font;
    public float TextScale = 0.30f;
    public float ItemHeight = 24f;

    public Color BgIdle    = new( 35,  40,  55, 255);
    public Color BgHover   = new( 55,  65,  85, 255);
    public Color BorderColor = new(100, 110, 130, 255);
    public Color TextColor = new(230, 235, 245, 255);
    public Color ListBg    = new( 25,  30,  42, 255);
    public Color ListHover = new( 70,  85, 115, 255);

    private int _hoverIndex = -1;

    public Dropdown(float x, float y, float w, float h, IEnumerable<string> options, int selected = 0)
    {
        X = x; Y = y; Width = w; Height = h;
        Options = new List<string>(options);
        SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, Options.Count - 1));
    }

    public Dropdown OnChange(Action<int> cb) { OnChangeCallback = cb; return this; }
    public Dropdown WithFont(Font f, float scale = 0.30f) { Font = f; TextScale = scale; return this; }

    public string SelectedOption => SelectedIndex >= 0 && SelectedIndex < Options.Count
        ? Options[SelectedIndex] : "";

    public override void OnPointerClick(PointerEvent e)
    {
        if (!Enabled) return;
        Open = !Open;
        _hoverIndex = -1;
    }

    public override void Update(Context ctx)
    {
        if (Open)
        {
            // Track which item is hovered for highlight.
            var (ax, ay, aw, _) = AbsoluteRect();
            float listY = ay + Height;
            float my = ctx.MouseY;
            float mx = ctx.MouseX;
            _hoverIndex = -1;
            if (mx >= ax && mx < ax + aw)
            {
                int idx = (int)((my - listY) / ItemHeight);
                if (idx >= 0 && idx < Options.Count) _hoverIndex = idx;
            }

            // Click commits or cancels.
            if (ctx.MouseClicked)
            {
                if (_hoverIndex >= 0)
                {
                    if (_hoverIndex != SelectedIndex)
                    {
                        SelectedIndex = _hoverIndex;
                        OnChangeCallback?.Invoke(SelectedIndex);
                    }
                    Open = false;
                }
                else if (ctx.Hovered != this)
                {
                    Open = false;
                }
            }
            if (InputManager.WasKeyPressed(Key.Escape)) Open = false;
        }
        base.Update(ctx);
    }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (ax, ay, aw, ah) = AbsoluteRect();
        float a = EffectiveAlpha;
        float ws = ctx.WorldScale;

        // Header.
        bool headerHover = ctx.Hovered == this && !Open;
        Color bg = headerHover ? BgHover : BgIdle;
        var w0 = ctx.ToWorld(ax, ay);
        SpriteBatcher.DrawSolid(w0.X, w0.Y, aw * ws, ah * ws, GuiHelpers.Mul(bg, a), SpriteLayer.UIBack);
        Draw.RectOutline(w0.X, w0.Y, aw * ws, ah * ws, GuiHelpers.Mul(BorderColor, a));

        var font = Font ?? ctx.DefaultFont;
        if (font != null)
        {
            float baseline = ay + ah * 0.5f + font.Ascent * TextScale * 0.32f;
            TextRenderer.DrawScreen(font, SelectedOption, ax + 8f, baseline,
                TextScale, GuiHelpers.Mul(TextColor, a),
                ctx.Camera, ctx.ViewportW, ctx.ViewportH, default, TextAlign.Left);
            // Indicator triangle.
            float arrowSize = ah * 0.30f;
            float cx = ax + aw - arrowSize - 8f;
            float cy = ay + ah * 0.5f - arrowSize * 0.4f;
            var t1 = ctx.ToWorld(cx, cy);
            var t2 = ctx.ToWorld(cx + arrowSize, cy);
            var t3 = ctx.ToWorld(cx + arrowSize * 0.5f, cy + arrowSize);
            Draw.Triangle(t1.X, t1.Y, t2.X, t2.Y, t3.X, t3.Y, GuiHelpers.Mul(TextColor, a));
        }

        // Open list.
        if (Open)
        {
            float listY = ay + ah;
            float listH = Options.Count * ItemHeight;
            var lw = ctx.ToWorld(ax, listY);
            SpriteBatcher.DrawSolid(lw.X, lw.Y, aw * ws, listH * ws, GuiHelpers.Mul(ListBg, a), SpriteLayer.UIBack);
            for (int i = 0; i < Options.Count; i++)
            {
                float iy = listY + i * ItemHeight;
                if (i == _hoverIndex)
                {
                    var hw = ctx.ToWorld(ax, iy);
                    SpriteBatcher.DrawSolid(hw.X, hw.Y, aw * ws, ItemHeight * ws, GuiHelpers.Mul(ListHover, a), SpriteLayer.UIBack);
                }
                if (font != null)
                {
                    float baseline = iy + ItemHeight * 0.5f + font.Ascent * TextScale * 0.32f;
                    TextRenderer.DrawScreen(font, Options[i], ax + 8f, baseline,
                        TextScale, GuiHelpers.Mul(TextColor, a),
                        ctx.Camera, ctx.ViewportW, ctx.ViewportH, default, TextAlign.Left);
                }
            }
            Draw.RectOutline(lw.X, lw.Y, aw * ws, listH * ws, GuiHelpers.Mul(BorderColor, a));
        }
        base.Render(ctx);
    }

    public override bool Equals(object? obj) => base.Equals(obj);
    public override int GetHashCode() => base.GetHashCode();

    // Hit test extends to the open list area so dropdown-list clicks still hit "this".
    public new bool ContainsScreenPoint(float sx, float sy)
    {
        var (ax, ay, aw, ah) = AbsoluteRect();
        if (sx >= ax && sx < ax + aw && sy >= ay && sy < ay + ah) return true;
        if (Open && sx >= ax && sx < ax + aw && sy >= ay + ah && sy < ay + ah + Options.Count * ItemHeight) return true;
        return false;
    }
}
