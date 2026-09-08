using StbImageSharp;

namespace GateKit;

/// <summary>
/// A decoded RGB screenshot, plus the comparisons the gates keep needing: how many pixels changed,
/// how bright a region is, how much colour it carries.
///
/// <para>Kept deliberately small. These gates ask coarse questions of a frame — "did the far half
/// change?", "how much of this is clipped to white?" — and a real image library would be a much
/// larger dependency for questions that are a few loops each.</para>
/// </summary>
public sealed class GateImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Tightly packed RGB, top-left origin, 3 bytes per pixel.</summary>
    private readonly byte[] _rgb;

    private GateImage(int w, int h, byte[] rgb) { Width = w; Height = h; _rgb = rgb; }

    public static GateImage Load(string path)
    {
        using var fs = File.OpenRead(path);
        var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlue);
        return new GateImage(img.Width, img.Height, img.Data);
    }

    public (byte R, byte G, byte B) this[int x, int y]
    {
        get
        {
            int o = (y * Width + x) * 3;
            return (_rgb[o], _rgb[o + 1], _rgb[o + 2]);
        }
    }

    /// <summary>Sub-image in FRACTIONS of the whole (0..1), which is how every gate wants to express
    /// "skip the stats overlay" or "the near half" without hard-coding a resolution.</summary>
    public GateImage Crop(float x0, float y0, float x1, float y1)
    {
        int cx0 = Clamp((int)(x0 * Width), 0, Width - 1), cx1 = Clamp((int)(x1 * Width), 1, Width);
        int cy0 = Clamp((int)(y0 * Height), 0, Height - 1), cy1 = Clamp((int)(y1 * Height), 1, Height);
        int w = Math.Max(cx1 - cx0, 1), h = Math.Max(cy1 - cy0, 1);

        var outRgb = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            Array.Copy(_rgb, ((cy0 + y) * Width + cx0) * 3, outRgb, y * w * 3, w * 3);
        return new GateImage(w, h, outRgb);
    }

    private static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));

    public int PixelCount => Width * Height;

    /// <summary>Mean of all three channels averaged together, 0..255.</summary>
    public double MeanBrightness()
    {
        long sum = 0;
        for (int i = 0; i < _rgb.Length; i++) sum += _rgb[i];
        return (double)sum / _rgb.Length;
    }

    /// <summary>Fraction of pixels whose darkest channel is at or above <paramref name="level"/> —
    /// i.e. how much of the frame has clipped to white.</summary>
    public double FractionAtLeast(int level)
    {
        int n = 0;
        for (int i = 0; i < _rgb.Length; i += 3)
            if (_rgb[i] >= level && _rgb[i + 1] >= level && _rgb[i + 2] >= level) n++;
        return (double)n / PixelCount;
    }

    /// <summary>Mean gap between the brightest and darkest channel of a pixel: 0 for greyscale,
    /// tens for saturated colour. The cheapest way to ask "is this image still in colour?".</summary>
    public double ChannelSpread()
    {
        long sum = 0;
        for (int i = 0; i < _rgb.Length; i += 3)
        {
            int max = Math.Max(_rgb[i], Math.Max(_rgb[i + 1], _rgb[i + 2]));
            int min = Math.Min(_rgb[i], Math.Min(_rgb[i + 1], _rgb[i + 2]));
            sum += max - min;
        }
        return (double)sum / PixelCount;
    }

    /// <summary>Pixels where the two images differ by more than <paramref name="tolerance"/>
    /// summed across channels. Sizes must match.</summary>
    public static int CountDiffering(GateImage a, GateImage b, int tolerance = 0)
    {
        Require(a, b);
        int n = 0;
        for (int i = 0; i < a._rgb.Length; i += 3)
        {
            int d = Math.Abs(a._rgb[i] - b._rgb[i])
                  + Math.Abs(a._rgb[i + 1] - b._rgb[i + 1])
                  + Math.Abs(a._rgb[i + 2] - b._rgb[i + 2]);
            if (d > tolerance) n++;
        }
        return n;
    }

    /// <summary>Largest single-channel difference between two images.</summary>
    public static int PeakDelta(GateImage a, GateImage b)
    {
        Require(a, b);
        int peak = 0;
        for (int i = 0; i < a._rgb.Length; i++)
            peak = Math.Max(peak, Math.Abs(a._rgb[i] - b._rgb[i]));
        return peak;
    }

    /// <summary>Differing pixels within a fractional sub-rect — "did anything change in the near
    /// half?", which is the question a whole-frame percentage would average away.</summary>
    public static int CountDifferingIn(GateImage a, GateImage b, float x0, float y0, float x1, float y1,
                                       int tolerance = 0)
        => CountDiffering(a.Crop(x0, y0, x1, y1), b.Crop(x0, y0, x1, y1), tolerance);

    private static void Require(GateImage a, GateImage b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            throw new InvalidOperationException(
                $"gate: image sizes differ ({a.Width}x{a.Height} vs {b.Width}x{b.Height}) — " +
                "the two runs rendered at different resolutions, so any diff would be meaningless.");
    }
}
