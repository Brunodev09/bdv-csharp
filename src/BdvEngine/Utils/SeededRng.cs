namespace BdvEngine;

/// <summary>Park-Miller LCG. Same sequence as the TS version for byte-compatible worlds.</summary>
public sealed class SeededRng
{
    private long _state;

    public SeededRng(int seed)
    {
        _state = seed % 2147483647;
        if (_state <= 0) _state += 2147483646;
    }

    public double Next()
    {
        _state = _state * 16807L % 2147483647L;
        return (_state - 1) / 2147483646.0;
    }

    public int NextInt(int min, int max)
        => (int)(Next() * (max - min + 1)) + min;
}
