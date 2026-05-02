using Silk.NET.OpenAL;

namespace BdvEngine;

public sealed class AudioHandle
{
    internal uint Source;
    internal AL? Al;
    public bool Stopped { get; internal set; }
    public AudioChannel Channel { get; internal set; }

    internal AudioHandle(AL al, uint source, AudioChannel ch)
    {
        Al = al; Source = source; Channel = ch;
    }

    public void Stop()
    {
        if (Stopped || Al == null) return;
        Stopped = true;
        Al.SourceStop(Source);
        Al.DeleteSource(Source);
        Al = null;
    }

    public void SetVolume(float v)
    {
        if (Stopped || Al == null) return;
        Al.SetSourceProperty(Source, SourceFloat.Gain, MathF.Max(0f, v));
    }

    public void SetPan(float p)
    {
        if (Stopped || Al == null) return;
        // Simulate stereo pan with positional audio: place source on a unit arc.
        p = Math.Clamp(p, -1f, 1f);
        Al.SetSourceProperty(Source, SourceVector3.Position, p, 0f, -MathF.Sqrt(MathF.Max(0f, 1f - p * p)));
    }

    public void SetRate(float r)
    {
        if (Stopped || Al == null) return;
        Al.SetSourceProperty(Source, SourceFloat.Pitch, MathF.Max(0.01f, r));
    }

    public bool IsPlaying()
    {
        if (Stopped || Al == null) return false;
        Al.GetSourceProperty(Source, GetSourceInteger.SourceState, out int state);
        return state == (int)SourceState.Playing;
    }
}
