using System.Numerics;
using System.Text.Json;

namespace BdvEngine;

public sealed class RigidBodyBehaviorData : IBehaviorData
{
    public string Name { get; set; } = "rigidBody";
    public float Vx = 0, Vy = 0;
    public float Gravity = 0;
    public float BounceDamping = 0.7f;
    public float Friction = 0.99f;
    public bool Kinematic = false;

    public void SetFromJson(JsonElement json)
    {
        if (json.TryGetProperty("name", out var n)) Name = n.GetString() ?? Name;
        if (json.TryGetProperty("vx", out var v)) Vx = v.GetSingle();
        if (json.TryGetProperty("vy", out var v2)) Vy = v2.GetSingle();
        if (json.TryGetProperty("gravity", out var g)) Gravity = g.GetSingle();
        if (json.TryGetProperty("bounceDamping", out var bd)) BounceDamping = bd.GetSingle();
        if (json.TryGetProperty("friction", out var f)) Friction = f.GetSingle();
        if (json.TryGetProperty("kinematic", out var k)) Kinematic = k.GetBoolean();
    }
}

public sealed class RigidBodyBehaviorBuilder : IBehaviorBuilder
{
    public string Type => "rigidBody";

    public IBehavior BuildFromJson(JsonElement json)
    {
        var data = new RigidBodyBehaviorData();
        data.SetFromJson(json);
        return new RigidBodyBehavior(data);
    }
}

public sealed class RigidBodyBehavior : BaseBehavior
{
    public float Vx, Vy;
    public float Gravity;
    public float BounceDamping;
    public float Friction;
    public bool Kinematic;
    public ColliderComponent? Collider;

    public static List<RigidBodyBehavior> AllBodies = new();
    private static readonly HashSet<string> _resolvedPairs = new();

    public static void BeginFrame() => _resolvedPairs.Clear();
    public static void ClearAll() => AllBodies.Clear();

    public RigidBodyBehavior(RigidBodyBehaviorData data) : base(data)
    {
        Vx = data.Vx;
        Vy = data.Vy;
        Gravity = data.Gravity;
        BounceDamping = data.BounceDamping;
        Friction = data.Friction;
        Kinematic = data.Kinematic;
    }

    public override void SetOwner(SimObject owner)
    {
        base.SetOwner(owner);
        AllBodies.Add(this);
    }

    public override void Update(double deltaTime)
    {
        float t = (float)deltaTime;
        if (Collider == null)
        {
            if (_owner.GetComponent("collider") is ColliderComponent c) Collider = c;
            if (Collider == null) return;
        }

        if (Collider.IsStatic) return;

        var pos = _owner.Transform.Position;
        if (!Kinematic)
        {
            Vy += Gravity * t;
            pos.X += Vx * t;
            pos.Y += Vy * t;
        }
        _owner.Transform.Position = pos;

        int myIdx = AllBodies.IndexOf(this);
        for (int i = 0; i < AllBodies.Count; i++)
        {
            var other = AllBodies[i];
            if (other == this || other.Collider == null) continue;
            if (!other.Collider.IsStatic && !other.Kinematic)
            {
                int lo = Math.Min(myIdx, i), hi = Math.Max(myIdx, i);
                string key = $"{lo}:{hi}";
                if (!_resolvedPairs.Add(key)) continue;
            }
            ResolveCollision(other);
        }

        // Hard clamp pass against statics
        pos = _owner.Transform.Position;
        foreach (var other in AllBodies)
        {
            if (other == this || other.Collider == null || !other.Collider.IsStatic) continue;
            if (Collider.Shape == ColliderShape.Rect && other.Collider.Shape == ColliderShape.Rect)
            {
                var a = Collider.GetWorldRect();
                var b = other.Collider.GetWorldRect();
                var ov = Collision.RectOverlap(a.X, a.Y, a.W, a.H, b.X, b.Y, b.W, b.H);
                if (ov.HasValue)
                {
                    pos.X += ov.Value.X;
                    pos.Y += ov.Value.Y;
                    if (ov.Value.X != 0) Vx = 0;
                    if (ov.Value.Y != 0) Vy = 0;
                }
            }
        }
        _owner.Transform.Position = pos;
    }

