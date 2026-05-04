namespace BdvEngine.Gui;

public enum CanvasScaleMode
{
    /// <summary>1 GUI pixel = 1 logical screen pixel (current/default behavior).</summary>
    ConstantPixelSize,
    /// <summary>UI scales linearly to keep a reference resolution looking the same.
    /// Set <see cref="Root.ReferenceWidth"/> / <see cref="Root.ReferenceHeight"/>;
    /// blend between width-match and height-match via <see cref="Root.MatchWidthOrHeight"/>.</summary>
    ScaleWithScreenSize,
    /// <summary>UI scales by the framebuffer-to-window ratio so it stays the same
    /// physical size on retina displays (no upscaling, just crispness).</summary>
    ConstantPhysicalSize,
}
