using System.Text.Json;

namespace BdvEngine;

public sealed class RayCastBehaviorData : IBehaviorData
{
    public string Name { get; set; } = "rayCast";
    public Color RayColor = new(255, 255, 0, 150);
    public Color HitColor = new(255, 0, 0, 255);
    public float HitRadius = 5f;

    public void SetFromJson(JsonElement json)
    {
        if (json.TryGetProperty("name", out var n)) Name = n.GetString() ?? Name;
    }
}

public sealed class RayCastBehaviorBuilder : IBehaviorBuilder
{
    public string Type => "rayCast";
    public IBehavior BuildFromJson(JsonElement json)
    {
        var d = new RayCastBehaviorData();
        d.SetFromJson(json);
        return new RayCastBehavior(d);
    }
}

public sealed class RayCastBehavior : BaseBehavior
{
    public float TargetX, TargetY;
    public float HitX, HitY;
    public bool HasHit;

    private readonly Color _rayColor;
    private readonly Color _hitColor;
    private readonly float _hitRadius;

    public RayCastBehavior(RayCastBehaviorData data) : base(data)
    {
        _rayColor = data.RayColor;
        _hitColor = data.HitColor;
        _hitRadius = data.HitRadius;
    }

    public override void Update(double deltaTime)
    {
        var pos = _owner.Transform.Position;
        float dx = TargetX - pos.X, dy = TargetY - pos.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len == 0f) return;
        dx /= len; dy /= len;

        HasHit = false;
        float closestT = float.PositiveInfinity;
        var ownRb = _owner.GetBehavior<RigidBodyBehavior>();

        foreach (var body in RigidBodyBehavior.AllBodies)
        {
            if (body == ownRb) continue;
            if (body.Collider == null) continue;
            var col = body.Collider;
            float t = -1f;

            if (col.Shape == ColliderShape.Rect)
            {
                var r = col.GetWorldRect();
                t = Collision.RayRect(pos.X, pos.Y, dx, dy, r.X, r.Y, r.W, r.H);
            }
            else
            {
                var c = col.GetWorldCircle();
                float ox = pos.X - c.X, oy = pos.Y - c.Y;
                float a = dx * dx + dy * dy;
                float b2 = ox * dx + oy * dy;
                float cc = ox * ox + oy * oy - c.R * c.R;
                float disc = b2 * b2 - a * cc;
                if (disc >= 0f)
                {
                    t = (-b2 - MathF.Sqrt(disc)) / a;
                    if (t < 0f) t = (-b2 + MathF.Sqrt(disc)) / a;
                }
            }

            if (t >= 0f && t < closestT)
            {
                closestT = t;
                HitX = pos.X + dx * t;
                HitY = pos.Y + dy * t;
                HasHit = true;
            }
        }
    }

    public override void Render(Shader shader)
    {
        var pos = _owner.Transform.Position;
        if (HasHit)
        {
            Draw.Line(pos.X, pos.Y, HitX, HitY, _rayColor);
            Draw.Circle(HitX, HitY, _hitRadius, _hitColor);
        }
        else
        {
            Draw.Line(pos.X, pos.Y, TargetX, TargetY,
                new Color(_rayColor.R, _rayColor.G, _rayColor.B, 50));
        }
    }
}
