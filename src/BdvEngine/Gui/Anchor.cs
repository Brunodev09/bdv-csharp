using System.Numerics;

namespace BdvEngine.Gui;

/// <summary>
/// Common anchor presets for <see cref="Element.AnchorTo(Anchor)"/>. Each preset
/// sets <see cref="Element.AnchorMin"/>, <see cref="Element.AnchorMax"/>, and
/// <see cref="Element.Pivot"/> in one call.
///
/// "Pin" presets use a single anchor point — element keeps its explicit
/// `Width`/`Height`. "Stretch" presets use a span — on each stretched axis,
/// `X` becomes the inset from the start anchor line and the size is computed
/// from the parent at render time (with `Width`/`Height` repurposed as the
/// inset from the end anchor line).
/// </summary>
public enum Anchor
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, MiddleCenter, MiddleRight,
    BottomLeft, BottomCenter, BottomRight,
    StretchTop, StretchMiddle, StretchBottom,
    StretchLeft, StretchCenter, StretchRight,
    StretchAll,
}

internal static class AnchorPresets
{
    public static (Vector2 min, Vector2 max, Vector2 pivot) Of(Anchor a) => a switch
    {
        Anchor.TopLeft       => (new(0,    0   ), new(0,    0   ), new(0,    0   )),
        Anchor.TopCenter     => (new(0.5f, 0   ), new(0.5f, 0   ), new(0.5f, 0   )),
        Anchor.TopRight      => (new(1,    0   ), new(1,    0   ), new(1,    0   )),
        Anchor.MiddleLeft    => (new(0,    0.5f), new(0,    0.5f), new(0,    0.5f)),
        Anchor.MiddleCenter  => (new(0.5f, 0.5f), new(0.5f, 0.5f), new(0.5f, 0.5f)),
        Anchor.MiddleRight   => (new(1,    0.5f), new(1,    0.5f), new(1,    0.5f)),
        Anchor.BottomLeft    => (new(0,    1   ), new(0,    1   ), new(0,    1   )),
        Anchor.BottomCenter  => (new(0.5f, 1   ), new(0.5f, 1   ), new(0.5f, 1   )),
        Anchor.BottomRight   => (new(1,    1   ), new(1,    1   ), new(1,    1   )),
        Anchor.StretchTop    => (new(0,    0   ), new(1,    0   ), new(0,    0   )),
        Anchor.StretchMiddle => (new(0,    0.5f), new(1,    0.5f), new(0,    0.5f)),
        Anchor.StretchBottom => (new(0,    1   ), new(1,    1   ), new(0,    1   )),
        Anchor.StretchLeft   => (new(0,    0   ), new(0,    1   ), new(0,    0   )),
        Anchor.StretchCenter => (new(0.5f, 0   ), new(0.5f, 1   ), new(0.5f, 0   )),
        Anchor.StretchRight  => (new(1,    0   ), new(1,    1   ), new(1,    0   )),
        Anchor.StretchAll    => (new(0,    0   ), new(1,    1   ), new(0,    0   )),
        _ => (Vector2.Zero, Vector2.Zero, Vector2.Zero),
    };
}
