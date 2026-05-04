namespace BdvEngine.Gui;

/// <summary>
/// "Breathing" highlight applied while the cursor is over the owner element.
/// Sets <see cref="Element.RenderScale"/> from <see cref="Anim.Pulse"/> and lerps
/// back to 1.0 when un-hovered so toggling looks smooth instead of snapping.
///
/// Hit testing uses the unscaled rect, so the button stays clickable under the
/// cursor even at the smallest point of the pulse.
/// </summary>
public sealed class PulseOnHoverBehavior : IElementBehavior
{
    public float Min;
    public float Max;
    public float Period;
    /// <summary>Seconds to ease back to scale 1.0 after the pointer leaves.</summary>
    public float ReleaseTime = 0.18f;

    private float _current = 1f;
    private bool _hovered;

    public PulseOnHoverBehavior(float min = 0.94f, float max = 1.08f, float period = 0.9f)
    { Min = min; Max = max; Period = period; }

    // Phase 3: react to enter/exit edges instead of polling Hovered each frame.
    public void OnPointerEnter(Element owner, PointerEvent e) => _hovered = true;
    public void OnPointerExit (Element owner, PointerEvent e) => _hovered = false;

    public void Update(Context ctx, Element owner)
    {
        float target = _hovered ? Anim.Pulse(Min, Max, Period) : 1f;
        // Smooth catch-up so leaving the button doesn't snap.
        float k = _hovered ? 0.4f : MathF.Min(1f, (float)Time.Delta / MathF.Max(ReleaseTime, 0.0001f));
        _current += (target - _current) * k;
        owner.RenderScale = _current;
    }
}
