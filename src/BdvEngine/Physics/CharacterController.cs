using System.Numerics;

namespace BdvEngine;

/// <summary>
/// A capsule character that walks, slides along walls, climbs slopes and steps, and falls under
/// gravity. Attach to a <see cref="SimObject"/> alongside a <see cref="CapsuleCollider"/>; call
/// <see cref="Move"/> each frame with the desired horizontal velocity.
///
/// <code>
/// var body = new CapsuleCollider(radius: 0.35f, height: 1.8f, center: new Vector3(0, 0.9f, 0));
/// player.AddComponent(body);
/// var cc = new CharacterController(body);
/// player.AddComponent(cc);
/// // ...each frame:
/// cc.Move(new Vector3(inputX, 0, inputZ) * 5f, dt);
/// if (cc.IsGrounded &amp;&amp; jumpPressed) cc.Jump(5f);
/// </code>
///
/// <para><b>Kinematic, not a rigid body.</b> It resolves penetration by moving out of it rather
/// than by accumulating forces. That is what makes character movement feel controlled instead of
/// bouncy, and it is why every engine ships a character controller separate from its physics
/// bodies.</para>
/// </summary>
public sealed class CharacterController : BaseComponent
{
    private readonly CapsuleCollider _capsule;

    /// <summary>Downward acceleration, world units/s². Negative is down.</summary>
    public float Gravity = -22f;

    /// <summary>Steepest surface that counts as ground, in degrees. Anything steeper is a wall:
    /// the character slides down it instead of standing on it.</summary>
    public float SlopeLimitDegrees = 50f;

    /// <summary>Tallest lip the character walks over without jumping. Without this a capsule stops
    /// dead at every kerb and terrain seam.</summary>
    public float StepOffset = 0.35f;

    /// <summary>Gap kept between the capsule and everything else. A character resolved to exactly
    /// zero distance re-collides next frame on floating-point noise and jitters.</summary>
    public float SkinWidth = 0.02f;

    /// <summary>Which layers block movement.</summary>
    public int CollisionMask = ~0;

    /// <summary>Max depenetration passes per move. Three handles a corner (two walls plus the
    /// floor); more is wasted work in all but pathological geometry.</summary>
    public int MaxResolveIterations = 3;

    /// <summary>True when standing on a surface no steeper than <see cref="SlopeLimitDegrees"/>.</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>Upward normal of the ground under the character; <c>UnitY</c> when airborne.</summary>
    public Vector3 GroundNormal { get; private set; } = Vector3.UnitY;

    /// <summary>Current vertical speed. Positive is up; reset on landing.</summary>
    public float VerticalVelocity { get; private set; }

    /// <summary>True on the frame a collision stopped horizontal movement — useful for footstep
    /// sounds, or to know a path is blocked.</summary>
    public bool HitWall { get; private set; }

    public CharacterController(CapsuleCollider capsule) : base(new ControllerData())
        => _capsule = capsule;

    public CapsuleCollider Capsule => _capsule;

    /// <summary>Launch upward. Ignored unless grounded, so holding jump can't fly.</summary>
    public void Jump(float speed)
    {
        if (!IsGrounded) return;
        VerticalVelocity = speed;
        IsGrounded = false;
    }

    /// <summary>Overwrite vertical speed directly — for launchers, knockback, or a double jump the
    /// game implements itself.</summary>
    public void SetVerticalVelocity(float v) => VerticalVelocity = v;

    /// <summary>
    /// Move by <paramref name="horizontalVelocity"/> (units/s, Y ignored) for
    /// <paramref name="dt"/> seconds, applying gravity and resolving collisions.
    ///
    /// <para>Horizontal and vertical are resolved separately. Doing them together makes a character
    /// walking into a wall also lose its footing, because one combined push has to answer two
    /// different questions.</para>
    /// </summary>
    public void Move(Vector3 horizontalVelocity, double dt)
    {
        if (Owner == null) return;
        float step = (float)dt;
        if (step <= 0f) return;

        HitWall = false;
        var t = Owner.Transform;

        // ── horizontal ──
        var wish = new Vector3(horizontalVelocity.X, 0f, horizontalVelocity.Z) * step;
        if (wish.LengthSquared() > 1e-12f)
        {
            var before = t.Position;
            t.Position += wish;
            Owner.RebakeMatrices();

            if (ResolveOverlaps(out var pushed))
            {
                // Only the horizontal part of the push is a wall. Letting the vertical part through
                // here would launch the character up ramps at walking speed.
                var flat = new Vector3(pushed.X, 0f, pushed.Z);
                if (flat.LengthSquared() > 1e-8f) HitWall = true;

                // Step-up: if the blockage is short enough to walk over, lift and retry once.
                if (HitWall && StepOffset > 0f && TryStepUp(before, wish)) HitWall = false;
            }
        }

        // ── vertical ──
        VerticalVelocity += Gravity * step;
        t.Position += new Vector3(0f, VerticalVelocity * step, 0f);
        Owner.RebakeMatrices();

        bool wasGrounded = IsGrounded;
        IsGrounded = false;
        GroundNormal = Vector3.UnitY;

        if (ResolveOverlaps(out var vpush))
        {
            if (vpush.Y > 0.001f)
            {
                // Pushed upward: we landed on something.
                var n = Vector3.Normalize(vpush);
                float slopeDeg = MathF.Acos(Math.Clamp(n.Y, -1f, 1f)) * 180f / MathF.PI;
                if (slopeDeg <= SlopeLimitDegrees)
                {
                    IsGrounded = true;
                    GroundNormal = n;
                    if (VerticalVelocity < 0f) VerticalVelocity = 0f;
                }
            }
            else if (vpush.Y < -0.001f && VerticalVelocity > 0f)
            {
                VerticalVelocity = 0f;   // clipped a ceiling
            }
        }

        // Ground probe: catch the case where the character is a hair above the floor, so walking
        // down a gentle slope doesn't alternate grounded/airborne every frame.
        if (!IsGrounded && VerticalVelocity <= 0f) ProbeGround(wasGrounded);
    }

