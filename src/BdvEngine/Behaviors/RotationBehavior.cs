using System.Numerics;
using System.Text.Json;

namespace BdvEngine;

public sealed class RotationBehaviorData : IBehaviorData
{
    public string Name { get; set; } = string.Empty;
    public Vector3 Rotation = Vector3.Zero;

    public void SetFromJson(JsonElement json)
    {
        if (json.TryGetProperty("name", out var n)) Name = n.GetString() ?? "";
        else throw new InvalidOperationException("Name must be defined.");
        if (json.TryGetProperty("rotation", out var r))
        {
            float x = r.TryGetProperty("x", out var xe) ? xe.GetSingle() : 0;
            float y = r.TryGetProperty("y", out var ye) ? ye.GetSingle() : 0;
            float z = r.TryGetProperty("z", out var ze) ? ze.GetSingle() : 0;
            Rotation = new Vector3(x, y, z);
        }
    }
}

public sealed class RotationBehaviorBuilder : IBehaviorBuilder
{
    public System.Type BehaviorType => typeof(RotationBehavior);

    public string Type => "rotation";

    public IBehavior BuildFromJson(JsonElement json)
    {
        var data = new RotationBehaviorData();
        data.SetFromJson(json);
        return new RotationBehavior(data);
    }
}

public sealed class RotationBehavior : BaseBehavior
{
    /// <summary>Radians per second about each axis. Public (not a private copy of the data) so the
    /// inspector can retune it live and the scene serialiser saves what you actually see.</summary>
    public Vector3 Rotation;

    public RotationBehavior(RotationBehaviorData data) : base(data) => Rotation = data.Rotation;

    public override void Update(double deltaTime)
    {
        _owner.Transform.Rotation += Rotation * (float)deltaTime;
        base.Update(deltaTime);
    }
}
