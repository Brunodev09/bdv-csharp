using System.Numerics;

namespace BdvEngine;

/// <summary>A half-plane constraint: velocities on the left of <see cref="Direction"/> through
/// <see cref="Point"/> are permitted.</summary>
public readonly struct OrcaLine
{
    public readonly Vector2 Point;
    public readonly Vector2 Direction;

    public OrcaLine(Vector2 point, Vector2 direction) { Point = point; Direction = direction; }
}

/// <summary>One neighbour as the solver sees it. Positions and velocities are in the XZ plane.</summary>
public readonly struct OrcaNeighbour
{
    public readonly Vector2 Position;
    public readonly Vector2 Velocity;
    public readonly float Radius;

    /// <summary>False for something that will not yield — a static obstacle, or a scripted mover.
    /// The avoiding agent then takes the full correction instead of half.</summary>
    public readonly bool Reciprocal;

    public OrcaNeighbour(Vector2 position, Vector2 velocity, float radius, bool reciprocal = true)
    {
        Position = position;
        Velocity = velocity;
        Radius = radius;
        Reciprocal = reciprocal;
    }
}

/// <summary>
/// Optimal Reciprocal Collision Avoidance in the XZ plane.
///
/// <para>Each neighbour contributes a half-plane of velocities that would lead to a collision
/// within the time horizon; the solver then picks the velocity closest to the one the agent wanted
/// that satisfies all of them.</para>
///
/// <para><b>Why this and not separation forces.</b> A repulsion force is three lines of code and
/// fails exactly where avoidance matters: two agents walking head-on push straight back along the
/// line joining them, so they slow, stop, and jitter rather than stepping aside. ORCA is
/// reciprocal — each agent assumes the other will take half the correction — which makes them pick
/// opposite sides and pass smoothly without any communication.</para>
///
/// <para>Ported from the reference RVO2 formulation. The linear program is exact, not iterative:
/// for a handful of neighbours it is a few dozen floating-point operations.</para>
/// </summary>
public static class Orca
{
    /// <summary>
    /// Velocity closest to <paramref name="preferred"/> that avoids every neighbour.
    ///
    /// <para><paramref name="timeHorizon"/> is how far ahead collisions are anticipated. Larger
    /// makes agents swing wide and early; smaller makes them cut it fine and then correct hard.
    /// <paramref name="dt"/> only matters for neighbours already overlapping, where the constraint
    /// is "separate within one step" rather than "avoid within the horizon".</para>
    /// </summary>
    public static Vector2 ComputeVelocity(Vector2 position, Vector2 velocity, float radius,
                                          Vector2 preferred, float maxSpeed,
                                          IReadOnlyList<OrcaNeighbour> neighbours,
                                          float timeHorizon, float dt,
                                          List<OrcaLine> scratch)
    {
        scratch.Clear();
        float invTimeHorizon = 1f / MathF.Max(timeHorizon, 1e-4f);

        for (int i = 0; i < neighbours.Count; i++)
        {
            var other = neighbours[i];
            var relativePosition = other.Position - position;
            var relativeVelocity = velocity - other.Velocity;
            float distSq = relativePosition.LengthSquared();
            float combinedRadius = radius + other.Radius;
            float combinedRadiusSq = combinedRadius * combinedRadius;

            Vector2 u, direction;

            if (distSq > combinedRadiusSq)
            {
                // Vector from the cut-off circle's centre to the relative velocity.
                var w = relativeVelocity - invTimeHorizon * relativePosition;
                float wLengthSq = w.LengthSquared();
                float dotProduct = Vector2.Dot(w, relativePosition);

                if (dotProduct < 0f && dotProduct * dotProduct > combinedRadiusSq * wLengthSq)
                {
                    // Closest point is on the cut-off circle itself.
                    float wLength = MathF.Sqrt(wLengthSq);
                    var unitW = w / MathF.Max(wLength, 1e-6f);
                    direction = new Vector2(unitW.Y, -unitW.X);
                    u = (combinedRadius * invTimeHorizon - wLength) * unitW;
                }
                else
                {
                    // Closest point is on one of the legs of the velocity obstacle.
                    float leg = MathF.Sqrt(MathF.Max(distSq - combinedRadiusSq, 0f));

                    if (Det(relativePosition, w) > 0f)
                    {
                        direction = new Vector2(
                            relativePosition.X * leg - relativePosition.Y * combinedRadius,
                            relativePosition.X * combinedRadius + relativePosition.Y * leg) / distSq;
                    }
                    else
                    {
                        direction = -new Vector2(
                            relativePosition.X * leg + relativePosition.Y * combinedRadius,
                            -relativePosition.X * combinedRadius + relativePosition.Y * leg) / distSq;
                    }

                    float dotProduct2 = Vector2.Dot(relativeVelocity, direction);
                    u = dotProduct2 * direction - relativeVelocity;
                }
            }
            else
            {
                // Already overlapping. Push apart within one timestep rather than over the horizon,
                // or two agents that spawn on top of each other stay there indefinitely.
                float invDt = 1f / MathF.Max(dt, 1e-4f);
                var w = relativeVelocity - invDt * relativePosition;
                float wLength = w.Length();
                var unitW = w / MathF.Max(wLength, 1e-6f);
                direction = new Vector2(unitW.Y, -unitW.X);
                u = (combinedRadius * invDt - wLength) * unitW;
            }

            // Reciprocity: each side takes half the correction, assuming the other takes the rest.
            // Against something that will not yield, take all of it.
            float share = other.Reciprocal ? 0.5f : 1f;
            scratch.Add(new OrcaLine(velocity + share * u, direction));
        }

        var result = preferred;
        int failedAt = LinearProgram2(scratch, maxSpeed, preferred, false, ref result);
        if (failedAt < scratch.Count)
            LinearProgram3(scratch, failedAt, maxSpeed, ref result);
        return result;
    }

