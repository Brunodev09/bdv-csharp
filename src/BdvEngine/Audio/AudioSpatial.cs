using System.Numerics;

namespace BdvEngine;

/// <summary>
/// How a sound's loudness falls off with distance. Mirrors OpenAL's models exactly, so
/// <see cref="Spatial.GainAt"/> predicts what you will actually hear.
/// </summary>
public enum AudioFalloff
{
    /// <summary>No attenuation — always full volume. For UI clicks and narration that happen to be
    /// routed through a positioned source.</summary>
    None,
    /// <summary>Physically correct inverse-square-ish falloff, clamped between reference and max
    /// distance. The default, and what almost every world sound wants.</summary>
    Inverse,
    /// <summary>Straight line from full volume at reference distance to silence at max distance.
    /// Not physical, but predictable — useful when a designer needs a sound to be exactly
    /// inaudible past a known radius.</summary>
    Linear,
}

/// <summary>
/// Per-source spatial settings, and the attenuation curve that turns them into a gain.
///
/// <para><b>ReferenceDistance is the knob that matters.</b> It is the distance at which the sound
/// plays at full volume, and everything scales from it. A footstep might use 1, a waterfall 15.
/// Getting it wrong is the usual reason world audio sounds either deafening or absent — not the
/// rolloff, which is what people reach for first.</para>
/// </summary>
public struct Spatial
{
    /// <summary>Distance at which the sound is at full volume. Below this it does not get louder.</summary>
    public float ReferenceDistance;

    /// <summary>Distance at which attenuation stops (<see cref="AudioFalloff.Inverse"/>) or reaches
    /// silence (<see cref="AudioFalloff.Linear"/>).</summary>
    public float MaxDistance;

    /// <summary>How aggressively volume drops between the two. 1 is physical; higher makes a sound
    /// local, and 0 disables distance attenuation entirely.</summary>
    public float Rolloff;

    public AudioFalloff Falloff;

    public static Spatial Default => new()
    {
        ReferenceDistance = 1f,
        MaxDistance = 100f,
        Rolloff = 1f,
        Falloff = AudioFalloff.Inverse,
    };

    /// <summary>
    /// Gain multiplier at <paramref name="distance"/>, in 0..1.
    ///
    /// <para>This is the same formula OpenAL applies internally, which makes it usable for more
    /// than prediction: checking it before <see cref="AudioManager.PlayAt"/> is how you avoid
    /// spending a hardware voice on a sound nobody can hear. There are only so many sources, and a
    /// busy scene will exhaust them on inaudible ones otherwise.</para>
    /// </summary>
    public readonly float GainAt(float distance)
    {
        float reference = MathF.Max(ReferenceDistance, 1e-4f);
        float max = MathF.Max(MaxDistance, reference);
        float rolloff = MathF.Max(Rolloff, 0f);

        switch (Falloff)
        {
            case AudioFalloff.None:
                return 1f;

            case AudioFalloff.Linear:
            {
                float d = Math.Clamp(distance, reference, max);
                if (max - reference < 1e-4f) return 1f;
                return 1f - rolloff * (d - reference) / (max - reference);
            }

            default:
            {
                float d = Math.Clamp(distance, reference, max);
                return reference / (reference + rolloff * (d - reference));
            }
        }
    }
}

/// <summary>
/// Where the ears are. The engine drives this from the camera every frame unless
/// <see cref="AudioManager.AutoListenerFromCamera"/> is turned off.
///
/// <para><b>Orientation matters as much as position.</b> Panning comes from the source's direction
/// relative to the listener's forward and up vectors — a listener with a stale orientation puts
/// sounds on the wrong side of your head even when its position is perfect.</para>
/// </summary>
public struct AudioListenerState
{
    public Vector3 Position;
    public Vector3 Forward;
    public Vector3 Up;

    /// <summary>Metres per second, used only for Doppler. Left at zero, there is no pitch shift.</summary>
    public Vector3 Velocity;

    public static AudioListenerState Default => new()
    {
        Position = Vector3.Zero,
        Forward = -Vector3.UnitZ,
        Up = Vector3.UnitY,
        Velocity = Vector3.Zero,
    };
}
