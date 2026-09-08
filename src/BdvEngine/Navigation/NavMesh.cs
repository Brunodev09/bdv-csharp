using System.Numerics;

namespace BdvEngine;

/// <summary>One convex cell of walkable floor: an axis-aligned rectangle at a height.</summary>
public sealed class NavPoly
{
    public float MinX, MaxX, MinZ, MaxZ;
    /// <summary>Floor height. Constant across the polygon, because the builder only merges cells
    /// whose heights agree — a ramp becomes several thin polys rather than one sloped one.</summary>
    public float Y;

    /// <summary>Indices into <see cref="NavMesh.Polys"/> reachable directly from here, paired with
    /// the shared edge each one is crossed through.</summary>
    public readonly List<NavPortal> Portals = new();

    /// <summary>Indices into <see cref="NavMesh.Links"/> leaving this polygon.</summary>
    public readonly List<int> LinkIds = new();

    public Vector3 Center => new((MinX + MaxX) * 0.5f, Y, (MinZ + MaxZ) * 0.5f);
    public float Width => MaxX - MinX;
    public float Depth => MaxZ - MinZ;
    public float Area => Width * Depth;

    public bool ContainsXZ(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    /// <summary>Nearest point inside this polygon to (x, z), at the polygon's height.</summary>
    public Vector3 ClampXZ(float x, float z)
        => new(Math.Clamp(x, MinX, MaxX), Y, Math.Clamp(z, MinZ, MaxZ));
}

/// <summary>A shared edge between two polygons — the segment an agent walks through.</summary>
public readonly struct NavPortal
{
    public readonly int Neighbour;
    public readonly Vector3 A, B;

    public NavPortal(int neighbour, Vector3 a, Vector3 b) { Neighbour = neighbour; A = a; B = b; }

    public Vector3 Mid => (A + B) * 0.5f;
}

/// <summary>What kind of traversal an off-mesh link represents. The mesh treats them all the same;
/// the tag exists so an agent can play the right animation and pick the right movement.</summary>
public enum NavLinkKind
{
    /// <summary>Horizontal gap crossed under the agent's own power.</summary>
    Jump,
    /// <summary>Downward-only step off a ledge — cheap to take, impossible to reverse.</summary>
    Drop,
    /// <summary>Vertical traversal at a fixed speed: ladder, vine, rope.</summary>
    Climb,
    /// <summary>Anything the game defines — a door, a teleporter, a zipline.</summary>
    Custom,
}

/// <summary>
/// A connection between two points on the mesh that is NOT walkable adjacency: a jump across a gap,
/// a drop off a ledge, a ladder, a teleporter.
///
/// <para>Links are what make a navmesh usable for real level design. Without them the reachable
/// world is exactly the connected walkable surface, so a ledge you can obviously hop down is a dead
/// end and two rooftops a metre apart are separate worlds.</para>
/// </summary>
public sealed class NavLink
{
    public int FromPoly;
    public int ToPoly;
    public Vector3 Start;
    public Vector3 End;
    public NavLinkKind Kind = NavLinkKind.Jump;

    /// <summary>Multiplies the link's length when costing a route. Above 1 makes the pathfinder
    /// prefer walking around — the usual want, since a jump is riskier than a detour and an agent
    /// that leaps at every opportunity looks broken.</summary>
    public float CostMultiplier = 2f;

    /// <summary>False for a one-way link. Drops are one-way by nature: falling off a ledge is not
    /// reversible.</summary>
    public bool Bidirectional = true;

    /// <summary>Set false to disable without removing — a closed door, a broken bridge.</summary>
    public bool Enabled = true;

