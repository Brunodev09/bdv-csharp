using System.Text.Json;

namespace BdvEngine;

public sealed class StatefulAnimationBehaviorData : IBehaviorData
{
    public string Name { get; set; } = "animState";
    public string ComponentName = "";
    public Dictionary<string, int[]> States = new();
    public string DefaultState = "";
    public double FrameTime = 0.1; // seconds

    public void SetFromJson(JsonElement json)
    {
        if (json.TryGetProperty("name", out var n)) Name = n.GetString() ?? Name;
        if (json.TryGetProperty("componentName", out var cn)) ComponentName = cn.GetString() ?? "";
        if (json.TryGetProperty("defaultState", out var ds)) DefaultState = ds.GetString() ?? "";
        if (json.TryGetProperty("frameTime", out var ft)) FrameTime = ft.GetDouble();
        if (json.TryGetProperty("states", out var st))
        {
            foreach (var prop in st.EnumerateObject())
            {
                var arr = new List<int>();
                foreach (var x in prop.Value.EnumerateArray()) arr.Add(x.GetInt32());
                States[prop.Name] = arr.ToArray();
            }
        }
    }
}

public sealed class StatefulAnimationBehaviorBuilder : IBehaviorBuilder
{
    public string Type => "statefulAnimation";
    public IBehavior BuildFromJson(JsonElement json)
    {
        var d = new StatefulAnimationBehaviorData();
        d.SetFromJson(json);
        return new StatefulAnimationBehavior(d);
    }
}

public sealed class StatefulAnimationBehavior : BaseBehavior
{
    private readonly Dictionary<string, int[]> _states = new();
    private string _currentState = "";
    private readonly string _componentName;
    private AnimatedSpriteComponent? _component;
    private readonly double _frameTime;

    public StatefulAnimationBehavior(StatefulAnimationBehaviorData data) : base(data)
    {
        _componentName = data.ComponentName;
        _frameTime = data.FrameTime;
        foreach (var (k, v) in data.States) _states[k] = v;
        _currentState = data.DefaultState;
    }

    public void AddState(string name, int[] frameSequence)
    {
        _states[name] = frameSequence;
        if (_currentState == "") _currentState = name;
    }

    public void SetState(string name)
    {
        if (name == _currentState) return;
        if (!_states.ContainsKey(name)) return;
        _currentState = name;
        ResolveComponent();
        if (_component != null)
        {
            _component.Sprite.SetFrameSequence(_states[name]);
            _component.Sprite.SetFrameTime(_frameTime);
        }
    }

    public string GetState() => _currentState;

    public override void Update(double deltaTime)
    {
        if (_component == null)
        {
            ResolveComponent();
            if (_component != null && !string.IsNullOrEmpty(_currentState)
                && _states.TryGetValue(_currentState, out var seq))
            {
                _component.Sprite.SetFrameSequence(seq);
                _component.Sprite.SetFrameTime(_frameTime);
            }
        }
    }

    private void ResolveComponent()
    {
        if (_component != null) return;
        var c = _owner.GetComponent(_componentName);
        if (c is AnimatedSpriteComponent asc) _component = asc;
    }
}
