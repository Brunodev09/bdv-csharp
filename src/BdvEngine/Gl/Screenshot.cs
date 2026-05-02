using Silk.NET.OpenGL;

namespace BdvEngine;

public static class Screenshot
{
    /// <summary>Set this from anywhere; the engine will capture and clear after the frame finishes rendering.</summary>
    public static string? PendingPath { get; set; }

    public static unsafe void CaptureFullPpm(string path)
    {
        var gl = Gfx.Gl;
        Span<int> vp = stackalloc int[4];
        fixed (int* p = vp) gl.GetInteger(GetPName.Viewport, p);
        CapturePpm(path, vp[0], vp[1], vp[2], vp[3]);
    }

    public static unsafe void CapturePpm(string path, int x, int y, int width, int height)
    {
        var gl = Gfx.Gl;
        var bytes = new byte[width * height * 3];
        fixed (byte* p = bytes)
            gl.ReadPixels(x, y, (uint)width, (uint)height, PixelFormat.Rgb, PixelType.UnsignedByte, p);

        using var fs = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        fs.Write(header);
        // GL origin is bottom-left; flip vertically.
        for (int row = height - 1; row >= 0; row--)
            fs.Write(bytes, row * width * 3, width * 3);
    }
}
