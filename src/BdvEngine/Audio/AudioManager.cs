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
            _initialized = true;
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
