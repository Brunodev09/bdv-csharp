namespace BdvEngine;

/// <summary>Value-noise + fBM, deterministic for a given seed (matches TS port).</summary>
public sealed class Noise
{
    private readonly int[] _perm = new int[512];

    public Noise(int seed)
    {
        for (int i = 0; i < 256; i++) _perm[i] = i;
        var rng = new SeededRng(seed);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.NextInt(0, i);
            (_perm[i], _perm[j]) = (_perm[j], _perm[i]);
        }
        for (int i = 0; i < 256; i++) _perm[256 + i] = _perm[i];
    }

    private int Hash(int x, int y) => _perm[(_perm[x & 255] + y) & 511];
    private static float Lerp(float a, float b, float t) => a + t * (b - a);
    private static float Smooth(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    public float Get(float x, float y)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float xf = x - xi, yf = y - yi;
        float sx = Smooth(xf), sy = Smooth(yf);
        float a = Hash(xi, yi) / 255f;
        float b = Hash(xi + 1, yi) / 255f;
        float c = Hash(xi, yi + 1) / 255f;
        float d = Hash(xi + 1, yi + 1) / 255f;
        return Lerp(Lerp(a, b, sx), Lerp(c, d, sx), sy);
    }

    public float Fbm(float x, float y, int octaves = 4)
    {
        float val = 0f, amp = 0.5f, freq = 1f, max = 0f;
        for (int i = 0; i < octaves; i++)
        {
            val += Get(x * freq, y * freq) * amp;
            max += amp; amp *= 0.5f; freq *= 2f;
        }
        return val / max;
    }
}
