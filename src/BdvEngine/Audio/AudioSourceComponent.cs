using System.Numerics;
using System.Text.Json;

namespace BdvEngine;

/// <summary>
/// A sound attached to a <see cref="SimObject"/>. It follows the object's world transform, so a
/// looping river or a chased enemy's footsteps stay where the object is without the game tracking
/// the handle itself.
///
/// <code>
/// var falls = new SimObject(w.NextId(), "waterfall");
/// falls.Transform.Position = new Vector3(14, 2, -30);
/// falls.AddComponent(new AudioSourceComponent
/// {
///     Clip = "water", Loop = true, PlayOnLoad = true,
///     ReferenceDistance = 6f, MaxDistance = 60f,
/// });
/// w.Add(falls);
/// </code>
///
/// <para><b>Velocity is derived, not declared.</b> The component measures how far its owner moved
/// since the last frame and feeds that to OpenAL, so Doppler works on anything that moves — a
/// physics body, an animated node, a hand-tweened platform — without the game reporting speeds it
/// would otherwise have no reason to compute.</para>
///
/// <para>With no audio device, or a clip that was never loaded, every method is a no-op. Audio
/// never takes a game down.</para>
/// </summary>
public sealed class AudioSourceComponent : BaseComponent
{
    /// <summary>Clip name as registered with <see cref="AudioManager.Load"/>.</summary>
    public string Clip = "";

    [Range(0f, 4f)] public float Volume = 1f;
    [Range(0.05f, 4f)] public float Pitch = 1f;
    public bool Loop;

    /// <summary>Start as soon as the object loads. The natural setting for ambience; leave it off
    /// for anything the game triggers.</summary>
    public bool PlayOnLoad;

    /// <summary>False plays the clip head-relative, ignoring position entirely — for narration or
    /// UI that happens to live on a scene object.</summary>
    public bool Spatialised = true;

    /// <summary>Distance at which the clip is at full volume. See <see cref="Spatial"/>: this is
    /// the knob that decides whether world audio sounds right.</summary>
    [Range(0.01f, 200f)] public float ReferenceDistance = 1f;

    [Range(0.1f, 2000f)] public float MaxDistance = 100f;
    [Range(0f, 10f)] public float Rolloff = 1f;
    public AudioFalloff Falloff = AudioFalloff.Inverse;
    public AudioChannel Channel = AudioChannel.Sfx;

    private AudioHandle? _handle;
    private Vector3 _lastPosition;
    private bool _hasLastPosition;

    /// <summary>The live handle while playing, or null. Use it for one-off tweaks (a fade, a rate
    /// change) that don't warrant a field here.</summary>
    public AudioHandle? Handle => _handle;

    public bool IsPlaying => _handle?.IsPlaying() ?? false;

    /// <summary>World position the sound is emitted from — the owner's, or the origin if detached.</summary>
    public Vector3 WorldPosition => _owner?.WorldMatrix.Translation ?? Vector3.Zero;

    /// <summary>Current spatial settings as a <see cref="Spatial"/>, so
    /// <see cref="Spatial.GainAt"/> can predict this source's audibility.</summary>
    public Spatial SpatialSettings => new()
    {
        ReferenceDistance = ReferenceDistance,
        MaxDistance = MaxDistance,
        Rolloff = Rolloff,
        Falloff = Falloff,
    };

    /// <summary>Gain this source would have at the listener right now, 0..1. Cheap, and worth
    /// checking before starting a sound in a crowded scene — hardware voices are finite.</summary>
    public float AudibleGain
        => SpatialSettings.GainAt(Vector3.Distance(WorldPosition, AudioManager.Listener.Position));

    public AudioSourceComponent() : base(new AudioSourceData()) { }

    public override void Load()
    {
        if (PlayOnLoad) Play();
    }

    /// <summary>Start (or restart) the clip.</summary>
    public void Play()
    {
        Stop();
        if (string.IsNullOrEmpty(Clip)) return;

        if (!Spatialised)
        {
            var flat = PlayOptions.Default;
            flat.Volume = Volume;
            flat.Loop = Loop;
            flat.Rate = Pitch;
            flat.Channel = Channel;
            _handle = AudioManager.Play(Clip, flat);
            return;
        }

        var opts = Play3DOptions.Default;
        opts.Volume = Volume;
        opts.Loop = Loop;
        opts.Rate = Pitch;
        opts.Channel = Channel;
        opts.Spatial = SpatialSettings;
        _handle = AudioManager.PlayAt(Clip, WorldPosition, opts);

        _lastPosition = WorldPosition;
        _hasLastPosition = true;
    }

    public void Stop()
    {
        _handle?.Stop();
        _handle = null;
        _hasLastPosition = false;
    }

    public override void Unload() => Stop();

    public override void Update(double deltaTime)
    {
        if (_handle == null || !Spatialised) return;
        if (_handle.Stopped) { _handle = null; return; }

        var p = WorldPosition;

        // Derive velocity from movement rather than asking the game for it. dt can be 0 on the
        // first frame or a paused step; dividing by it would produce an infinite velocity and a
        // Doppler shift that sounds like a scream.
        if (_hasLastPosition && deltaTime > 1e-6)
            _handle.SetVelocity((p - _lastPosition) / (float)deltaTime);

        _handle.SetPosition(p);
        _lastPosition = p;
        _hasLastPosition = true;
    }

    private sealed class AudioSourceData : IComponentData
    {
        public string Name { get; set; } = "audio";
        public void SetFromJson(JsonElement json) { }
    }
}

/// <summary>Registers <see cref="AudioSourceComponent"/> with the component registry, so placed
/// ambience serialises into a <c>.scene.json</c> through the generic field bridge — every field is
/// a supported type, so no bespoke read/write path is needed.</summary>
public sealed class AudioSourceComponentBuilder : IComponentBuilder
{
    public System.Type ComponentType => typeof(AudioSourceComponent);

    public string Type => "audio";

    public IComponent BuildFromJson(JsonElement json) => new AudioSourceComponent();
}
