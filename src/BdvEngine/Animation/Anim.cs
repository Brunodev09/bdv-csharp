namespace BdvEngine;

/// <summary>
/// Stateless procedural-animation primitives. All functions are pure and read time
/// from <see cref="Time.TotalF"/> by default — pass an explicit `t` to drive the
/// animation from a different clock (e.g., per-instance lifetimes for popups).
///
/// Compose freely:
///   sprite.Transform.Scale = Vector3.One * Anim.Pulse(0.9f, 1.1f, period: 1.5f);
///   color.A = (byte)(255 * Anim.PingPong(0.4f, 1f, period: 1.0f));
///   button.Y = baseY + Anim.SinWave(amplitude: 4f, period: 2f);
/// </summary>
public static class Anim
{
    /// <summary>Sine-based smooth oscillation between min and max with the given period (seconds).</summary>
    public static float Pulse(float min, float max, float period, float phase = 0f, float? t = null)
    {
        float now = t ?? Time.TotalF;
        float s = MathF.Sin((now / MathF.Max(period, 0.0001f) + phase) * MathF.Tau);
        return min + (max - min) * (s * 0.5f + 0.5f);
    }

    /// <summary>Triangle wave between min and max — linear up, linear down, sharper than Pulse.</summary>
    public static float PingPong(float min, float max, float period, float phase = 0f, float? t = null)
    {
        float now = (t ?? Time.TotalF) + phase * period;
        float p = MathF.Max(period, 0.0001f);
        float f = (now / p) % 1f;
        if (f < 0) f += 1f;
        float tri = f < 0.5f ? f * 2f : (1f - f) * 2f;
        return min + (max - min) * tri;
    }

    /// <summary>One-shot ramp from 0 to 1 over `duration` seconds, clamped after.</summary>
    public static float Ramp(float startTime, float duration, float? t = null)
    {
        float now = t ?? Time.TotalF;
        return Math.Clamp((now - startTime) / MathF.Max(duration, 0.0001f), 0f, 1f);
    }

    /// <summary>Centered sine wave around 0 with the given amplitude.</summary>
    public static float SinWave(float amplitude, float period, float phase = 0f, float? t = null)
        => MathF.Sin(((t ?? Time.TotalF) / MathF.Max(period, 0.0001f) + phase) * MathF.Tau) * amplitude;

    public static class Ease
    {
        public static float InOutSine(float x) => -(MathF.Cos(MathF.PI * x) - 1f) * 0.5f;
        public static float InOutQuad(float x) => x < 0.5f ? 2f * x * x : 1f - MathF.Pow(-2f * x + 2f, 2f) * 0.5f;
        public static float OutBack(float x) { const float c1 = 1.70158f; const float c3 = c1 + 1f; return 1f + c3 * MathF.Pow(x - 1f, 3f) + c1 * MathF.Pow(x - 1f, 2f); }
        public static float OutBounce(float x)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (x < 1f / d1) return n1 * x * x;
            if (x < 2f / d1) { x -= 1.5f / d1; return n1 * x * x + 0.75f; }
            if (x < 2.5f / d1) { x -= 2.25f / d1; return n1 * x * x + 0.9375f; }
            x -= 2.625f / d1; return n1 * x * x + 0.984375f;
        }
    }
}
