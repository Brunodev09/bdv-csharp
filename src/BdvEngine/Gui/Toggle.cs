namespace BdvEngine.Gui;

/// <summary>
/// Boolean Toggle styled as a radio/checkbox-style button. Differs from
/// <see cref="Checkbox"/> in that it uses a full <see cref="ColorBlock"/> selection
/// state machine and integrates with <see cref="ToggleGroup"/> for mutual exclusion
/// (radio button behavior).
/// </summary>
public sealed class Toggle : Element
{
    public string LabelText;
    public bool Value;
    public ColorBlock Colors = ColorBlock.DefaultToggle;
    public Color BorderColor = new(100, 110, 130, 255);
    public Color TextColor = new(230, 235, 245, 255);
    public Font? Font;
    public float TextScale = 0.32f;
    public Action<bool>? OnChangeCallback;
    public ToggleGroup? Group;

    private bool _pressed;
    private float _displayedR, _displayedG, _displayedB, _displayedA;

    public Toggle(float x, float y, float w, float h, string label, bool value)
    { X = x; Y = y; Width = w; Height = h; LabelText = label; Value = value; }

    public Toggle OnChange(Action<bool> cb) { OnChangeCallback = cb; return this; }
    public Toggle WithFont(Font f, float scale = 0.32f) { Font = f; TextScale = scale; return this; }
    public Toggle WithColors(ColorBlock cb) { Colors = cb; return this; }
    public Toggle InGroup(ToggleGroup group) { Group = group; group.Register(this); return this; }

    public override void OnPointerDown (PointerEvent e) { if (Enabled) _pressed = true; }
    public override void OnPointerUp   (PointerEvent e) { _pressed = false; }
    public override void OnPointerClick(PointerEvent e)
    {
        if (!Enabled) return;
        bool newVal = Group != null ? true : !Value;
        if (Group != null && !Group.AllowSwitchOff && Value) return;
        if (Group != null) Group.SetSelected(this);
        else SetValueInternal(newVal);
    }

    internal void SetValueInternal(bool v)
    {
        if (Value == v) return;
        Value = v;
        OnChangeCallback?.Invoke(Value);
    }

    private SelectableState CurrentState()
    {
        if (!Enabled) return SelectableState.Disabled;
        if (_pressed) return SelectableState.Pressed;
        if (Value)    return SelectableState.Selected;
        return SelectableState.Normal;
    }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (ax, ay, aw, ah) = AbsoluteRect();
        float a = EffectiveAlpha;
        float ws = ctx.WorldScale;
        float box = ah;

        // Color fade lerp toward target.
        Color target = Colors.For(CurrentState());
        if (Colors.FadeDuration <= 0f || Time.Delta <= 0)
        {
            _displayedR = target.R; _displayedG = target.G;
            _displayedB = target.B; _displayedA = target.A;
        }
        else
        {
            float k = MathF.Min(1f, (float)(Time.Delta / Colors.FadeDuration));
            _displayedR += (target.R - _displayedR) * k;
            _displayedG += (target.G - _displayedG) * k;
            _displayedB += (target.B - _displayedB) * k;
            _displayedA += (target.A - _displayedA) * k;
        }
        Color disp = new((byte)_displayedR, (byte)_displayedG, (byte)_displayedB, (byte)_displayedA);

        var w0 = ctx.ToWorld(ax, ay);
        SpriteBatcher.DrawSolid(w0.X, w0.Y, box * ws, box * ws,
            GuiHelpers.Mul(disp, a), SpriteLayer.UIBack);
        Draw.RectOutline(w0.X, w0.Y, box * ws, box * ws, GuiHelpers.Mul(BorderColor, a));

        var font = Font ?? ctx.DefaultFont;
        if (font != null)
        {
            float baseline = ay + ah * 0.5f + font.Ascent * TextScale * 0.32f;
            TextRenderer.DrawScreen(font, LabelText, ax + box + 8f, baseline,
                TextScale, GuiHelpers.Mul(TextColor, a),
                ctx.Camera, ctx.ViewportW, ctx.ViewportH, default, TextAlign.Left);
        }
        base.Render(ctx);
    }
}
