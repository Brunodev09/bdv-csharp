using StbImageWriteSharp;

namespace BdvEngine;

/// <summary>
/// Lightweight PNG export. Sister to <see cref="StbImageSharp"/>'s read path —
/// uses the same author's write library so encoding parity is guaranteed.
/// Used by tools (the spritesheet editor) and games that need to dump screenshots
/// or generated atlases at runtime.
/// </summary>
public static class PngWriter
{
    /// <summary>Write an RGBA byte buffer (4 bytes/pixel, row-major) as a PNG file.</summary>
    public static void SavePng(string path, int width, int height, byte[] rgba)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException($"PngWriter.SavePng: expected {width * height * 4} bytes, got {rgba.Length}");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var stream = File.Create(path);
        var writer = new ImageWriter();
        writer.WritePng(rgba, width, height, ColorComponents.RedGreenBlueAlpha, stream);
    }
}
