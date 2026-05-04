namespace BdvEngine.Gui;

/// <summary>
/// Horizontal value slider. Click-and-drag to set, value clamped to [Min, Max].
/// OnChange fires every frame the value moves; OnRelease fires once on mouse-up.
/// While dragging the slider captures input, so leaving the bounds doesn't lose grip.
/// </summary>
public sealed class Slider : Element
{
    public float Min;
    public float Max;
    public float Value;
    public Color TrackColor  = new( 35,  40,  55, 230);
    public Color FillColor   = new( 95, 140, 255, 255);
    public Color HandleColor = new(255, 255, 255, 255);
    public Color BorderColor = new( 80,  90, 120, 255);
    public Action<float>? OnChangeCallback;
    public Action<float>? OnReleaseCallback;

    private bool _dragging;

    public Slider(float x, float y, float w, float h, float min, float max, float value)
    {
        X = x; Y = y; Width = w; Height = h; Min = min; Max = max;
        Value = Math.Clamp(value, min, max);
    }

    public Slider OnChange(Action<float> cb) { OnChangeCallback = cb; return this; }
    public Slider OnRelease(Action<float> cb) { OnReleaseCallback = cb; return this; }
    public Slider WithColors(Color track, Color fill, Color handle)
    { TrackColor = track; FillColor = fill; HandleColor = handle; return this; }

    public override void OnPointerDown(PointerEvent e)
    {
        if (!Enabled) return;
        _dragging = true;
        UpdateValueFromMouseX(e.X);
    }

    public override void OnPointerDrag(PointerEvent e)
    {
        if (_dragging) UpdateValueFromMouseX(e.X);
    }

    public override void OnPointerUp(PointerEvent e)
    {
        if (!_dragging) return;
        _dragging = false;
        OnReleaseCallback?.Invoke(Value);
    }

    private void UpdateValueFromMouseX(float mouseX)
    {
        var (ax, _, aw, _) = AbsoluteRect();
        float t = Math.Clamp((mouseX - ax) / aw, 0f, 1f);
        float newVal = Min + t * (Max - Min);
        if (newVal != Value)
        {
            Value = newVal;
            OnChangeCallback?.Invoke(Value);
        }
    }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (ax, ay, aw, ah) = AbsoluteRect();
        float ws = ctx.WorldScale;
        var w0 = ctx.ToWorld(ax, ay);
        SpriteBatcher.DrawSolid(w0.X, w0.Y, aw * ws, ah * ws, TrackColor, SpriteLayer.UIBack);
        float t = Max > Min ? (Value - Min) / (Max - Min) : 0f;
        SpriteBatcher.DrawSolid(w0.X, w0.Y, aw * t * ws, ah * ws, FillColor, SpriteLayer.UIBack);
        // Thin vertical handle, slightly taller than the track for visibility.
        var wh = ctx.ToWorld(ax + aw * t - 2f, ay - 3f);
        SpriteBatcher.DrawSolid(wh.X, wh.Y, 4f * ws, (ah + 6f) * ws, HandleColor, SpriteLayer.UIBack);
        Draw.RectOutline(w0.X, w0.Y, aw * ws, ah * ws, BorderColor);
        base.Render(ctx);
    }
}