    private static float Det(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Optimise along a single line subject to all earlier ones. False when this line's
    /// constraint cannot be met at all.</summary>
    private static bool LinearProgram1(List<OrcaLine> lines, int lineNo, float radius,
                                       Vector2 optimal, bool directionOpt, ref Vector2 result)
    {
        var line = lines[lineNo];
        float dotProduct = Vector2.Dot(line.Point, line.Direction);
        float discriminant = dotProduct * dotProduct + radius * radius - line.Point.LengthSquared();

        if (discriminant < 0f) return false;   // the max-speed circle is entirely outside this line

        float sqrtDiscriminant = MathF.Sqrt(discriminant);
        float tLeft = -dotProduct - sqrtDiscriminant;
        float tRight = -dotProduct + sqrtDiscriminant;

        for (int i = 0; i < lineNo; i++)
        {
            float denominator = Det(line.Direction, lines[i].Direction);
            float numerator = Det(lines[i].Direction, line.Point - lines[i].Point);

            if (MathF.Abs(denominator) <= 1e-5f)
            {
                // Lines are parallel: either this one is already satisfied everywhere, or nowhere.
                if (numerator < 0f) return false;
                continue;
            }

            float t = numerator / denominator;
            if (denominator >= 0f) tRight = MathF.Min(tRight, t);
            else tLeft = MathF.Max(tLeft, t);

            if (tLeft > tRight) return false;
        }

        if (directionOpt)
        {
            result = line.Point + (Vector2.Dot(optimal, line.Direction) > 0f ? tRight : tLeft) * line.Direction;
        }
        else
        {
            float t = Vector2.Dot(line.Direction, optimal - line.Point);
            result = line.Point + Math.Clamp(t, tLeft, tRight) * line.Direction;
        }
        return true;
    }

    /// <summary>Full 2D program. Returns the number of lines satisfied — less than the total means
    /// the constraints conflict and <see cref="LinearProgram3"/> has to relax them.</summary>
    private static int LinearProgram2(List<OrcaLine> lines, float radius, Vector2 optimal,
                                      bool directionOpt, ref Vector2 result)
    {
        if (directionOpt) result = optimal * radius;
        else if (optimal.LengthSquared() > radius * radius) result = Vector2.Normalize(optimal) * radius;
        else result = optimal;

        for (int i = 0; i < lines.Count; i++)
        {
            // Already on the allowed side of this line: nothing to do.
            if (Det(lines[i].Direction, lines[i].Point - result) <= 0f) continue;

            var temp = result;
            if (!LinearProgram1(lines, i, radius, optimal, directionOpt, ref result))
            {
                result = temp;
                return i;
            }
        }
        return lines.Count;
    }

    /// <summary>
    /// Relaxation for over-constrained cases — a crowd pressing in from every side, where no
    /// velocity satisfies everything.
    ///
    /// <para>Instead of giving up (which would leave an agent frozen in exactly the situation where
    /// it most needs to move), this minimises the worst constraint violation, so the agent pushes
    /// out along the direction of least resistance.</para>
    /// </summary>
    private static void LinearProgram3(List<OrcaLine> lines, int beginLine, float radius,
                                       ref Vector2 result)
    {
        float distance = 0f;
        var projected = new List<OrcaLine>();

        for (int i = beginLine; i < lines.Count; i++)
        {
            if (Det(lines[i].Direction, lines[i].Point - result) <= distance) continue;

            projected.Clear();
            for (int j = 0; j < i; j++)
            {
                float determinant = Det(lines[i].Direction, lines[j].Direction);

                Vector2 point;
                if (MathF.Abs(determinant) <= 1e-5f)
                {
                    if (Vector2.Dot(lines[i].Direction, lines[j].Direction) > 0f) continue;  // same side
                    point = 0.5f * (lines[i].Point + lines[j].Point);
                }
                else
                {
                    point = lines[i].Point
                          + Det(lines[j].Direction, lines[i].Point - lines[j].Point)
                            / determinant * lines[i].Direction;
                }

                var direction = Vector2.Normalize(lines[j].Direction - lines[i].Direction);
                projected.Add(new OrcaLine(point, direction));
            }

            var temp = result;
            var optimal = new Vector2(-lines[i].Direction.Y, lines[i].Direction.X);
            if (LinearProgram2(projected, radius, optimal, true, ref result) < projected.Count)
                result = temp;   // shouldn't happen with exact arithmetic; keep the safe value

            distance = Det(lines[i].Direction, lines[i].Point - result);
        }
    }
}
