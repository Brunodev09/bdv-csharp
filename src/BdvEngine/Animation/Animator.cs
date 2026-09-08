namespace BdvEngine;

/// <summary>
/// Plays <see cref="AnimationClip"/>s onto a skeleton — the runtime half of skinned animation.
/// Attach to the node that owns the clips (usually a loaded model's root); it advances a playhead
/// each frame and writes the pose into the joint nodes, which the renderer then bakes into a
/// <see cref="Skin"/> palette.
///
/// <code>
/// var hero = World.Load("assets/hero.glb").At(0, 0, 0);
/// var anim = hero.Object.GetComponent&lt;Animator&gt;()!;   // the loader attaches one if the glb has clips
/// anim.Play("Walk");
/// anim.CrossFade("Attack", 0.2f);   // blend over 0.2s, no snap
/// </code>
///
/// <para>Deliberately a plain state holder, not a state-machine graph. A graph editor is a large
/// chunk of Unity's complexity, and for this workflow a <c>switch</c> in game code both diffs and
/// reviews better — and an agent can write it.</para>
/// </summary>
public sealed class Animator : BaseComponent
{
    private readonly Dictionary<string, AnimationClip> _clips = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Clip currently playing, or null when stopped.</summary>
    public AnimationClip? Current { get; private set; }

    /// <summary>Playhead into <see cref="Current"/>, in seconds.</summary>
    public float Time { get; private set; }

    /// <summary>Playback rate. 1 = authored speed; negative plays backwards.</summary>
    public float Speed = 1f;

    /// <summary>Whether <see cref="Current"/> wraps at its end or holds the final pose.</summary>
    public bool Loop = true;

    /// <summary>True once a non-looping clip has reached its end.</summary>
    public bool Finished { get; private set; }

    // Outgoing clip during a crossfade.
    private AnimationClip? _prev;
    private float _prevTime;
    private float _fadeElapsed;
    private float _fadeDuration;

    public Animator() : base(new AnimatorData()) { }

    public IReadOnlyCollection<string> ClipNames => _clips.Keys;
    public bool Has(string clip) => _clips.ContainsKey(clip);
    public AnimationClip? Get(string clip) => _clips.GetValueOrDefault(clip);

    public void Add(AnimationClip clip) => _clips[clip.Name] = clip;

    /// <summary>Play a clip from the start, cutting instantly. Unknown names are ignored (with a
    /// warning) rather than thrown — a missing clip should not take the game down mid-frame.</summary>
    public void Play(string name, bool loop = true)
    {
        if (!_clips.TryGetValue(name, out var clip))
        {
            Console.Error.WriteLine(
                $"[anim] no clip '{name}' on '{Owner?.Name}'. Have: {string.Join(", ", _clips.Keys)}");
            return;
        }
        Play(clip, loop);
    }

    public void Play(AnimationClip clip, bool loop = true)
    {
        Current = clip;
        Time = 0f;
        Loop = loop;
        Finished = false;
        _prev = null;
        _fadeDuration = 0f;
    }

    /// <summary>Blend into a clip over <paramref name="seconds"/> instead of snapping. Re-requesting
    /// the clip already playing is a no-op, so this is safe to call every frame from state logic.</summary>
    public void CrossFade(string name, float seconds = 0.2f, bool loop = true)
    {
        if (!_clips.TryGetValue(name, out var clip))
        {
            Console.Error.WriteLine(
                $"[anim] no clip '{name}' on '{Owner?.Name}'. Have: {string.Join(", ", _clips.Keys)}");
            return;
        }
        if (ReferenceEquals(clip, Current)) return;
        if (seconds <= 0f || Current == null) { Play(clip, loop); return; }

        _prev = Current;
        _prevTime = Time;
        _fadeDuration = seconds;
        _fadeElapsed = 0f;

        Current = clip;
        Time = 0f;
        Loop = loop;
        Finished = false;
    }

    public void Stop() { Current = null; _prev = null; }

    /// <summary>Jump the playhead (clamped, or wrapped when looping) and pose immediately — for
    /// scrubbing from the editor or posing a frame in a headless capture.</summary>
    public void Seek(float seconds)
    {
        if (Current == null) return;
        Time = Wrap(seconds, Current.Duration, Loop);
        Current.Apply(Time);
    }

    public override void Update(double deltaTime)
    {
        if (Current == null) return;

        float dt = (float)deltaTime * Speed;
        Time = Advance(Time, dt, Current.Duration, Loop, ref _finishedBacking);
        Finished = _finishedBacking;

        if (_prev != null)
        {
            _fadeElapsed += MathF.Abs((float)deltaTime);
            float w = _fadeDuration > 1e-6f ? Math.Clamp(_fadeElapsed / _fadeDuration, 0f, 1f) : 1f;

            // Outgoing clip keeps running underneath, so a fade out of a walk cycle doesn't freeze
            // one leg mid-stride while the other blends.
            bool ignored = false;
            _prevTime = Advance(_prevTime, dt, _prev.Duration, true, ref ignored);

            _prev.Apply(_prevTime);
            Current.Blend(Time, w);

            if (w >= 1f) _prev = null;
        }
        else
        {
            Current.Apply(Time);
        }
    }

    private bool _finishedBacking;

    private static float Advance(float t, float dt, float duration, bool loop, ref bool finished)
    {
        if (duration <= 0f) { finished = true; return 0f; }
        t += dt;
        if (loop) return Wrap(t, duration, true);
        if (t >= duration) { finished = true; return duration; }
        if (t <= 0f) { finished = true; return 0f; }
        return t;
    }

    private static float Wrap(float t, float duration, bool loop)
    {
        if (duration <= 0f) return 0f;
        if (!loop) return Math.Clamp(t, 0f, duration);
        t %= duration;
        return t < 0f ? t + duration : t;
    }

    private sealed class AnimatorData : IComponentData
    {
        public string Name { get; set; } = "animator";
        public void SetFromJson(System.Text.Json.JsonElement json) { }
    }
}
