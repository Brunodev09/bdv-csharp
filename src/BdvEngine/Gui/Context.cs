namespace BdvEngine.Gui;

/// <summary>
/// Per-frame context passed down the UI tree. Holds the rendering surface (camera +
/// viewport for screen-space conversion), the default font, and aggregated input
/// state (mouse position + edge-detected click/release flags). Widgets read from
/// this; <see cref="Root"/> populates it once per Update/Render call.
/// </summary>
public sealed class Context
{
    public Camera Camera = null!;
    public int ViewportW;
    public int ViewportH;
    public Font? DefaultFont;
    public float DefaultTextScale = 0.4f;

    public float MouseX;
    public float MouseY;
    public bool MouseDown;
    /// <summary>True the single frame the mouse went from up to down.</summary>
    public bool MouseClicked;
    /// <summary>True the single frame the mouse went from down to up.</summary>
    public bool MouseReleased;

    /// <summary>Topmost pickable element under the cursor this frame, or null.</summary>
    public Element? Hovered;
    /// <summary>Element that has captured input (e.g., a slider being dragged).</summary>
    public Element? Capturing;

    /// <summary>UI canvas scale (set by Root). Multiplies logical coords to actual pixels.</summary>
    public float UIScale = 1f;

    /// <summary>1 logical UI pixel = WorldScale world units. Accounts for camera zoom + canvas scale.</summary>
    public float WorldScale => UIScale / Camera.Zoom;

    /// <summary>Convert a logical-UI-pixel position to a world-space position.</summary>
    public System.Numerics.Vector2 ToWorld(float screenX, float screenY)
        => Camera.ScreenToWorld(screenX * UIScale, screenY * UIScale, ViewportW, ViewportH);
}
