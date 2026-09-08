using System.Numerics;

namespace BdvEngine;

/// <summary>Something the avoidance solver has to steer around.</summary>
public interface IAvoidanceAgent
{
    /// <summary>World position. Only X and Z are used.</summary>
    Vector3 AvoidancePosition { get; }

    /// <summary>Current world velocity, for predicting where this agent is going.</summary>
    Vector3 AvoidanceVelocity { get; }

    float AvoidanceRadius { get; }

    /// <summary>False while this agent should be ignored — asleep, dead, mid-jump, or simply
    /// switched off.</summary>
    bool AvoidanceActive { get; }

    /// <summary>False for anything that will not yield: a scripted mover, a player, a boulder.
    /// Others then take the full correction rather than assuming this one takes half.</summary>
    bool AvoidanceReciprocal { get; }
}

/// <summary>
/// The registry of agents that avoid each other, and the neighbour query over them.
///
/// <para>Static, like <see cref="PhysicsWorld"/>, because a game has one crowd. Agents register on
/// load and unregister on unload; <see cref="Clear"/> resets it between levels.</para>
///
/// <para><b>Broadphase is a linear scan.</b> Same call as the physics world: at a few hundred
/// agents the distance checks cost less than maintaining a grid, and the query surface will not
/// change when that stops being true.</para>
/// </summary>
public static class AvoidanceWorld
{
    private static readonly List<IAvoidanceAgent> _agents = new();

    /// <summary>How far away another agent can be and still influence steering. Beyond roughly
    /// max speed times the time horizon there is nothing to gain.</summary>
    public static float NeighbourDistance = 6f;

    /// <summary>Cap on neighbours considered, nearest first. A dense crowd has dozens within
    /// range, and the nearest few dominate the result — the rest are cost without effect.</summary>
    public static int MaxNeighbours = 8;

    /// <summary>Seconds of lookahead. Larger swings agents wide and early; smaller cuts it fine and
    /// then corrects hard. 2 is a reasonable default for walking characters.</summary>
    public static float TimeHorizon = 2f;

    /// <summary>
    /// Tiny per-agent nudge applied to the preferred velocity, in units per second.
    ///
    /// <para>ORCA is deterministic and reciprocal, which means perfectly symmetric situations
    /// produce perfectly symmetric answers: agents arranged in a ring all walking through the
    /// centre each compute the mirror image of their neighbour's solution and the whole crowd locks
    /// solid, correctly avoiding each other forever without making progress. Twelve agents on a
    /// circle deadlocked exactly this way — 0 of 12 arrived, none overlapping.</para>
    ///
    /// <para>Breaking the tie is all that is needed. The offset is derived from the agent's slot in
    /// the registry, so it is stable frame to frame and reproducible run to run — a random nudge
    /// would work too but would make crowd behaviour untestable. Set to 0 for exact symmetry if a
    /// scene actually wants it.</para>
    /// </summary>
    public static float SymmetryBreaking = 0.05f;

    public static IReadOnlyList<IAvoidanceAgent> Agents => _agents;

    public static void Register(IAvoidanceAgent a) { if (!_agents.Contains(a)) _agents.Add(a); }

    public static void Unregister(IAvoidanceAgent a) => _agents.Remove(a);

    public static void Clear() => _agents.Clear();

    /// <summary>
    /// Adjust <paramref name="preferred"/> so <paramref name="self"/> avoids its neighbours.
    ///
    /// <para>Returns <paramref name="preferred"/> unchanged when nothing is nearby, which is the
    /// common case and costs one distance check per registered agent.</para>
    /// </summary>
    public static Vector3 Steer(IAvoidanceAgent self, Vector3 preferred, float maxSpeed, float dt,
                                List<OrcaNeighbour> neighbourScratch, List<OrcaLine> lineScratch)
    {
        neighbourScratch.Clear();
        if (_agents.Count < 2) return preferred;

        var selfPos = Flatten(self.AvoidancePosition);
        float range = MathF.Max(NeighbourDistance, 0.01f);
        float rangeSq = range * range;

        for (int i = 0; i < _agents.Count; i++)
        {
            var other = _agents[i];
            if (ReferenceEquals(other, self) || !other.AvoidanceActive) continue;

            var otherPos = Flatten(other.AvoidancePosition);
            if (Vector2.DistanceSquared(selfPos, otherPos) > rangeSq) continue;

            neighbourScratch.Add(new OrcaNeighbour(otherPos, Flatten(other.AvoidanceVelocity),
                                                   other.AvoidanceRadius, other.AvoidanceReciprocal));
        }

        if (neighbourScratch.Count == 0) return preferred;

        var goal = Flatten(preferred);
        if (SymmetryBreaking > 0f)
        {
            // Golden-angle spacing so nearby indices get well-separated directions rather than a
            // gradient the crowd could still line up along.
            float angle = _agents.IndexOf(self) * 2.399963f;
            goal += new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * SymmetryBreaking;
        }

        if (neighbourScratch.Count > MaxNeighbours)
        {
            neighbourScratch.Sort((a, b) =>
                Vector2.DistanceSquared(selfPos, a.Position)
                    .CompareTo(Vector2.DistanceSquared(selfPos, b.Position)));
            neighbourScratch.RemoveRange(MaxNeighbours, neighbourScratch.Count - MaxNeighbours);
        }

        var solved = Orca.ComputeVelocity(
            selfPos, Flatten(self.AvoidanceVelocity), self.AvoidanceRadius,
            goal, maxSpeed, neighbourScratch, TimeHorizon, dt, lineScratch);

        // Y is untouched: avoidance is a horizontal concern, and the character controller owns
        // vertical motion.
        return new Vector3(solved.X, preferred.Y, solved.Y);
    }

    private static Vector2 Flatten(Vector3 v) => new(v.X, v.Z);
}
