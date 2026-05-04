namespace BdvEngine.Gui;

public enum SelectableState { Normal, Highlighted, Pressed, Selected, Disabled }

/// <summary>
/// Five-state color set shared by selectable widgets (Button, Toggle, Dropdown).
/// Mirrors Unity's <c>UnityEngine.UI.ColorBlock</c>.
/// </summary>
public struct ColorBlock
{
    public Color Normal;
    public Color Highlighted;
    public Color Pressed;
    public Color Selected;
    public Color Disabled;
    /// <summary>Seconds to lerp between states. 0 = snap.</summary>
    public float FadeDuration;

    public Color For(SelectableState s) => s switch
    {
        SelectableState.Highlighted => Highlighted,
        SelectableState.Pressed     => Pressed,
        SelectableState.Selected    => Selected,
        SelectableState.Disabled    => Disabled,
        _                           => Normal,
    };

    public static ColorBlock DefaultButton => new()
    {
        Normal      = new Color( 45,  50,  70, 230),
        Highlighted = new Color( 70,  80, 110, 240),
        Pressed     = new Color( 95, 110, 150, 245),
        Selected    = new Color( 80,  95, 130, 240),
        Disabled    = new Color( 40,  40,  45, 200),
        FadeDuration = 0.10f,
    };

    public static ColorBlock DefaultToggle => new()
    {
        Normal      = new Color( 40,  45,  60, 220),
        Highlighted = new Color( 60,  70,  90, 235),
        Pressed     = new Color( 90, 100, 130, 245),
        Selected    = new Color( 95, 200, 140, 255),
        Disabled    = new Color( 35,  35,  40, 180),
        FadeDuration = 0.10f,
    };
}
