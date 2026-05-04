namespace BdvEngine.Gui;

/// <summary>
/// Single-line text entry with cursor position, shift-select, and clipboard.
///
/// • Click to focus, click outside / Escape to defocus.
/// • Cursor: Left/Right arrows move; Home/End jump.
/// • Selection: hold Shift while moving the cursor (or Cmd/Ctrl-A for all).
/// • Backspace / Delete remove a char or the active selection.
/// • Cmd/Ctrl-C / X / V copy / cut / paste via the system clipboard.
/// • Enter / KeypadEnter fires <see cref="OnSubmitCallback"/>.
/// </summary>
public sealed class TextInput : Element
{
    public string Text;
    public string Placeholder = "";
    public int MaxLength = 256;
    public bool Focused;
    public Font? Font;
    public float TextScale = 0.32f;

    public Color BgIdle           = new( 30,  35,  48, 255);
    public Color BgFocused        = new( 45,  55,  78, 255);
    public Color BorderColor      = new(100, 110, 130, 255);
    public Color TextColor        = new(235, 240, 250, 255);
    public Color PlaceholderColor = new(120, 130, 150, 255);
    public Color CaretColor       = new(255, 240, 180, 255);
    public Color SelectionColor   = new( 80, 110, 200, 160);

    public Action<string>? OnChangeCallback;
    public Action<string>? OnSubmitCallback;

    private int _cursor;       // index 0..Text.Length
    private int _selStart = -1; // -1 = no selection

    public TextInput(float x, float y, float w, float h, string initial = "")
    {
        X = x; Y = y; Width = w; Height = h;
        Text = initial;
        _cursor = Text.Length;
    }

    public TextInput WithPlaceholder(string p) { Placeholder = p; return this; }
    public TextInput WithFont(Font font, float scale = 0.32f) { Font = font; TextScale = scale; return this; }
    public TextInput OnChange(Action<string> cb) { OnChangeCallback = cb; return this; }
    public TextInput OnSubmit(Action<string> cb) { OnSubmitCallback = cb; return this; }

    public override void OnPointerDown(PointerEvent e)
    {
        Focused = true;
        // Position cursor at click — approximate via the font's measure.
        var font = Font;
        if (font == null) { _cursor = Text.Length; _selStart = -1; return; }
        var (ax, _, _, _) = AbsoluteRect();
        float clickX = e.X - ax - 8f;
        _cursor = NearestCharIndex(font, clickX);
        _selStart = -1;
    }

    public override void OnPointerDrag(PointerEvent e)
    {
        var font = Font;
        if (font == null) return;
        var (ax, _, _, _) = AbsoluteRect();
        float dragX = e.X - ax - 8f;
        int dragCursor = NearestCharIndex(font, dragX);
        if (_selStart < 0) _selStart = _cursor;
        _cursor = dragCursor;
    }

    private int NearestCharIndex(Font font, float relX)
    {
        if (relX <= 0) return 0;
        float w = 0;
        for (int i = 0; i < Text.Length; i++)
        {
            string s = Text.Substring(i, 1);
            float charW = font.Measure(s) * TextScale;
            if (relX < w + charW * 0.5f) return i;
            w += charW;
        }
        return Text.Length;
    }

