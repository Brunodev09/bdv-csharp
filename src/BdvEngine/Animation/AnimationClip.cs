using System.Numerics;

namespace BdvEngine;

/// <summary>Which part of a node's transform a channel drives.</summary>
public enum AnimationPath { Translation, Rotation, Scale }

/// <summary>glTF sampler interpolation. CUBICSPLINE is parsed but sampled as LINEAR (see
/// <see cref="AnimationSampler"/>).</summary>
public enum Interpolation { Linear, Step, CubicSpline }

/// <summary>
/// A keyframe track: sorted times and the values at those times, flattened. Vec3 tracks have 3
/// components per key, quaternion tracks 4.
/// </summary>
public sealed class AnimationSampler
{
    public readonly float[] Times;
    public readonly float[] Values;
    public readonly int Components;
    public readonly Interpolation Mode;

    public AnimationSampler(float[] times, float[] values, int components, Interpolation mode)
    {
        Times = times;
        Values = values;
        Components = components;
        Mode = mode;
    }

    public float Duration => Times.Length == 0 ? 0f : Times[^1];

    /// <summary>Find the key pair bracketing <paramref name="t"/> and the 0..1 blend between them.
    /// Clamps at both ends — a clip shorter than the one driving it holds its last pose rather than
    /// snapping back.</summary>
    private void Bracket(float t, out int k0, out int k1, out float f)
    {
        int n = Times.Length;
        if (n == 0) { k0 = k1 = 0; f = 0f; return; }
        if (t <= Times[0]) { k0 = k1 = 0; f = 0f; return; }
        if (t >= Times[n - 1]) { k0 = k1 = n - 1; f = 0f; return; }

        // Binary search: clips can carry hundreds of keys and this runs per channel per frame.
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (Times[mid] <= t) lo = mid; else hi = mid;
        }
        k0 = lo;
        k1 = hi;
        float span = Times[k1] - Times[k0];
        f = span > 1e-9f ? (t - Times[k0]) / span : 0f;
        if (Mode == Interpolation.Step) f = 0f;
    }

    public Vector3 SampleVec3(float t)
    {
        if (Times.Length == 0) return Vector3.Zero;
        Bracket(t, out int k0, out int k1, out float f);
        var a = new Vector3(Values[k0 * 3], Values[k0 * 3 + 1], Values[k0 * 3 + 2]);
        if (k0 == k1 || f <= 0f) return a;
        var b = new Vector3(Values[k1 * 3], Values[k1 * 3 + 1], Values[k1 * 3 + 2]);
        return Vector3.Lerp(a, b, f);
    }

    public Quaternion SampleQuaternion(float t)
    {
        if (Times.Length == 0) return Quaternion.Identity;
        Bracket(t, out int k0, out int k1, out float f);
        var a = new Quaternion(Values[k0 * 4], Values[k0 * 4 + 1], Values[k0 * 4 + 2], Values[k0 * 4 + 3]);
        if (k0 == k1 || f <= 0f) return Quaternion.Normalize(a);
        var b = new Quaternion(Values[k1 * 4], Values[k1 * 4 + 1], Values[k1 * 4 + 2], Values[k1 * 4 + 3]);
        // Slerp, not Lerp: rotations that lerp componentwise slide off the unit sphere and the
        // limb visibly shortens through the middle of a large rotation.
        return Quaternion.Slerp(a, b, f);
    }
}

/// <summary>One sampler bound to one node's transform channel.</summary>
public sealed class AnimationChannel
{
    public readonly SimObject Target;
    public readonly AnimationPath Path;
    public readonly AnimationSampler Sampler;

    public AnimationChannel(SimObject target, AnimationPath path, AnimationSampler sampler)
    {
        Target = target;
        Path = path;
        Sampler = sampler;
    }
}

/// <summary>
/// A named set of channels — one glTF animation ("Walk", "Idle"). Clips do not own a playhead;
/// <see cref="Animator"/> does, so the same clip can drive several characters.
/// </summary>
public sealed class AnimationClip
{
    public readonly string Name;
    public readonly List<AnimationChannel> Channels;

    /// <summary>Longest channel, in seconds.</summary>
    public readonly float Duration;

    public AnimationClip(string name, List<AnimationChannel> channels)
    {
        Name = name;
        Channels = channels;
        float d = 0f;
        foreach (var c in channels) d = MathF.Max(d, c.Sampler.Duration);
        Duration = d;
    }

    /// <summary>Write this clip's pose at <paramref name="time"/> straight into the target nodes.</summary>
    public void Apply(float time)
    {
        foreach (var ch in Channels)
        {
            var tr = ch.Target.Transform;
            switch (ch.Path)
            {
                case AnimationPath.Translation: tr.Position = ch.Sampler.SampleVec3(time); break;
                case AnimationPath.Scale: tr.Scale = ch.Sampler.SampleVec3(time); break;
                case AnimationPath.Rotation:
                    tr.Orientation = ch.Sampler.SampleQuaternion(time);
                    tr.UseOrientation = true;
                    break;
            }
        }
    }

    /// <summary>Blend this clip's pose at <paramref name="time"/> over whatever the nodes already
    /// hold, by <paramref name="weight"/> (0 = leave alone, 1 = fully this clip). The mechanism
    /// behind <see cref="Animator"/>'s crossfade: apply the outgoing clip, then blend the incoming
    /// one in over it.</summary>
    public void Blend(float time, float weight)
    {
        if (weight <= 0f) return;
        if (weight >= 1f) { Apply(time); return; }

        foreach (var ch in Channels)
        {
            var tr = ch.Target.Transform;
            switch (ch.Path)
            {
                case AnimationPath.Translation:
                    tr.Position = Vector3.Lerp(tr.Position, ch.Sampler.SampleVec3(time), weight);
                    break;
                case AnimationPath.Scale:
                    tr.Scale = Vector3.Lerp(tr.Scale, ch.Sampler.SampleVec3(time), weight);
                    break;
                case AnimationPath.Rotation:
                    var target = ch.Sampler.SampleQuaternion(time);
                    // A node still on the Euler path has no meaningful Orientation to blend from.
                    tr.Orientation = tr.UseOrientation
                        ? Quaternion.Slerp(tr.Orientation, target, weight)
                        : target;
                    tr.UseOrientation = true;
                    break;
            }
        }
    }
}