    public float Length => Vector3.Distance(Start, End);
}

/// <summary>One step of a path: a position, plus the link being entered if this is where an agent
/// leaves the walkable surface.</summary>
public readonly struct NavWaypoint
{
    public readonly Vector3 Position;

    /// <summary>The link this waypoint begins, or null for ordinary walking. When set, the NEXT
    /// waypoint is the link's far end and the agent should traverse rather than walk to it.</summary>
    public readonly NavLink? Link;

    public NavWaypoint(Vector3 position, NavLink? link = null)
    {
        Position = position;
        Link = link;
    }

    public bool IsLinkStart => Link != null;
}

/// <summary>A path as waypoints that know which transitions are links. Reused across queries —
/// <see cref="Clear"/> keeps the capacity.</summary>
public sealed class NavPath
{
    public readonly List<NavWaypoint> Waypoints = new();

    public int Count => Waypoints.Count;
    public NavWaypoint this[int i] => Waypoints[i];

    public void Clear() => Waypoints.Clear();

    /// <summary>True when any step of the route is an off-mesh link.</summary>
    public bool UsesLinks
    {
        get
        {
            for (int i = 0; i < Waypoints.Count; i++) if (Waypoints[i].IsLinkStart) return true;
            return false;
        }
    }

    public float Length()
    {
        float d = 0f;
        for (int i = 0; i < Waypoints.Count - 1; i++)
            d += Vector3.Distance(Waypoints[i].Position, Waypoints[i + 1].Position);
        return d;
    }

    public void CopyPositionsTo(List<Vector3> into)
    {
        into.Clear();
        for (int i = 0; i < Waypoints.Count; i++) into.Add(Waypoints[i].Position);
    }
}

/// <summary>
/// A walkable surface as convex polygons plus the portals between them, and A* over that graph.
///
/// <para>Build one with <see cref="NavMeshBuilder"/> rather than by hand — it is derived from the
/// collision world, so it stays true to what a character can actually walk on.</para>
///
/// <code>
/// var nav = NavMeshBuilder.Build(new NavBakeSettings
/// {
///     Bounds = new Bounds(new Vector3(-40, -5, -40), new Vector3(40, 20, 40)),
///     CellSize = 0.5f, AgentRadius = 0.35f, AgentHeight = 1.8f,
/// });
///
/// if (nav.FindPath(from, to, path)) { /* path is a list of world waypoints */ }
/// </code>
///
/// <para><b>Why polygons and not the grid they came from.</b> A* on a grid produces staircase paths
/// that need smoothing anyway, and it costs a node per cell — a 200x200m area at half-metre cells
/// is 160,000 nodes. Merging into rectangles typically leaves a few hundred, and the funnel over
/// their portals yields genuinely straight lines rather than a smoothed approximation of a
/// staircase.</para>
///
/// <para><b>Polygons are axis-aligned rectangles.</b> That is the honest limitation of this
/// implementation: a diagonal wall becomes a staircase of rectangles rather than one angled
/// polygon, so the mesh has more polys than a Recast-style build would produce for the same scene.
/// In exchange the merge is simple and provably correct, and the funnel still produces straight
/// paths because it works on portal segments, not on polygon shapes.</para>
/// </summary>
public sealed class NavMesh
{
    private readonly List<NavPoly> _polys = new();

    public IReadOnlyList<NavPoly> Polys => _polys;

    /// <summary>Cell size the mesh was baked at. Kept for diagnostics and for sizing queries that
    /// want to reason in the same units.</summary>
    public float CellSize { get; internal set; } = 0.5f;

    /// <summary>Vertical span an agent may cross at a portal, from the bake's step height. Two
    /// otherwise-adjacent polys further apart than this are NOT linked — that is what stops a path
    /// from walking off a ledge onto the floor below.</summary>
    public float StepHeight { get; internal set; } = 0.4f;

    /// <summary>Walkable cells the bake ended up with, after erosion. Reported so a gate can show
    /// how far the rectangle merge compressed the search space.</summary>
    public int WalkableCells { get; internal set; }

    public bool IsEmpty => _polys.Count == 0;

    private readonly List<NavLink> _links = new();

    /// <summary>Every off-mesh link, in the order they were added.</summary>
    public IReadOnlyList<NavLink> Links => _links;

    internal int Add(NavPoly p) { _polys.Add(p); return _polys.Count - 1; }

