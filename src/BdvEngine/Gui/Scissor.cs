using Silk.NET.OpenGL;

namespace BdvEngine.Gui;

/// <summary>
/// glScissor stack for clipping UI children to their parent's bounds. Coordinates are
/// in logical window pixels (top-left origin, Y down) — matching what the rest of the
/// Gui uses. Internally converts to framebuffer pixels and Y-up before calling the GL.
///
/// Push() and Pop() each call SpriteBatcher.Flush() so the scissor change applies
/// only to subsequent draws. Nested pushes intersect with the parent rect.
/// </summary>
public static class Scissor
{
    private static readonly Stack<(int X, int Y, int W, int H)> _stack = new();

    public static void Push(float screenX, float screenY, float width, float height)
    {
        // Drain anything queued before us — those draws should not be scissored.
        SpriteBatcher.Flush();

        float sx = Gfx.WindowWidth  > 0 ? Gfx.FramebufferWidth  / (float)Gfx.WindowWidth  : 1f;
        float sy = Gfx.WindowHeight > 0 ? Gfx.FramebufferHeight / (float)Gfx.WindowHeight : 1f;
        int fx = (int)MathF.Floor(screenX * sx);
        int fyTop = (int)MathF.Floor(screenY * sy);
        int fw = (int)MathF.Ceiling(width  * sx);
        int fh = (int)MathF.Ceiling(height * sy);
        // GL scissor origin is bottom-left; convert top-down screen Y to bottom-up.
        int glY = Gfx.FramebufferHeight - fyTop - fh;

        if (_stack.Count > 0)
        {
            var p = _stack.Peek();
            int x1 = Math.Max(fx, p.X);
            int y1 = Math.Max(glY, p.Y);
            int x2 = Math.Min(fx + fw, p.X + p.W);
            int y2 = Math.Min(glY + fh, p.Y + p.H);
            fx = x1; glY = y1;
            fw = Math.Max(0, x2 - x1);
            fh = Math.Max(0, y2 - y1);
        }

        _stack.Push((fx, glY, fw, fh));
        var gl = Gfx.Gl;
        gl.Enable(EnableCap.ScissorTest);
        gl.Scissor(fx, glY, (uint)fw, (uint)fh);
    }

    public static void Pop()
    {
        // Drain children drawn under this scissor.
        SpriteBatcher.Flush();
        if (_stack.Count > 0) _stack.Pop();

        var gl = Gfx.Gl;
        if (_stack.Count > 0)
        {
            var t = _stack.Peek();
            gl.Scissor(t.X, t.Y, (uint)t.W, (uint)t.H);
        }
        else
        {
            gl.Disable(EnableCap.ScissorTest);
        }
    }
}
