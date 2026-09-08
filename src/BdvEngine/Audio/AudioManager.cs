using System.Numerics;
using Silk.NET.OpenAL;

namespace BdvEngine;

public enum AudioChannel { Sfx, Music }

public struct PlayOptions
{
    public float Volume;
    public bool Loop;
    public float Rate;
    public float? Pan;
    public AudioChannel Channel;

    public static PlayOptions Default => new() { Volume = 1f, Loop = false, Rate = 1f, Pan = null, Channel = AudioChannel.Sfx };
}

/// <summary>Options for a sound placed in the world. Unlike <see cref="PlayOptions"/> there is no
/// Pan: panning is derived from where the source sits relative to the listener, which is the whole
/// point of positioning it.</summary>
public struct Play3DOptions
{
    public float Volume;
    public bool Loop;
    public float Rate;
    public AudioChannel Channel;
    public Spatial Spatial;

    /// <summary>Metres per second, for Doppler. Zero means no pitch shift.</summary>
    public Vector3 Velocity;

    public static Play3DOptions Default => new()
    {
        Volume = 1f, Loop = false, Rate = 1f,
        Channel = AudioChannel.Sfx, Spatial = Spatial.Default, Velocity = Vector3.Zero,
    };
}

/// <summary>
/// OpenAL-backed mixer with master/sfx/music gain channels. Lazy-initialized:
/// the device opens on the first call. If no audio device is available,
/// every API becomes a graceful no-op (Play returns null).
/// </summary>
public static class AudioManager
{
    private static AL? _al;
    private static unsafe Device* _device;
    private static unsafe Context* _context;
    private static ALContext? _alc;
    private static bool _initialized;
    private static bool _failed;

    private static readonly Dictionary<string, uint> _buffers = new();
    private static readonly List<AudioHandle> _active = new();
    private static AudioHandle? _currentMusic;

    private static AudioListenerState _listener = AudioListenerState.Default;
    private static float _dopplerFactor = 1f;
    private static float _speedOfSound = 343.3f;

    /// <summary>Where the ears are. Set through <see cref="SetListener"/>.</summary>
    public static AudioListenerState Listener => _listener;

    /// <summary>Let the engine drive the listener from the camera each frame. On by default,
    /// because a camera is the right listener for the overwhelming majority of games. Turn it off
    /// when the ears belong somewhere else — a first-person body while the camera orbits, or a
    /// strategy game where the listener follows the cursor rather than the eye.</summary>
    public static bool AutoListenerFromCamera = true;

    /// <summary>Doppler strength. 0 disables the effect; 1 is physical. Only has any effect on
    /// sources and listeners that have been given a velocity.</summary>
    public static float DopplerFactor
    {
        get => _dopplerFactor;
        set { _dopplerFactor = MathF.Max(0f, value); if (_initialized) _al!.DopplerFactor(_dopplerFactor); }
    }

    /// <summary>Metres per second, in the same units as your world. Change it with
    /// <see cref="DopplerFactor"/> to exaggerate or suppress the effect.</summary>
    public static float SpeedOfSound
    {
        get => _speedOfSound;
        set { _speedOfSound = MathF.Max(1f, value); if (_initialized) _al!.SpeedOfSound(_speedOfSound); }
    }

    private static float _masterVolume = 1f;
    private static float _sfxVolume = 1f;
    private static float _musicVolume = 1f;

    public static float MasterVolume
    {
        get => _masterVolume;
        set { _masterVolume = MathF.Max(0f, value); ApplyGains(); }
    }

    public static float SfxVolume
    {
        get => _sfxVolume;
        set { _sfxVolume = MathF.Max(0f, value); ApplyGains(); }
    }

    public static float MusicVolume
    {
        get => _musicVolume;
        set { _musicVolume = MathF.Max(0f, value); ApplyGains(); }
    }