    /// <summary>
    /// Connect two points that are not walkably adjacent — a jump, a drop, a ladder, a door.
    ///
    /// <para>Both ends snap to whichever polygon they sit on; a link whose end is off the mesh is
    /// rejected rather than added as a route to nowhere. Returns the link, or null if either end
    /// found no polygon.</para>
    /// </summary>
    public NavLink? AddLink(Vector3 start, Vector3 end, NavLinkKind kind = NavLinkKind.Jump,
                            bool bidirectional = true, float costMultiplier = 2f)
    {
        int from = NearestPoly(start), to = NearestPoly(end);
        if (from < 0 || to < 0 || from == to) return null;

        var link = new NavLink
        {
            FromPoly = from, ToPoly = to,
            Start = _polys[from].ClampXZ(start.X, start.Z),
            End = _polys[to].ClampXZ(end.X, end.Z),
            Kind = kind,
            Bidirectional = bidirectional,
            CostMultiplier = costMultiplier,
        };
        return AddLink(link);
    }

    /// <summary>Add a fully-specified link. Used by the builder's auto-generation, which has
    /// already resolved both polygons.</summary>
    public NavLink AddLink(NavLink link)
    {
        int id = _links.Count;
        _links.Add(link);
        _polys[link.FromPoly].LinkIds.Add(id);
        if (link.Bidirectional) _polys[link.ToPoly].LinkIds.Add(id);
        return link;
    }

    /// <summary>Remove every link. Re-bake or re-generate after calling this.</summary>
    public void ClearLinks()
    {
        _links.Clear();
        for (int i = 0; i < _polys.Count; i++) _polys[i].LinkIds.Clear();
    }

    internal void Link(int a, int b, Vector3 e0, Vector3 e1)
    {
        _polys[a].Portals.Add(new NavPortal(b, e0, e1));
        _polys[b].Portals.Add(new NavPortal(a, e0, e1));
    }