    private void ResolveCollision(RigidBodyBehavior other)
    {
        var myCol = Collider!;
        var otherCol = other.Collider!;
        var myPos = _owner.Transform.Position;
        var otherPos = other._owner.Transform.Position;
        bool isStatic = otherCol.IsStatic || other.Kinematic;

        if (myCol.Shape == ColliderShape.Rect && otherCol.Shape == ColliderShape.Rect)
        {
            var a = myCol.GetWorldRect();
            var b = otherCol.GetWorldRect();
            var ov = Collision.RectOverlap(a.X, a.Y, a.W, a.H, b.X, b.Y, b.W, b.H);
            if (ov.HasValue)
            {
                if (isStatic)
                {
                    myPos.X += ov.Value.X;
                    myPos.Y += ov.Value.Y;
                    if (ov.Value.X > 0 && Vx < 0) Vx = -Vx * BounceDamping;
                    else if (ov.Value.X < 0 && Vx > 0) Vx = -Vx * BounceDamping;
                    if (ov.Value.Y > 0 && Vy < 0) { Vy = -Vy * BounceDamping; Vx *= Friction; }
                    else if (ov.Value.Y < 0 && Vy > 0) { Vy = -Vy * BounceDamping; Vx *= Friction; }
                }
                else
                {
                    myPos.X += ov.Value.X / 2;
                    myPos.Y += ov.Value.Y / 2;
                    otherPos.X -= ov.Value.X / 2;
                    otherPos.Y -= ov.Value.Y / 2;
                    if (MathF.Abs(ov.Value.X) > MathF.Abs(ov.Value.Y))
                    { (Vx, other.Vx) = (other.Vx, Vx); }
                    else
                    { (Vy, other.Vy) = (other.Vy, Vy); }
                }
            }
        }
        else if (myCol.Shape == ColliderShape.Circle && otherCol.Shape == ColliderShape.Circle)
        {
            var a = myCol.GetWorldCircle();
            var b = otherCol.GetWorldCircle();
            if (Collision.CircleCircle(a.X, a.Y, a.R, b.X, b.Y, b.R))
            {
                float dx = b.X - a.X, dy = b.Y - a.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist == 0f) dist = 1f;
                float nx = dx / dist, ny = dy / dist;
                float pen = a.R + b.R - dist;
                if (isStatic)
                {
                    myPos.X -= nx * pen;
                    myPos.Y -= ny * pen;
                    float dot = Vx * nx + Vy * ny;
                    Vx -= 2 * dot * nx * BounceDamping;
                    Vy -= 2 * dot * ny * BounceDamping;
                }
                else
                {
                    myPos.X -= nx * pen / 2;
                    myPos.Y -= ny * pen / 2;
                    otherPos.X += nx * pen / 2;
                    otherPos.Y += ny * pen / 2;
                    float aVn = Vx * nx + Vy * ny;
                    float bVn = other.Vx * nx + other.Vy * ny;
                    Vx += (bVn - aVn) * nx;
                    Vy += (bVn - aVn) * ny;
                    other.Vx += (aVn - bVn) * nx;
                    other.Vy += (aVn - bVn) * ny;
                }
            }
        }
        else if (myCol.Shape == ColliderShape.Circle && otherCol.Shape == ColliderShape.Rect)
        {
            var c = myCol.GetWorldCircle();
            var r = otherCol.GetWorldRect();
            if (Collision.CircleRect(c.X, c.Y, c.R, r.X, r.Y, r.W, r.H))
            {
                float nearestX = MathF.Max(r.X, MathF.Min(c.X, r.X + r.W));
                float nearestY = MathF.Max(r.Y, MathF.Min(c.Y, r.Y + r.H));
                float dx = c.X - nearestX, dy = c.Y - nearestY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist == 0f) dist = 1f;
                float nx = dx / dist, ny = dy / dist;
                float pen = c.R - dist;
                myPos.X += nx * pen;
                myPos.Y += ny * pen;
                float dot = Vx * nx + Vy * ny;
                float damp = isStatic ? BounceDamping : 1f;
                Vx -= 2 * dot * nx * damp;
                Vy -= 2 * dot * ny * damp;
            }
        }
        else if (myCol.Shape == ColliderShape.Rect && otherCol.Shape == ColliderShape.Circle)
        {
            var r = myCol.GetWorldRect();
            var c = otherCol.GetWorldCircle();
            if (Collision.CircleRect(c.X, c.Y, c.R, r.X, r.Y, r.W, r.H))
            {
                var ov = Collision.RectOverlap(r.X, r.Y, r.W, r.H,
                    c.X - c.R, c.Y - c.R, c.R * 2, c.R * 2);
                if (ov.HasValue)
                {
                    myPos.X += ov.Value.X;
                    myPos.Y += ov.Value.Y;
                    if (isStatic)
                    {
                        if (ov.Value.X != 0f) Vx = -Vx * BounceDamping;
                        if (ov.Value.Y != 0f) Vy = -Vy * BounceDamping;
                    }
                    else
                    {
                        if (ov.Value.X != 0f) (Vx, other.Vx) = (other.Vx, Vx);
                        if (ov.Value.Y != 0f) (Vy, other.Vy) = (other.Vy, Vy);
                    }
                }
            }
        }

        _owner.Transform.Position = myPos;
        other._owner.Transform.Position = otherPos;
    }
}