    public static unsafe void Init()
    {
        if (_initialized || _failed) return;
        try
        {
            _alc = ALContext.GetApi();
            _al = AL.GetApi();
            _device = _alc.OpenDevice("");
            if (_device == null) { _failed = true; return; }
            _context = _alc.CreateContext(_device, null);
            _alc.MakeContextCurrent(_context);
            _al.GetError(); // clear

            // Clamped inverse-distance is the model Spatial.GainAt mirrors. Setting it explicitly
            // rather than relying on the AL default is what keeps the prediction honest.
            _al.DistanceModel(DistanceModel.InverseDistanceClamped);
            _al.DopplerFactor(_dopplerFactor);
            _al.SpeedOfSound(_speedOfSound);
            _initialized = true;
            ApplyListener();
        }
        catch (Exception e)
        {
            Console.WriteLine($"AudioManager init failed: {e.Message}");
            _failed = true;
        }
    }

    public static unsafe void Shutdown()
    {
        if (!_initialized) return;
        StopAll();
        foreach (var b in _buffers.Values) _al!.DeleteBuffer(b);
        _buffers.Clear();
        _alc!.DestroyContext(_context);
        _alc.CloseDevice(_device);
        _al!.Dispose();
        _alc.Dispose();
        _al = null; _alc = null; _device = null; _context = null;
        _initialized = false;
    }

    /// <summary>Decode a WAV file from disk and upload to a buffer keyed by name.</summary>
    public static void Load(string name, string path)
    {
        Init();
        if (!_initialized) return;
        if (_buffers.ContainsKey(name)) return;

        var pcm = WavDecoder.Decode(path);
        BufferFormat fmt = (pcm.Channels, pcm.BitsPerSample) switch
        {
            (1, 8)  => BufferFormat.Mono8,
            (1, 16) => BufferFormat.Mono16,
            (2, 8)  => BufferFormat.Stereo8,
            (2, 16) => BufferFormat.Stereo16,
            _ => throw new InvalidDataException($"Unsupported WAV format ({pcm.Channels}ch, {pcm.BitsPerSample}-bit)"),
        };

        uint buf = _al!.GenBuffer();
        unsafe
        {
            fixed (byte* p = pcm.Data)
                _al.BufferData(buf, fmt, p, pcm.Data.Length, pcm.SampleRate);
        }
        _buffers[name] = buf;
    }

    /// <summary>Move the ears. Call every frame while the camera moves; the engine does this for
    /// you unless <see cref="AutoListenerFromCamera"/> is off.</summary>
    public static void SetListener(Vector3 position, Vector3 forward, Vector3 up,
                                   Vector3 velocity = default)
    {
        _listener.Position = position;
        _listener.Forward = forward.LengthSquared() > 1e-8f ? Vector3.Normalize(forward) : -Vector3.UnitZ;
        _listener.Up = up.LengthSquared() > 1e-8f ? Vector3.Normalize(up) : Vector3.UnitY;
        _listener.Velocity = velocity;
        ApplyListener();
    }

    private static unsafe void ApplyListener()
    {
        if (!_initialized) return;
        var p = _listener.Position;
        var v = _listener.Velocity;
        _al!.SetListenerProperty(ListenerVector3.Position, p.X, p.Y, p.Z);
        _al.SetListenerProperty(ListenerVector3.Velocity, v.X, v.Y, v.Z);

        // Orientation is six floats: forward then up, in that order. Passing them separately or
        // in the wrong order mirrors the stereo image, which is maddening to diagnose by ear.
        var o = stackalloc float[6]
        {
            _listener.Forward.X, _listener.Forward.Y, _listener.Forward.Z,
            _listener.Up.X, _listener.Up.Y, _listener.Up.Z,
        };
        _al.SetListenerProperty(ListenerFloatArray.Orientation, o);
    }

