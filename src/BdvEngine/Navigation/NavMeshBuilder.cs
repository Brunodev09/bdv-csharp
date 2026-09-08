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