    /// <summary>Lift by <see cref="StepOffset"/>, retry the move, and settle back down. Returns
    /// true if the character got past the obstruction.</summary>
    private bool TryStepUp(Vector3 originalPosition, Vector3 wish)
    {
        var t = Owner!.Transform;
        var saved = t.Position;

        t.Position = originalPosition + new Vector3(0f, StepOffset, 0f) + wish;
        Owner.RebakeMatrices();

        if (ResolveOverlaps(out var push) && (MathF.Abs(push.X) > 1e-4f || MathF.Abs(push.Z) > 1e-4f))
        {
            t.Position = saved;             // still blocked up there — it was a wall, not a step
            Owner.RebakeMatrices();
            return false;
        }

        // Settle onto whatever we stepped onto — but only if the descent doesn't put us back
        // inside the obstacle. Mid-climb the character is still horizontally overlapping the step's
        // face, and snapping down there would be pushed straight back out, cancelling the frame's
        // progress and leaving it stuck at the lip forever.
        var (a, _) = _capsule.WorldSegment();
        if (PhysicsWorld.Raycast(a, -Vector3.UnitY, StepOffset + _capsule.WorldRadius + 0.1f,
                                 out var hit, CollisionMask, _capsule))
            TrySnapDown(hit.Point.Y + _capsule.WorldRadius + SkinWidth - a.Y);
        return true;
    }

    /// <summary>Push the capsule out of everything it overlaps. <paramref name="totalPush"/> is the
    /// accumulated correction, whose direction tells the caller what it hit.</summary>
    private bool ResolveOverlaps(out Vector3 totalPush)
    {
        totalPush = Vector3.Zero;
        bool any = false;

        for (int iter = 0; iter < MaxResolveIterations; iter++)
        {
            var (a, b) = _capsule.WorldSegment();
            float r = _capsule.WorldRadius + SkinWidth;
            var candidates = PhysicsWorld.OverlapCapsule(a, b, r, CollisionMask, _capsule,
                                                         includeTriggers: false);
            if (candidates.Count == 0) break;

            var push = Vector3.Zero;
            foreach (var c in candidates)
            {
                // Resolve against the point on our own axis nearest the obstacle: that reduces
                // every capsule-vs-shape case to the sphere case Physics.ResolveSphere handles.
                var target = c.ClosestPoint((a + b) * 0.5f);
                var onAxis = Physics.ClosestPointOnSegment(target, a, b);
                if (!Physics.ResolveSphere(onAxis, r, c, out var dir, out float depth)) continue;
                push += dir * depth;
            }

            if (push.LengthSquared() < 1e-10f) break;

            Owner!.Transform.Position += push;
            Owner.RebakeMatrices();
            totalPush += push;
            any = true;
        }
        return any;
    }

    /// <summary>Snap to ground when hovering just above it. <paramref name="wasGrounded"/> widens
    /// the search, so walking off the crest of a slope doesn't read as stepping off a cliff.</summary>
    private void ProbeGround(bool wasGrounded)
    {
        var (a, _) = _capsule.WorldSegment();
        float r = _capsule.WorldRadius;
        float reach = r + SkinWidth * 2f + (wasGrounded ? StepOffset : 0.05f);

        if (!PhysicsWorld.Raycast(a, -Vector3.UnitY, reach, out var hit, CollisionMask, _capsule))
            return;

        float slopeDeg = MathF.Acos(Math.Clamp(hit.Normal.Y, -1f, 1f)) * 180f / MathF.PI;
        if (slopeDeg > SlopeLimitDegrees) return;

        IsGrounded = true;
        GroundNormal = hit.Normal;
        if (VerticalVelocity < 0f) VerticalVelocity = 0f;

        float delta = hit.Point.Y + r + SkinWidth - a.Y;
        if (delta > -reach) TrySnapDown(delta);
    }

    /// <summary>Move down by <paramref name="deltaY"/>, but undo it if the landing spot is
    /// horizontally blocked. Returns whether the snap stuck.</summary>
    private bool TrySnapDown(float deltaY)
    {
        if (deltaY >= 0f) return false;
        var t = Owner!.Transform;
        var saved = t.Position;
        t.Position += new Vector3(0f, deltaY, 0f);
        Owner.RebakeMatrices();
        if (!HasHorizontalOverlap()) return true;
        t.Position = saved;
        Owner.RebakeMatrices();
        return false;
    }

    /// <summary>Is the capsule overlapping something that would push it sideways? A mostly-vertical
    /// push is a floor and fine; a sideways one means we're inside a wall or a step's face.</summary>
    private bool HasHorizontalOverlap()
    {
        var (a, b) = _capsule.WorldSegment();
        float r = _capsule.WorldRadius + SkinWidth;
        foreach (var c in PhysicsWorld.OverlapCapsule(a, b, r, CollisionMask, _capsule,
                                                      includeTriggers: false))
        {
            var target = c.ClosestPoint((a + b) * 0.5f);
            var onAxis = Physics.ClosestPointOnSegment(target, a, b);
            if (!Physics.ResolveSphere(onAxis, r, c, out var dir, out _)) continue;
            if (MathF.Abs(dir.X) > 0.3f || MathF.Abs(dir.Z) > 0.3f) return true;
        }
        return false;
    }

    private sealed class ControllerData : IComponentData
    {
        public string Name { get; set; } = "characterController";
        public void SetFromJson(System.Text.Json.JsonElement json) { }
    }
}
