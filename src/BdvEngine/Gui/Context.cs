namespace BdvEngine.Gui;

/// <summary>
/// Per-frame context passed down the UI tree. Holds the rendering surface (camera +
/// viewport for screen-space conversion), the default font, and aggregated input
/// state (mouse position + edge-detected click/release flags). Widgets read from
/// this; <see cref="Root"/> populates it once per Update/Render call.
/// </summary>
public sealed class Context
{
    public Camera2D Camera = null!;
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

    /// <summary>1 screen pixel = WorldScale world units. Equals 1 / camera.Zoom.</summary>
    public float WorldScale => 1f / Camera.Zoom;

    /// <summary>Convert a screen pixel position to a world-space position.</summary>
    public System.Numerics.Vector2 ToWorld(float screenX, float screenY)
        => Camera.ScreenToWorld(screenX, screenY, ViewportW, ViewportH);
}
