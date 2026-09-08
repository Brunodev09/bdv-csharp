using System.Numerics;
using System.Text.Json;
using Silk.NET.Input;

namespace BdvEngine;

public sealed class KeyboardMovementBehaviorData : IBehaviorData
{
    public string Name { get; set; } = string.Empty;
    public float Speed { get; set; } = 0.1f;

    public void SetFromJson(JsonElement json)
    {
        if (json.TryGetProperty("name", out var n)) Name = n.GetString() ?? "";
        else throw new InvalidOperationException("Name must be defined in behavior data.");
        if (json.TryGetProperty("speed", out var s)) Speed = s.GetSingle();
    }
}

public sealed class KeyboardMovementBehaviorBuilder : IBehaviorBuilder
{
    public System.Type BehaviorType => typeof(KeyboardMovementBehavior);

    public string Type => "keyboardMovement";

    public IBehavior BuildFromJson(JsonElement json)
    {
        var data = new KeyboardMovementBehaviorData();
        data.SetFromJson(json);
        return new KeyboardMovementBehavior(data);
    }
}

public sealed class KeyboardMovementBehavior : BaseBehavior
{
    public float Speed { get; set; }

    public KeyboardMovementBehavior(KeyboardMovementBehaviorData data) : base(data)
    {
        Speed = data.Speed;
    }

    public override void Update(double deltaTime)
    {
        float move = Speed * (float)deltaTime;
        var p = _owner.Transform.Position;
        if (InputManager.IsKeyDown(Key.Left))  p.X -= move;
        if (InputManager.IsKeyDown(Key.Right)) p.X += move;
        if (InputManager.IsKeyDown(Key.Up))    p.Y -= move;
        if (InputManager.IsKeyDown(Key.Down))  p.Y += move;
        _owner.Transform.Position = p;
        base.Update(deltaTime);
    }
}