    /// <summary>
    /// Play a sound at a world position, attenuated and panned by where it is relative to the
    /// listener.
    ///
    /// <para>Returns null when there is no audio device or the clip isn't loaded — the same
    /// graceful no-op as <see cref="Play"/>, so audio never takes a game down.</para>
    /// </summary>
    public static AudioHandle? PlayAt(string name, Vector3 position, Play3DOptions options = default)
    {
        if (options.Equals(default(Play3DOptions))) options = Play3DOptions.Default;
        if (options.Rate == 0f) options.Rate = 1f;
        if (options.Volume == 0f && !options.Loop) options.Volume = 1f;
        if (options.Spatial.ReferenceDistance == 0f && options.Spatial.MaxDistance == 0f)
            options.Spatial = Spatial.Default;

        Init();
        if (!_initialized || !_buffers.TryGetValue(name, out uint buf)) return null;

        uint src = _al!.GenSource();
        _al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(src, SourceBoolean.Looping, options.Loop);
        _al.SetSourceProperty(src, SourceFloat.Pitch, MathF.Max(0.01f, options.Rate));
        _al.SetSourceProperty(src, SourceFloat.Gain, options.Volume * ChannelGain(options.Channel) * _masterVolume);

        // The one line that separates world audio from head audio: a relative source ignores the
        // listener entirely and always plays dead centre.
        _al.SetSourceProperty(src, SourceBoolean.SourceRelative, false);

        var handle = new AudioHandle(_al, src, options.Channel);
        handle.SetPosition(position);
        handle.SetVelocity(options.Velocity);
        handle.SetSpatial(options.Spatial);

        _al.SourcePlay(src);
        _active.Add(handle);
        return handle;
    }

    public static AudioHandle? Play(string name, PlayOptions options = default)
    {
        if (options.Equals(default(PlayOptions))) options = PlayOptions.Default;
        if (options.Volume == 0f && !options.Loop) options.Volume = 1f;
        if (options.Rate == 0f) options.Rate = 1f;

        Init();
        if (!_initialized || !_buffers.TryGetValue(name, out uint buf)) return null;

        uint src = _al!.GenSource();
        _al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(src, SourceBoolean.Looping, options.Loop);
        _al.SetSourceProperty(src, SourceFloat.Pitch, MathF.Max(0.01f, options.Rate));
        _al.SetSourceProperty(src, SourceFloat.Gain, options.Volume * ChannelGain(options.Channel) * _masterVolume);
        _al.SetSourceProperty(src, SourceBoolean.SourceRelative, true);
        if (options.Pan is float pan)
        {
            pan = Math.Clamp(pan, -1f, 1f);
            _al.SetSourceProperty(src, SourceVector3.Position, pan, 0f, -MathF.Sqrt(MathF.Max(0f, 1f - pan * pan)));
        }
        else
        {
            _al.SetSourceProperty(src, SourceVector3.Position, 0f, 0f, 0f);
        }

        _al.SourcePlay(src);

        var handle = new AudioHandle(_al, src, options.Channel);
        _active.Add(handle);
        return handle;
    }

    /// <summary>Stop the current music (if any) and start a looping track on the music channel.</summary>
    public static AudioHandle? PlayMusic(string name, float volume = 1f)
    {
        _currentMusic?.Stop();
        var opts = PlayOptions.Default;
        opts.Loop = true; opts.Volume = volume; opts.Channel = AudioChannel.Music;
        var h = Play(name, opts);
        _currentMusic = h;
        return h;
    }

    public static void StopMusic()
    {
        _currentMusic?.Stop();
        _currentMusic = null;
    }

    public static void StopAll()
    {
        foreach (var h in _active.ToArray()) h.Stop();
        _active.Clear();
        _currentMusic = null;
    }

    /// <summary>Reap finished sources. Call once per frame.</summary>
    public static void Update()
    {
        if (!_initialized) return;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var h = _active[i];
            if (h.Stopped || !h.IsPlaying())
            {
                if (!h.Stopped) h.Stop();
                if (_currentMusic == h) _currentMusic = null;
                _active.RemoveAt(i);
            }
        }
    }

    private static float ChannelGain(AudioChannel ch) =>
        ch == AudioChannel.Music ? _musicVolume : _sfxVolume;

    private static void ApplyGains()
    {
        if (!_initialized) return;
        foreach (var h in _active)
        {
            if (h.Stopped) continue;
            // We don't know the per-instance volume after the fact, so just rescale to the
            // channel*master product. Callers can override with handle.SetVolume if needed.
            h.SetVolume(ChannelGain(h.Channel) * _masterVolume);
        }
    }
}
