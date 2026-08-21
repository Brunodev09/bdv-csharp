using System.Text;

namespace BdvEngine.Gui;

/// <summary>
/// F9-toggled debug overlay. Draws thin outlines around every visible
/// widget so the layout is instantly readable, and prints the hovered
/// element's type + rect in a corner HUD. Read-only — moving widgets
/// stays a JSON-file edit (which is precisely the workflow we want).
///
/// Enable / disable via <see cref="Enabled"/>. Root owns one of
/// these and calls <see cref="Update"/> + <see cref="Render"/> once
/// per frame; games pay nothing when disabled.
/// </summary>
public sealed class UiLayoutInspector
{
    public bool Enabled;
    private Element? _hovered;

    private static readonly Color OutlineNormal = new(120, 150, 200, 90);
    private static readonly Color OutlineFlex   = new(120, 210, 130, 130);
    private static readonly Color OutlineGrid   = new(220, 150, 210, 130);
    private static readonly Color OutlineHover  = new(255, 220, 130, 220);

    public void Update(Root root, Context ctx)
    {
        if (InputManager.WasKeyPressed(Key.F9)) Enabled = !Enabled;
        if (!Enabled) { _hovered = null; return; }
        var m = InputManager.GetMousePosition();
        _hovered = PickAt(root, m.X, m.Y);
    }

    public void Render(Root root, Context ctx)
    {
        if (!Enabled) return;
        Walk(root);

        // Corner HUD: hover info.
        var font = ctx.DefaultFont;
        if (font == null) return;
        SpriteBatcher.DrawSolid(6, 6, 640, 40, new Color(0, 0, 0, 180), SpriteLayer.Overlay);
        var help = "F9  UI layout inspector — outlines every widget. Hover to inspect. Edit the JSON to move.";
        TextRenderer.DrawScreen(font, help, 12, 22, 0.18f, new Color(220, 230, 255, 240),
            ctx.Camera, ctx.ViewportW, ctx.ViewportH, default, TextAlign.Left);

        var sb = new StringBuilder();
        if (_hovered != null)
        {
            var (x, y, w, h) = _hovered.AbsoluteRect();
            sb.Append(_hovered.GetType().Name);
            if (!string.IsNullOrEmpty(_hovered.Name)) sb.Append("  '").Append(_hovered.Name).Append('\'');
            sb.Append("   X=").Append((int)_hovered.X)
              .Append(" Y=").Append((int)_hovered.Y)
              .Append(" W=").Append((int)_hovered.Width)
              .Append(" H=").Append((int)_hovered.Height);
            sb.Append("   abs=(").Append((int)x).Append(',').Append((int)y)
              .Append(' ').Append((int)w).Append('x').Append((int)h).Append(')');
        }
        else sb.Append("(hover any element to see its rect)");
        TextRenderer.DrawScreen(font, sb.ToString(), 12, 40, 0.20f, new Color(255, 220, 130, 255),
            ctx.Camera, ctx.ViewportW, ctx.ViewportH, default, TextAlign.Left);
    }

    private void Walk(Element el)
    {
        for (int i = 0; i < el.Children.Count; i++)
        {
            var c = el.Children[i];
            if (!c.Visible) continue;
            var (x, y, w, h) = c.AbsoluteRect();
            Color color;
            if      (c == _hovered)        color = OutlineHover;
            else if (c is FlexContainer)   color = OutlineFlex;
            else if (c is GridContainer)   color = OutlineGrid;
            else                            color = OutlineNormal;
            Outline(x, y, w, h, color);
            Walk(c);
        }
    }

    private static void Outline(float x, float y, float w, float h, Color color)
    {
        const float t = 1f;
        SpriteBatcher.DrawSolid(x,          y,         w, t, color, SpriteLayer.Overlay);
        SpriteBatcher.DrawSolid(x,          y + h - t, w, t, color, SpriteLayer.Overlay);
        SpriteBatcher.DrawSolid(x,          y,         t, h, color, SpriteLayer.Overlay);
        SpriteBatcher.DrawSolid(x + w - t,  y,         t, h, color, SpriteLayer.Overlay);
    }

    private static Element? PickAt(Element el, float mx, float my)
    {
        if (!el.Visible) return null;
        for (int i = el.Children.Count - 1; i >= 0; i--)
        {
            var hit = PickAt(el.Children[i], mx, my);
            if (hit != null) return hit;
        }
        if (el.Parent == null) return null;
        var (x, y, w, h) = el.AbsoluteRect();
        if (mx < x || mx >= x + w || my < y || my >= y + h) return null;
        return el;
    }
}
