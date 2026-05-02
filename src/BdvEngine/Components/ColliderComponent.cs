using System.Text.Json;

namespace BdvEngine;

public enum ColliderShape { Rect, Circle }

public readonly record struct WorldRect(float X, float Y, float W, float H);
public readonly record struct WorldCircle(float X, float Y, float R);

public sealed class ColliderComponentData : IComponentData
{
    public string Name { get; set; } = "collider";
    public ColliderShape Shape = ColliderShape.Rect;
    public float Width = 50;
    public float Height = 50;
    public float Radius = 25;
    public bool IsStatic = false;
    public Color Color = Color.White;
    public bool DebugDraw = true;

    public void SetFromJson(JsonElement json)
    {
        if (json.TryGetProperty("name", out var n)) Name = n.GetString() ?? Name;
        if (json.TryGetProperty("shape", out var s))
            Shape = s.GetString() == "circle" ? ColliderShape.Circle : ColliderShape.Rect;
        if (json.TryGetProperty("width", out var w)) Width = w.GetSingle();
        if (json.TryGetProperty("height", out var h)) Height = h.GetSingle();
        if (json.TryGetProperty("radius", out var r)) Radius = r.GetSingle();
        if (json.TryGetProperty("isStatic", out var st)) IsStatic = st.GetBoolean();
    }
}

public sealed class ColliderComponentBuilder : IComponentBuilder
{
    public string Type => "collider";

    public IComponent BuildFromJson(JsonElement json)
    {
        var data = new ColliderComponentData();
        data.SetFromJson(json);
        return new ColliderComponent(data);
    }
}

public sealed class ColliderComponent : BaseComponent
{
    public ColliderShape Shape;
    public float Width;
    public float Height;
    public float Radius;
    public bool IsStatic;
    public Color Color;
    public bool DebugDraw;

    public ColliderComponent(ColliderComponentData data) : base(data)
    {
        Shape = data.Shape;
        Width = data.Width;
        Height = data.Height;
        Radius = data.Radius;
        IsStatic = data.IsStatic;
        Color = data.Color;
        DebugDraw = data.DebugDraw;
    }

    public WorldRect GetWorldRect()
    {
        var p = _owner.Transform.Position;
        return new WorldRect(p.X - Width / 2f, p.Y - Height / 2f, Width, Height);
    }

    public WorldCircle GetWorldCircle()
    {
        var p = _owner.Transform.Position;
        return new WorldCircle(p.X, p.Y, Radius);
    }

    public override void Render(Shader shader)
    {
        if (!DebugDraw) return;
        var p = _owner.Transform.Position;
        if (Shape == ColliderShape.Rect)
            Draw.Rect(p.X - Width / 2f, p.Y - Height / 2f, Width, Height, Color);
        else
            Draw.Circle(p.X, p.Y, Radius, Color);
    }
}
