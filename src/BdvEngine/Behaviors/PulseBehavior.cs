using System.Numerics;
using System.Text.Json;

namespace BdvEngine;

public sealed class PulseBehaviorData : IBehaviorData
{
    public string Name { get; set; } = "pulse";
    public float Min = 0.9f;
    public float Max = 1.1f;
    public float Period = 1.2f;
    public float Phase = 0f;

    public void SetFromJson(JsonElement json)
    {
        if (json.TryGetProperty("name", out var n))   Name = n.GetString() ?? Name;
        if (json.TryGetProperty("min", out var mn))   Min = mn.GetSingle();
        if (json.TryGetProperty("max", out var mx))   Max = mx.GetSingle();
        if (json.TryGetProperty("period", out var p)) Period = p.GetSingle();
        if (json.TryGetProperty("phase", out var ph)) Phase = ph.GetSingle();
    }
}

public sealed class PulseBehaviorBuilder : IBehaviorBuilder
{
    public string Type => "pulse";
    public IBehavior BuildFromJson(JsonElement json)
    {
        var d = new PulseBehaviorData();
        d.SetFromJson(json);
        return new PulseBehavior(d);
    }
}

/// <summary>
/// "Breathing" highlight: drives the owner's Transform.Scale between Min and Max
/// every Period seconds via a sine wave. Captures the owner's *base* scale on first
/// update and modulates it — so the behavior plays nicely with any starting size.
/// </summary>
public sealed class PulseBehavior : BaseBehavior
{
    private readonly float _min;
    private readonly float _max;
    private readonly float _period;
    private readonly float _phase;
    private Vector3 _baseScale;
    private bool _captured;

    public bool Enabled { get; set; } = true;

    public PulseBehavior(PulseBehaviorData data) : base(data)
    {
        _min = data.Min; _max = data.Max; _period = data.Period; _phase = data.Phase;
    }

    public PulseBehavior(float min = 0.9f, float max = 1.1f, float period = 1.2f, float phase = 0f)
        : this(new PulseBehaviorData { Min = min, Max = max, Period = period, Phase = phase }) { }

    public override void Update(double deltaTime)
    {
        if (!_captured) { _baseScale = _owner.Transform.Scale; _captured = true; }
        if (!Enabled) { _owner.Transform.Scale = _baseScale; base.Update(deltaTime); return; }
        float k = Anim.Pulse(_min, _max, _period, _phase);
        _owner.Transform.Scale = _baseScale * k;
        base.Update(deltaTime);
    }
}