    public override void Update(Context ctx)
    {
        if (Focused)
        {
            if (ctx.MouseClicked && ctx.Hovered != this) Focused = false;

            bool shift = InputManager.IsKeyDown(Key.ShiftLeft) || InputManager.IsKeyDown(Key.ShiftRight);
            bool mod   = InputManager.IsKeyDown(Key.SuperLeft) || InputManager.IsKeyDown(Key.SuperRight)
                      || InputManager.IsKeyDown(Key.ControlLeft) || InputManager.IsKeyDown(Key.ControlRight);
            bool changed = false;

            // Typed printable chars.
            string typed = InputManager.ConsumeTypedString();
            foreach (char c in typed)
            {
                if (c < 32 || c == 127) continue;
                if (mod) continue; // ignore typed chars during mod combos (handled below)
                ReplaceSelection(c.ToString());
                changed = true;
            }

            // Cursor movement.
            if (InputManager.WasKeyPressed(Key.Left))  { MoveCursor(_cursor - 1, shift); }
            if (InputManager.WasKeyPressed(Key.Right)) { MoveCursor(_cursor + 1, shift); }
            if (InputManager.WasKeyPressed(Key.Home))  { MoveCursor(0,           shift); }
            if (InputManager.WasKeyPressed(Key.End))   { MoveCursor(Text.Length, shift); }

            // Editing.
            if (InputManager.WasKeyPressed(Key.Backspace))
            {
                if (HasSelection()) { ReplaceSelection(""); changed = true; }
                else if (_cursor > 0) { Text = Text.Remove(_cursor - 1, 1); _cursor--; changed = true; }
            }
            if (InputManager.WasKeyPressed(Key.Delete))
            {
                if (HasSelection()) { ReplaceSelection(""); changed = true; }
                else if (_cursor < Text.Length) { Text = Text.Remove(_cursor, 1); changed = true; }
            }
            if (InputManager.WasKeyPressed(Key.Enter) || InputManager.WasKeyPressed(Key.KeypadEnter))
            {
                OnSubmitCallback?.Invoke(Text);
                Focused = false;
            }
            if (InputManager.WasKeyPressed(Key.Escape)) Focused = false;

            // Clipboard / select-all.
            if (mod)
            {
                if (InputManager.WasKeyPressed(Key.A)) { _selStart = 0; _cursor = Text.Length; }
                if (InputManager.WasKeyPressed(Key.C)) Clipboard.SetText(GetSelectedText());
                if (InputManager.WasKeyPressed(Key.X) && HasSelection())
                {
                    Clipboard.SetText(GetSelectedText());
                    ReplaceSelection("");
                    changed = true;
                }
                if (InputManager.WasKeyPressed(Key.V))
                {
                    string s = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(s))
                    {
                        // Strip newlines (single-line input).
                        s = s.Replace("\r", "").Replace("\n", " ");
                        ReplaceSelection(s);
                        changed = true;
                    }
                }
            }

            if (changed) OnChangeCallback?.Invoke(Text);
        }
        base.Update(ctx);
    }

    private void MoveCursor(int target, bool extendSelection)
    {
        target = Math.Clamp(target, 0, Text.Length);
        if (extendSelection) { if (_selStart < 0) _selStart = _cursor; }
        else _selStart = -1;
        _cursor = target;
    }

    private bool HasSelection() => _selStart >= 0 && _selStart != _cursor;

    private string GetSelectedText()
    {
        if (!HasSelection()) return "";
        int a = Math.Min(_selStart, _cursor), b = Math.Max(_selStart, _cursor);
        return Text.Substring(a, b - a);
    }

    private void ReplaceSelection(string with)
    {
        if (HasSelection())
        {
            int a = Math.Min(_selStart, _cursor), b = Math.Max(_selStart, _cursor);
            Text = Text.Remove(a, b - a).Insert(a, with);
            _cursor = a + with.Length;
            _selStart = -1;
        }
        else
        {
            if (Text.Length + with.Length > MaxLength) with = with.Substring(0, Math.Max(0, MaxLength - Text.Length));
            Text = Text.Insert(_cursor, with);
            _cursor += with.Length;
        }
        _cursor = Math.Clamp(_cursor, 0, Text.Length);
    }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (rx, ry, rw, rh) = AbsoluteRect();
        float a = EffectiveAlpha;
        float ws = ctx.WorldScale;
        var w = ctx.ToWorld(rx, ry);
        SpriteBatcher.DrawSolid(w.X, w.Y, rw * ws, rh * ws,
            GuiHelpers.Mul(Focused ? BgFocused : BgIdle, a), SpriteLayer.UIBack);
        Draw.RectOutline(w.X, w.Y, rw * ws, rh * ws, GuiHelpers.Mul(BorderColor, a));

        var font = Font ?? ctx.DefaultFont;
        if (font == null) { base.Render(ctx); return; }

        float baseline = ry + rh * 0.5f + font.Ascent * TextScale * 0.32f;
        string display = Text.Length > 0 ? Text : Placeholder;
        Color col = Text.Length > 0 ? TextColor : PlaceholderColor;

        // Selection highlight (behind text).
        if (Focused && HasSelection() && Text.Length > 0)
        {
            int a1 = Math.Min(_selStart, _cursor);
            int b1 = Math.Max(_selStart, _cursor);
            float xPre  = font.Measure(Text.Substring(0, a1)) * TextScale;
            float xSel  = font.Measure(Text.Substring(a1, b1 - a1)) * TextScale;
            var swA = ctx.ToWorld(rx + 8f + xPre, ry + 4f);
            SpriteBatcher.DrawSolid(swA.X, swA.Y, xSel * ws, (rh - 8f) * ws,
                GuiHelpers.Mul(SelectionColor, a), SpriteLayer.UIBack);
        }

        TextRenderer.DrawScreen(font, display, rx + 8f, baseline, TextScale,
            GuiHelpers.Mul(col, a), ctx.Camera, ctx.ViewportW, ctx.ViewportH, default, TextAlign.Left);

        // Caret.
        if (Focused && (int)(Time.TotalF * 2f) % 2 == 0)
        {
            float caretX = rx + 8f + font.Measure(Text.Substring(0, Math.Min(_cursor, Text.Length))) * TextScale + 1f;
            var cw0 = ctx.ToWorld(caretX, ry + 4f);
            SpriteBatcher.DrawSolid(cw0.X, cw0.Y, 2f * ws, (rh - 8f) * ws,
                GuiHelpers.Mul(CaretColor, a), SpriteLayer.UI);
        }
        base.Render(ctx);
    }
}
