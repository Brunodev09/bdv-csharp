namespace BdvEngine;

/// <summary>
/// Monotonic game clock. Engine.OnUpdate advances it once per frame so any system that
/// needs "current time" — animations, particles, shaders — can read it without
/// threading delta through every call site.
/// </summary>
public static class Time
{
    public static double Total { get; private set; }
    public static float TotalF => (float)Total;
    public static double Delta { get; private set; }

    public static void Advance(double deltaSeconds)
    {
        Delta = deltaSeconds;
        Total += deltaSeconds;
    }

    public static void Reset()
    {
        Total = 0;
        Delta = 0;
    }
}
