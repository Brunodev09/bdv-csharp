using Silk.NET.OpenGL;

namespace BdvEngine;

public static class Gfx
{
    public static GL Gl { get; internal set; } = null!;
    /// <summary>Physical framebuffer pixels (retina-scaled). Updated each frame by the engine.</summary>
    public static int FramebufferWidth { get; internal set; }
    public static int FramebufferHeight { get; internal set; }
    /// <summary>Logical window pixels (matches what Game.ViewportWidth/Height reports).</summary>
    public static int WindowWidth { get; internal set; }
    public static int WindowHeight { get; internal set; }
}
