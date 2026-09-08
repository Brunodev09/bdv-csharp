using System.Numerics;
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

    /// <summary>Move a world-positioned source. Call every frame for anything that moves, or the
    /// sound stays where it started while its object walks away.</summary>
    public void SetPosition(Vector3 p)
    {
        if (Stopped || Al == null) return;
        Al.SetSourceProperty(Source, SourceVector3.Position, p.X, p.Y, p.Z);
    }

    /// <summary>Metres per second, for Doppler. Only matters when
    /// <see cref="AudioManager.DopplerFactor"/> is above 0.</summary>
    public void SetVelocity(Vector3 v)
    {
        if (Stopped || Al == null) return;
        Al.SetSourceProperty(Source, SourceVector3.Velocity, v.X, v.Y, v.Z);
    }

    /// <summary>Distance attenuation for this source.</summary>
    public void SetSpatial(Spatial s)
    {
        if (Stopped || Al == null) return;
        Al.SetSourceProperty(Source, SourceFloat.ReferenceDistance, MathF.Max(s.ReferenceDistance, 1e-4f));
        Al.SetSourceProperty(Source, SourceFloat.MaxDistance, MathF.Max(s.MaxDistance, s.ReferenceDistance));
        // Rolloff 0 is AL's own "no distance attenuation", which is exactly what None means here.
        Al.SetSourceProperty(Source, SourceFloat.RolloffFactor,
                             s.Falloff == AudioFalloff.None ? 0f : MathF.Max(s.Rolloff, 0f));
    }

    /// <summary>Read the source's world position back from OpenAL. Mostly for tests — game code
    /// already knows where it put the sound.</summary>
    public Vector3 GetPosition()
    {
        if (Stopped || Al == null) return Vector3.Zero;
        Al.GetSourceProperty(Source, SourceVector3.Position, out System.Numerics.Vector3 v);
        return v;
    }

    /// <summary>Read the source's velocity back from OpenAL. Mostly for tests.</summary>
    public Vector3 GetVelocity()
    {
        if (Stopped || Al == null) return Vector3.Zero;
        Al.GetSourceProperty(Source, SourceVector3.Velocity, out System.Numerics.Vector3 v);
        return v;
    }

    /// <summary>True when this source is positioned in the world rather than fixed to the
    /// listener's head.</summary>
    public bool IsSpatial()
    {
        if (Stopped || Al == null) return false;
        Al.GetSourceProperty(Source, SourceBoolean.SourceRelative, out bool relative);
        return !relative;
    }

    public bool IsPlaying()
    {
        if (Stopped || Al == null) return false;
        Al.GetSourceProperty(Source, GetSourceInteger.SourceState, out int state);
        return state == (int)SourceState.Playing;
    }
}
