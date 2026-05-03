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

    public PulseOnHoverBehavior(float min = 0.94f, float max = 1.08f, float period = 0.9f)
    { Min = min; Max = max; Period = period; }

    public void Update(Context ctx, Element owner)
    {
        bool hovered = ctx.Hovered == owner;
        float target = hovered ? Anim.Pulse(Min, Max, Period) : 1f;
        // Smooth catch-up so leaving the button doesn't snap.
        float k = hovered ? 0.4f : MathF.Min(1f, (float)Time.Delta / MathF.Max(ReleaseTime, 0.0001f));
        _current += (target - _current) * k;
        owner.RenderScale = _current;
    }
}
