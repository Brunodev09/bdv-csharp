using System.Numerics;

namespace BdvEngine;

/// <summary>Inputs to a navmesh bake. Defaults suit a roughly human-sized agent in a metre-scaled
/// world.</summary>
public sealed class NavBakeSettings
{
    /// <summary>Volume to sample. Y matters: rays are cast from <c>Bounds.Max.Y</c> downward, so a
    /// ceiling above the top is not seen and floors below the bottom are not found.</summary>
    public Bounds Bounds = new(new Vector3(-50, -10, -50), new Vector3(50, 30, 50));

    /// <summary>Sampling resolution. Half the agent radius is a good rule: finer resolves narrow
    /// gaps and doorways, coarser bakes faster and produces fewer, larger polygons.</summary>
    public float CellSize = 0.5f;

    /// <summary>Agents are treated as this wide. The walkable area is eroded by it, so a path is
    /// never routed closer to a wall or a ledge than an agent's body can go.</summary>
    public float AgentRadius = 0.35f;

    /// <summary>Clearance an agent needs above the floor. A cell with less headroom than this is
    /// not walkable, which is what keeps agents from pathing under a table they cannot fit below.</summary>
    public float AgentHeight = 1.8f;

    /// <summary>Steepest floor an agent can stand on. Should match the character controller's
    /// <see cref="CharacterController.SlopeLimitDegrees"/>, or the mesh will promise routes the
    /// controller then refuses to walk.</summary>
    public float SlopeLimitDegrees = 50f;

    /// <summary>Height difference two neighbouring cells may have and still connect — the same idea
    /// as the controller's step offset. Larger than the real step height makes agents path off
    /// ledges they then fall down.</summary>
    public float StepHeight = 0.4f;

    /// <summary>Cells whose floor heights differ by more than this are never merged into one
    /// polygon. Keeps a ramp as a staircase of flat polys instead of one polygon at an average
    /// height that floats above the middle of it.</summary>
    public float HeightTolerance = 0.2f;

    /// <summary>Layers the sampling rays consider solid. Matches the collision layers a character
    /// collides with; foliage or trigger layers should be excluded.</summary>
    public int LayerMask = ~0;

    /// <summary>Print a one-line summary when the bake finishes.</summary>
    public bool Verbose = true;
}

/// <summary>
/// Bakes a <see cref="NavMesh"/> out of the live <see cref="PhysicsWorld"/>.
///
/// <para>The mesh is <b>derived from collision</b>, not authored. That is the property that makes
/// it worth having in a solo workflow: place geometry, re-bake, and navigation matches what a
/// character can actually walk on, with no second representation of the level to keep in sync.</para>
///
/// <para><b>The pipeline</b>, and why each step is there:</para>
/// <list type="number">
///   <item><b>Sample</b> — a ray down per cell finds the floor, its height and its slope.</item>
///   <item><b>Headroom</b> — a ray up from the floor rejects cells an agent cannot stand in.</item>
///   <item><b>Erode</b> by the agent radius, so paths keep a body's width from walls and ledges.
///         Without this a path hugs geometry and agents grind along it.</item>
///   <item><b>Merge</b> walkable cells into maximal rectangles: convex polygons, far fewer nodes
///         than cells.</item>
///   <item><b>Link</b> rectangles that share an edge and are within step height.</item>
/// </list>
///
/// <para>Bake after the world is built and its colliders are registered. It reads the physics
/// world, so anything not in it — a purely visual mesh with no collider — is invisible here, which
/// is usually what you want and occasionally a surprise.</para>
/// </summary>
public static class NavMeshBuilder
{
    public static NavMesh Build(NavBakeSettings settings)
    {
        var mesh = new NavMesh { CellSize = settings.CellSize, StepHeight = settings.StepHeight };

        float cell = MathF.Max(settings.CellSize, 0.01f);
        var size = settings.Bounds.Size;
        int nx = Math.Max((int)MathF.Ceiling(size.X / cell), 1);
        int nz = Math.Max((int)MathF.Ceiling(size.Z / cell), 1);

        var walkable = new bool[nx * nz];
        var height = new float[nx * nz];

        float cosLimit = MathF.Cos(settings.SlopeLimitDegrees * MathF.PI / 180f);
        float rayTop = settings.Bounds.Max.Y;
        float rayLength = size.Y;

        // ── 1 & 2: sample the floor and check headroom ──
        int found = 0;
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            var origin = new Vector3(
                settings.Bounds.Min.X + (x + 0.5f) * cell,
                rayTop,
                settings.Bounds.Min.Z + (z + 0.5f) * cell);

            if (!PhysicsWorld.Raycast(origin, -Vector3.UnitY, rayLength, out var hit, settings.LayerMask))
                continue;

            if (hit.Normal.Y < cosLimit) continue;   // too steep to stand on

            // Headroom: start slightly above the floor so the ray doesn't immediately re-hit it.
            var above = hit.Point + new Vector3(0, 0.05f, 0);
            if (PhysicsWorld.Raycast(above, Vector3.UnitY, settings.AgentHeight - 0.05f, out _, settings.LayerMask))
                continue;

            int i = z * nx + x;
            walkable[i] = true;
            height[i] = hit.Point.Y;
            found++;
        }