    // ── queries ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Polygon under (or nearest to) a world point, or -1 on an empty mesh.
    ///
    /// <para>Prefers a polygon that contains the point in XZ and is closest in height — which is
    /// what makes multi-level geometry work, since a bridge and the ground beneath it both contain
    /// the same (x, z).</para>
    /// </summary>
    public int NearestPoly(Vector3 p, float maxVertical = 4f)
    {
        int best = -1;
        float bestScore = float.MaxValue;

        for (int i = 0; i < _polys.Count; i++)
        {
            var poly = _polys[i];
            bool inside = poly.ContainsXZ(p.X, p.Z);
            float dy = MathF.Abs(poly.Y - p.Y);
            if (inside && dy > maxVertical) continue;

            // Containment beats proximity: a poly the point is standing on always wins over one it
            // is merely near, however close that one is horizontally.
            var c = poly.ClampXZ(p.X, p.Z);
            float horizontal = new Vector2(c.X - p.X, c.Z - p.Z).Length();
            float score = inside ? dy : horizontal * 100f + dy;

            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    /// <summary>Nearest walkable world position to <paramref name="p"/>. Use it to snap a spawn or
    /// a click target onto the mesh before pathing from or to it.</summary>
    public bool NearestPoint(Vector3 p, out Vector3 result)
    {
        int i = NearestPoly(p);
        if (i < 0) { result = p; return false; }
        result = _polys[i].ClampXZ(p.X, p.Z);
        return true;
    }

    /// <summary>True when a world point sits on the mesh (within <paramref name="tolerance"/>
    /// vertically) — a cheap "can something stand here" test.</summary>
    public bool IsWalkable(Vector3 p, float tolerance = 0.5f)
    {
        for (int i = 0; i < _polys.Count; i++)
            if (_polys[i].ContainsXZ(p.X, p.Z) && MathF.Abs(_polys[i].Y - p.Y) <= tolerance)
                return true;
        return false;
    }

    // ── pathfinding ─────────────────────────────────────────────────────────

    private readonly List<int> _corridor = new();
    /// <summary>Link used to ENTER _corridor[i], or -1 when it was walked into. This is what lets
    /// the path builder split the corridor at links instead of funnelling straight through one.</summary>
    private readonly List<int> _corridorLink = new();
    private int[] _cameLink = Array.Empty<int>();
    private readonly PriorityQueue<int, float> _open = new();
    private float[] _gScore = Array.Empty<float>();
    private int[] _cameFrom = Array.Empty<int>();
    private int[] _visitStamp = Array.Empty<int>();
    private int _search;

    /// <summary>
    /// Path from <paramref name="start"/> to <paramref name="end"/> as world waypoints, or false if
    /// there is no route.
    ///
    /// <para><paramref name="path"/> is cleared and refilled, so callers can keep one list and
    /// avoid allocating per request. The first waypoint is the start point snapped to the mesh and
    /// the last is the goal snapped to the mesh — an unreachable goal fails rather than silently
    /// returning a path to somewhere near it.</para>
    /// </summary>
    public bool FindPath(Vector3 start, Vector3 end, List<Vector3> path)
    {
        if (!FindPath(start, end, _scratchPath)) { path.Clear(); return false; }
        _scratchPath.CopyPositionsTo(path);
        return path.Count > 0;
    }

    private readonly NavPath _scratchPath = new();
    private readonly List<Vector3> _runPath = new();
    private readonly List<int> _run = new();

    /// <summary>
    /// Path from <paramref name="start"/> to <paramref name="end"/>, marking which transitions are
    /// off-mesh links so an agent can jump or climb them rather than walking.
    ///
    /// <para>A link is a discrete transition, not walkable adjacency, so the corridor is split at
    /// every link and each walkable run is funnelled on its own. Funnelling straight through a link
    /// would produce a straight line from before it to after it — cutting the corner an agent has
    /// to actually leap from.</para>
    /// </summary>
    public bool FindPath(Vector3 start, Vector3 end, NavPath path)
    {
        path.Clear();
        if (_polys.Count == 0) return false;

        int s = NearestPoly(start), t = NearestPoly(end);
        if (s < 0 || t < 0) return false;

        var startPoint = _polys[s].ClampXZ(start.X, start.Z);
        var endPoint = _polys[t].ClampXZ(end.X, end.Z);

        if (s == t)
        {
            path.Waypoints.Add(new NavWaypoint(startPoint));
            path.Waypoints.Add(new NavWaypoint(endPoint));
            return true;
        }

        if (!SearchCorridor(s, t)) return false;

        var runStart = startPoint;
        _run.Clear();
        _run.Add(_corridor[0]);

        for (int i = 1; i < _corridor.Count; i++)
        {
            int linkId = _corridorLink[i];
            if (linkId < 0) { _run.Add(_corridor[i]); continue; }

            // The corridor crosses a link here. Close the current walkable run at the link's near
            // end, emit the link, and begin a fresh run from its far end.
            var link = _links[linkId];
            bool forward = link.ToPoly == _corridor[i];
            var enter = forward ? link.Start : link.End;
            var exit = forward ? link.End : link.Start;

            EmitRun(runStart, enter, path, link);

            path.Waypoints.Add(new NavWaypoint(exit));
            runStart = exit;
            _run.Clear();
            _run.Add(_corridor[i]);
        }

        EmitRun(runStart, endPoint, path, null);
        return path.Count > 0;
    }

    /// <summary>Funnel the polygons currently in <see cref="_run"/> and append them, tagging the
    /// final waypoint with <paramref name="exitLink"/> when this run ends at one.</summary>
    private void EmitRun(Vector3 from, Vector3 to, NavPath path, NavLink? exitLink)
    {
        _runPath.Clear();
        if (_run.Count <= 1)
        {
            _runPath.Add(from);
            if (Vector3.DistanceSquared(from, to) > 1e-6f) _runPath.Add(to);
        }
        else
        {
            FunnelRun(from, to, _runPath);
        }

        for (int i = 0; i < _runPath.Count; i++)
        {
            // Don't repeat the point a previous run already ended on.
            if (path.Count > 0 && Vector3.DistanceSquared(path[^1].Position, _runPath[i]) < 1e-6f)
                continue;

            bool last = i == _runPath.Count - 1;
            path.Waypoints.Add(new NavWaypoint(_runPath[i], last ? exitLink : null));
        }

        // A run whose points all collapsed still has to carry the link, or the traversal is lost.
        if (exitLink != null && (path.Count == 0 || !path[^1].IsLinkStart))
        {
            if (path.Count > 0) path.Waypoints[^1] = new NavWaypoint(path[^1].Position, exitLink);
            else path.Waypoints.Add(new NavWaypoint(to, exitLink));
        }
    }

    /// <summary>A* over the polygon graph, filling <see cref="_corridor"/> with poly indices.</summary>
    private bool SearchCorridor(int start, int goal)
    {
        int n = _polys.Count;
        if (_gScore.Length < n)
        {
            _gScore = new float[n];
            _cameFrom = new int[n];
            _cameLink = new int[n];
            _visitStamp = new int[n];
        }

        // A generation stamp instead of clearing the arrays: pathfinding is called often and per
        // frame, and wiping three arrays of every polygon each time dwarfs the search itself.
        _search++;
        _open.Clear();

        _gScore[start] = 0f;
        _cameFrom[start] = -1;
        _cameLink[start] = -1;
        _visitStamp[start] = _search;
        _open.Enqueue(start, Heuristic(start, goal));

        while (_open.TryDequeue(out int current, out _))
        {
            if (current == goal)
            {
                _corridor.Clear();
                _corridorLink.Clear();
                for (int at = goal; at != -1; at = _cameFrom[at])
                {
                    _corridor.Add(at);
                    _corridorLink.Add(_cameLink[at]);
                }
                _corridor.Reverse();
                _corridorLink.Reverse();
                return true;
            }

            var poly = _polys[current];
            for (int i = 0; i < poly.Portals.Count; i++)
            {
                var portal = poly.Portals[i];
                int next = portal.Neighbour;

                // Cost through the portal itself rather than centre-to-centre: two large polys
                // sharing a short edge are not as cheap to cross as their centres suggest.
                float step = Vector3.Distance(poly.Center, portal.Mid)
                           + Vector3.Distance(portal.Mid, _polys[next].Center);
                float g = _gScore[current] + step;

                if (_visitStamp[next] == _search && g >= _gScore[next]) continue;
                _visitStamp[next] = _search;
                _gScore[next] = g;
                _cameFrom[next] = current;
                _cameLink[next] = -1;
                _open.Enqueue(next, g + Heuristic(next, goal));
            }

            // Off-mesh links leave from the same polygon. Costed by length times the multiplier, so
            // a jump can be made deliberately unattractive relative to walking round.
            var linkIds = poly.LinkIds;
            for (int i = 0; i < linkIds.Count; i++)
            {
                var link = _links[linkIds[i]];
                if (!link.Enabled) continue;

                bool forward = link.FromPoly == current;
                if (!forward && !link.Bidirectional) continue;
                int next = forward ? link.ToPoly : link.FromPoly;
                if (next == current) continue;

                var entry = forward ? link.Start : link.End;
                float g = _gScore[current]
                        + Vector3.Distance(poly.Center, entry)
                        + link.Length * MathF.Max(link.CostMultiplier, 0.01f);

                if (_visitStamp[next] == _search && g >= _gScore[next]) continue;
                _visitStamp[next] = _search;
                _gScore[next] = g;
                _cameFrom[next] = current;
                _cameLink[next] = linkIds[i];
                _open.Enqueue(next, g + Heuristic(next, goal));
            }
        }
        return false;
    }

    private float Heuristic(int a, int b) => Vector3.Distance(_polys[a].Center, _polys[b].Center);

    /// <summary>
    /// Simple Stupid Funnel: walk the portal sequence keeping a left and right bound, and emit a
    /// corner whenever they cross. Turns the corridor into the straight lines an agent should
    /// actually walk, rather than a tour of polygon centres.
    /// </summary>
    private void FunnelRun(Vector3 start, Vector3 end, List<Vector3> path)
    {
        // Portal list: the start point as a degenerate portal, each corridor portal oriented
        // left/right relative to travel, then the goal as another degenerate portal.
        var lefts = new List<Vector3> { start };
        var rights = new List<Vector3> { start };

        for (int i = 0; i < _run.Count - 1; i++)
        {
            int from = _run[i], to = _run[i + 1];
            var portal = FindPortal(from, to);

            // Orient the edge so "left" is genuinely on the left of the direction of travel; the
            // stored order is arbitrary, and a flipped portal makes the funnel emit corners on the
            // wrong side and produce paths that cut through walls.
            var dir = _polys[to].Center - _polys[from].Center;
            var toA = portal.A - _polys[from].Center;
            bool aIsLeft = dir.Z * toA.X - dir.X * toA.Z > 0f;

            lefts.Add(aIsLeft ? portal.A : portal.B);
            rights.Add(aIsLeft ? portal.B : portal.A);
        }

        lefts.Add(end);
        rights.Add(end);

        var apex = start;
        int apexIndex = 0, leftIndex = 0, rightIndex = 0;
        var portalLeft = lefts[0];
        var portalRight = rights[0];

        path.Add(apex);

        for (int i = 1; i < lefts.Count; i++)
        {
            var left = lefts[i];
            var right = rights[i];

            // Tighten the right bound; if it crosses the left one, the left bound was a corner.
            if (Cross2(apex, portalRight, right) <= 0f)
            {
                if (Vector3.DistanceSquared(apex, portalRight) < 1e-8f || Cross2(apex, portalLeft, right) > 0f)
                {
                    portalRight = right;
                    rightIndex = i;
                }
                else
                {
                    AddWaypoint(path, portalLeft);
                    apex = portalLeft;
                    apexIndex = leftIndex;
                    portalLeft = apex;
                    portalRight = apex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }

            if (Cross2(apex, portalLeft, left) >= 0f)
            {
                if (Vector3.DistanceSquared(apex, portalLeft) < 1e-8f || Cross2(apex, portalRight, left) < 0f)
                {
                    portalLeft = left;
                    leftIndex = i;
                }
                else
                {
                    AddWaypoint(path, portalRight);
                    apex = portalRight;
                    apexIndex = rightIndex;
                    portalLeft = apex;
                    portalRight = apex;
                    leftIndex = apexIndex;
                    rightIndex = apexIndex;
                    i = apexIndex;
                    continue;
                }
            }
        }

        AddWaypoint(path, end);
    }

    private static void AddWaypoint(List<Vector3> path, Vector3 p)
    {
        if (path.Count > 0 && Vector3.DistanceSquared(path[^1], p) < 1e-6f) return;
        path.Add(p);
    }

    /// <summary>2D cross product in the XZ plane — positive when c is left of a→b.</summary>
    private static float Cross2(Vector3 a, Vector3 b, Vector3 c)
        => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

    private NavPortal FindPortal(int from, int to)
    {
        var portals = _polys[from].Portals;
        for (int i = 0; i < portals.Count; i++)
            if (portals[i].Neighbour == to) return portals[i];
        return new NavPortal(to, _polys[to].Center, _polys[to].Center);
    }

    /// <summary>Total polygon area, for a bake report — a mesh that suddenly covers half what it
    /// used to is the clearest sign a bake setting went wrong.</summary>
    public float TotalArea()
    {
        float a = 0f;
        for (int i = 0; i < _polys.Count; i++) a += _polys[i].Area;
        return a;
    }

    public int PortalCount()
    {
        int n = 0;
        for (int i = 0; i < _polys.Count; i++) n += _polys[i].Portals.Count;
        return n / 2;   // each portal is stored on both sides
    }
}