        // ── 3: erode by the agent radius ──
        int erodeCells = (int)MathF.Ceiling(settings.AgentRadius / cell);
        if (erodeCells > 0) walkable = Erode(walkable, height, nx, nz, erodeCells, settings.StepHeight);

        // ── 4 & 5: merge into rectangles and link them ──
        var rects = MergeRects(walkable, height, nx, nz, settings.HeightTolerance);
        foreach (var r in rects)
        {
            mesh.Add(new NavPoly
            {
                MinX = settings.Bounds.Min.X + r.X0 * cell,
                MaxX = settings.Bounds.Min.X + (r.X1 + 1) * cell,
                MinZ = settings.Bounds.Min.Z + r.Z0 * cell,
                MaxZ = settings.Bounds.Min.Z + (r.Z1 + 1) * cell,
                Y = r.Y,
            });
        }
        LinkAdjacent(mesh, settings.StepHeight);

        int eroded = 0;
        for (int i = 0; i < walkable.Length; i++) if (walkable[i]) eroded++;
        mesh.WalkableCells = eroded;

        if (settings.Verbose)
        {
            Console.WriteLine(
                $"[nav] baked {mesh.Polys.Count} polys, {mesh.PortalCount()} portals, " +
                $"{mesh.TotalArea():F0}m2 from {eroded:N0}/{found:N0} cells " +
                $"({nx}x{nz} grid at {cell:F2}m)");
        }
        return mesh;
    }

    /// <summary>
    /// Shrink the walkable set by <paramref name="radiusCells"/>, so no walkable cell is within
    /// that many cells of a non-walkable one <b>or of a drop taller than a step</b>.
    ///
    /// <para>The height test is the half that is easy to leave out and expensive to omit. Walkable
    /// is not enough on its own: the cell at the lip of a 3m wall has walkable neighbours — the
    /// floor below and the wall top — so a purely boolean erosion keeps it, and agents path right
    /// up to ledges and along the tops of walls. Treating a height discontinuity as an edge is what
    /// makes the eroded set mean "an agent's body fits here".</para>
    ///
    /// <para>Two separable passes (X then Z) rather than a square kernel: O(n) per axis instead of
    /// O(r²) per cell, for the same Chebyshev radius an axis-aligned grid implies.</para>
    /// </summary>
    private static bool[] Erode(bool[] src, float[] height, int nx, int nz,
                                int radiusCells, float stepHeight)
    {
        var tmp = new bool[src.Length];
        var dst = new bool[src.Length];

        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int centre = z * nx + x;
            bool ok = src[centre];
            for (int d = -radiusCells; d <= radiusCells && ok; d++)
            {
                int sx = x + d;
                ok = sx >= 0 && sx < nx
                     && src[z * nx + sx]
                     && MathF.Abs(height[z * nx + sx] - height[centre]) <= stepHeight;
            }
            tmp[centre] = ok;
        }

        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int centre = z * nx + x;
            bool ok = tmp[centre];
            for (int d = -radiusCells; d <= radiusCells && ok; d++)
            {
                int sz = z + d;
                ok = sz >= 0 && sz < nz
                     && tmp[sz * nx + x]
                     && MathF.Abs(height[sz * nx + x] - height[centre]) <= stepHeight;
            }
            dst[centre] = ok;
        }
        return dst;
    }

    private readonly record struct Rect(int X0, int Z0, int X1, int Z1, float Y);

    /// <summary>
    /// Greedy maximal-rectangle merge: take the longest run of unclaimed same-height cells in a
    /// row, then extend it down as far as every row below matches.
    ///
    /// <para>Not the minimal decomposition — that is NP-hard for general shapes — but it is linear,
    /// deterministic, and turns a big open floor into one polygon, which is what actually matters
    /// for search cost.</para>
    /// </summary>
    private static List<Rect> MergeRects(bool[] walkable, float[] height, int nx, int nz, float tolerance)
    {
        var used = new bool[walkable.Length];
        var rects = new List<Rect>();

        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int i = z * nx + x;
            if (!walkable[i] || used[i]) continue;

            float y = height[i];

            // Extend right while the cells stay walkable, unclaimed and at a compatible height.
            int x1 = x;
            while (x1 + 1 < nx && Match(x1 + 1, z)) x1++;

            // Extend down while EVERY cell of the next row matches across the full width.
            int z1 = z;
            while (z1 + 1 < nz)
            {
                bool rowOk = true;
                for (int cx = x; cx <= x1 && rowOk; cx++) rowOk = Match(cx, z1 + 1);
                if (!rowOk) break;
                z1++;
            }

            for (int cz = z; cz <= z1; cz++)
            for (int cx = x; cx <= x1; cx++)
                used[cz * nx + cx] = true;

            // Average height over the block, so a rect spanning a gentle rise sits in the middle of
            // it rather than at whichever corner happened to be sampled first.
            float sum = 0f;
            int n = 0;
            for (int cz = z; cz <= z1; cz++)
            for (int cx = x; cx <= x1; cx++) { sum += height[cz * nx + cx]; n++; }

            rects.Add(new Rect(x, z, x1, z1, sum / n));

            bool Match(int cx, int cz)
            {
                int k = cz * nx + cx;
                return walkable[k] && !used[k] && MathF.Abs(height[k] - y) <= tolerance;
            }
        }
        return rects;
    }


    /// <summary>
    /// Find and add jump and drop links automatically, by probing outward from every open edge of
    /// the mesh.
    ///
    /// <para>An open edge is a polygon boundary with no portal across it — the lip of a ledge, the
    /// near side of a gap, the rim of a platform. For each sample along one, this probes outward
    /// for a landing polygon and, if the air between the two ends is clear, adds a link.</para>
    ///
    /// <para><b>The clearance check is not optional.</b> Two polygons either side of a wall are
    /// exactly the geometry this looks for — near each other, unconnected — so without a ray
    /// between the ends the generator cheerfully links straight through walls, which is worse than
    /// having no links at all.</para>
    ///
    /// <para>At most one link is kept per ordered polygon pair (the shortest), or a 20m ledge
    /// facing a 20m ledge would produce a link per sample.</para>
    /// </summary>
    public static int GenerateLinks(NavMesh mesh, NavLinkSettings settings)
    {
        var best = new Dictionary<(int From, int To), NavLink>();

        for (int p = 0; p < mesh.Polys.Count; p++)
        {
            var poly = mesh.Polys[p];
            foreach (var (point, outward) in OpenEdgeSamples(mesh, p, settings.SampleSpacing))
                Probe(mesh, settings, best, p, poly, point, outward);
        }

        // Drops are one-way, so a pair joined by a drop one way and a jump the other keeps both.
        int added = 0;
        foreach (var link in best.Values)
        {
            mesh.AddLink(link);
            added++;
        }

        if (settings.Verbose)
        {
            int jumps = 0, drops = 0;
            foreach (var l in best.Values) { if (l.Kind == NavLinkKind.Drop) drops++; else jumps++; }
            Console.WriteLine($"[nav] generated {added} off-mesh links ({jumps} jump, {drops} drop)");
        }
        return added;
    }

    private static void Probe(NavMesh mesh, NavLinkSettings settings,
                              Dictionary<(int, int), NavLink> best,
                              int fromPoly, NavPoly poly, Vector3 point, Vector3 outward)
    {
        // March outward, keeping the nearest DROP and the nearest LEVEL landing separately.
        //
        // Taking whichever came first was wrong, and wrong in a way that silently removed the
        // interesting half of the feature: probing off a raised platform, the first thing under the
        // probe is the floor two metres below, so every edge produced a drop and the march stopped
        // before ever reaching the ledge across the gap. A level landing is a jump; a lower one is
        // a drop; they are different traversals to different places, and both are worth having.
        float step = MathF.Max(mesh.CellSize, 0.1f);
        bool haveLevel = false, haveDrop = false;

        for (float d = step; d <= settings.MaxJumpDistance && !haveLevel; d += step)
        {
            var probe = point + outward * d;

            int landing = FindLanding(mesh, probe, point.Y, settings);
            if (landing < 0 || landing == fromPoly) continue;
            if (SharesPortal(poly, landing)) continue;   // already walkable; no link wanted

            var target = mesh.Polys[landing];
            var end = target.ClampXZ(probe.X, probe.Z);
            float drop = point.Y - end.Y;
            bool isDrop = drop > settings.MaxJumpUp;

            if (isDrop && haveDrop) continue;            // already have the nearest drop
            if (!HasClearance(point, end, settings)) continue;

            Record(best, new NavLink
            {
                FromPoly = fromPoly,
                ToPoly = landing,
                Start = point,
                End = end,
                // A meaningful descent is a drop: one-way, because falling is not reversible.
                Kind = isDrop ? NavLinkKind.Drop : NavLinkKind.Jump,
                Bidirectional = !isDrop,
                CostMultiplier = isDrop ? settings.DropCost : settings.JumpCost,
            });

            if (isDrop) haveDrop = true;
            else haveLevel = true;
        }
    }

    private static void Record(Dictionary<(int, int), NavLink> best, NavLink link)
    {
        var key = (link.FromPoly, link.ToPoly);
        if (!best.TryGetValue(key, out var existing) || link.Length < existing.Length)
            best[key] = link;
    }

    /// <summary>Polygon a probe point would land on: the highest one under it within the drop
    /// limit, or one slightly above it within the jump-up limit.</summary>
    private static int FindLanding(NavMesh mesh, Vector3 probe, float fromY, NavLinkSettings settings)
    {
        int best = -1;
        float bestY = float.MinValue;

        for (int i = 0; i < mesh.Polys.Count; i++)
        {
            var poly = mesh.Polys[i];
            if (!poly.ContainsXZ(probe.X, probe.Z)) continue;

            float dy = fromY - poly.Y;
            if (dy > settings.MaxDropHeight) continue;      // too far to fall
            if (dy < -settings.MaxJumpUp) continue;         // too high to reach

            if (poly.Y > bestY) { bestY = poly.Y; best = i; }
        }
        return best;
    }

    private static bool SharesPortal(NavPoly poly, int other)
    {
        for (int i = 0; i < poly.Portals.Count; i++)
            if (poly.Portals[i].Neighbour == other) return true;
        return false;
    }

    /// <summary>Is there clear air between the two ends, at roughly chest height? This is what
    /// stops the generator linking through walls.</summary>
    private static bool HasClearance(Vector3 a, Vector3 b, NavLinkSettings settings)
    {
        float lift = settings.AgentHeight * 0.5f;
        var from = a + new Vector3(0, lift, 0);
        var to = b + new Vector3(0, lift, 0);
        var delta = to - from;
        float distance = delta.Length();
        if (distance < 1e-4f) return false;
        return !PhysicsWorld.Raycast(from, delta / distance, distance, out _, settings.LayerMask);
    }

    /// <summary>
    /// Points along a polygon's edges that no portal covers, each with the outward normal of the
    /// side it came from.
    ///
    /// <para>Subtracting the portal spans from each side is the whole trick: what remains is
    /// exactly the boundary where the walkable surface stops, which is where a jump or drop can
    /// begin.</para>
    /// </summary>
    private static IEnumerable<(Vector3 Point, Vector3 Outward)> OpenEdgeSamples(
        NavMesh mesh, int index, float spacing)
    {
        var poly = mesh.Polys[index];
        const float Eps = 1e-3f;
        float step = MathF.Max(spacing, 0.05f);

        // side, fixed coordinate, span to cover, outward direction, whether the span is along Z
        var sides = new (float Fixed, float Lo, float Hi, Vector3 Out, bool AlongZ)[]
        {
            (poly.MaxX, poly.MinZ, poly.MaxZ, Vector3.UnitX, true),
            (poly.MinX, poly.MinZ, poly.MaxZ, -Vector3.UnitX, true),
            (poly.MaxZ, poly.MinX, poly.MaxX, Vector3.UnitZ, false),
            (poly.MinZ, poly.MinX, poly.MaxX, -Vector3.UnitZ, false),
        };

        foreach (var (fixedCoord, lo, hi, outward, alongZ) in sides)
        {
            var covered = new List<(float Lo, float Hi)>();
            foreach (var portal in poly.Portals)
            {
                bool onThisSide = alongZ
                    ? MathF.Abs(portal.A.X - fixedCoord) < Eps && MathF.Abs(portal.B.X - fixedCoord) < Eps
                    : MathF.Abs(portal.A.Z - fixedCoord) < Eps && MathF.Abs(portal.B.Z - fixedCoord) < Eps;
                if (!onThisSide) continue;

                float p0 = alongZ ? portal.A.Z : portal.A.X;
                float p1 = alongZ ? portal.B.Z : portal.B.X;
                covered.Add((MathF.Min(p0, p1), MathF.Max(p0, p1)));
            }

            for (float t = lo + step * 0.5f; t < hi; t += step)
            {
                bool blocked = false;
                for (int i = 0; i < covered.Count && !blocked; i++)
                    blocked = t >= covered[i].Lo - Eps && t <= covered[i].Hi + Eps;
                if (blocked) continue;

                var point = alongZ
                    ? new Vector3(fixedCoord, poly.Y, t)
                    : new Vector3(t, poly.Y, fixedCoord);
                yield return (point, outward);
            }
        }
    }

    /// <summary>Link rectangles that touch along an edge and are within step height of each other.
    /// The shared segment becomes the portal the funnel steers through.</summary>
    private static void LinkAdjacent(NavMesh mesh, float stepHeight)
    {
        var polys = mesh.Polys;
        const float Touch = 1e-3f;

        for (int a = 0; a < polys.Count; a++)
        for (int b = a + 1; b < polys.Count; b++)
        {
            var pa = polys[a];
            var pb = polys[b];
            if (MathF.Abs(pa.Y - pb.Y) > stepHeight) continue;

            // Vertical shared edge: one's right side touching the other's left.
            bool xTouch = MathF.Abs(pa.MaxX - pb.MinX) < Touch || MathF.Abs(pb.MaxX - pa.MinX) < Touch;
            if (xTouch)
            {
                float z0 = MathF.Max(pa.MinZ, pb.MinZ), z1 = MathF.Min(pa.MaxZ, pb.MaxZ);
                if (z1 - z0 > Touch)
                {
                    float x = MathF.Abs(pa.MaxX - pb.MinX) < Touch ? pa.MaxX : pb.MaxX;
                    float y = (pa.Y + pb.Y) * 0.5f;
                    mesh.Link(a, b, new Vector3(x, y, z0), new Vector3(x, y, z1));
                    continue;
                }
            }

            bool zTouch = MathF.Abs(pa.MaxZ - pb.MinZ) < Touch || MathF.Abs(pb.MaxZ - pa.MinZ) < Touch;
            if (zTouch)
            {
                float x0 = MathF.Max(pa.MinX, pb.MinX), x1 = MathF.Min(pa.MaxX, pb.MaxX);
                if (x1 - x0 > Touch)
                {
                    float z = MathF.Abs(pa.MaxZ - pb.MinZ) < Touch ? pa.MaxZ : pb.MaxZ;
                    float y = (pa.Y + pb.Y) * 0.5f;
                    mesh.Link(a, b, new Vector3(x0, y, z), new Vector3(x1, y, z));
                }
            }
        }
    }
}

/// <summary>Controls automatic off-mesh link generation. See
/// <see cref="NavMeshBuilder.GenerateLinks"/>.</summary>
public sealed class NavLinkSettings
{
    /// <summary>Furthest horizontal gap an agent can cross. Gaps wider than this are left
    /// unconnected.</summary>
    public float MaxJumpDistance = 3f;

    /// <summary>Furthest an agent may fall. Drops beyond this are not linked — better a dead end
    /// than a route that kills the agent taking it.</summary>
    public float MaxDropHeight = 4f;

    /// <summary>How far up a jump may also climb. Small: leaping onto a ledge above you is much
    /// harder than dropping off one, and over-generous values produce links agents cannot make.</summary>
    public float MaxJumpUp = 0.8f;

    /// <summary>Spacing of probe points along a polygon's open edges. Finer finds more links and
    /// costs more; the dedupe pass keeps the count sane either way.</summary>
    public float SampleSpacing = 1f;

    /// <summary>Agent height, for the clearance check between the two ends.</summary>
    public float AgentHeight = 1.8f;

    /// <summary>Layers treated as blocking when checking a link has clear air along it.</summary>
    public int LayerMask = ~0;

    /// <summary>Cost multiplier given to generated jump links. Above 1 so the pathfinder walks
    /// around when walking around is reasonable.</summary>
    public float JumpCost = 2.5f;

    /// <summary>Cost multiplier for generated drops. Lower than a jump — dropping off a ledge is
    /// usually the natural move rather than a risk.</summary>
    public float DropCost = 1.5f;

    public bool Verbose = true;
}
