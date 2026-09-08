#:project ../src/BdvEngine/BdvEngine.csproj
// Navmesh gate.
//
//   dotnet run sketches/navmesh_test.cs -- --shot /tmp/nav.png --frames 30
//
// A walled room with one doorway, a raised platform reached by a ramp, and a sealed chamber. Each
// exists to test something a navmesh has to get right:
//
//   the wall     — a path must go AROUND it, not through, so its length exceeds the straight line
//   the doorway  — erosion must leave it passable for the agent's width, not seal it
//   the ramp     — step-height linking must connect two levels the agent can actually walk between
//   the chamber  — an unreachable goal must FAIL rather than return a path to somewhere near it
//
// The last one is the assertion that catches the most dangerous class of bug: a pathfinder that
// quietly returns its best effort sends agents walking confidently into walls forever.
using BdvEngine;
using System;
using System.Collections.Generic;
using System.Numerics;

NavMesh nav = null!;
SimObject walker = null!;
NavAgent agent = null!;
int checks = 0, failed = 0;

var start = new Vector3(-8f, 0f, -8f);
var goal = new Vector3(8f, 0f, 8f);
var sealedSpot = new Vector3(0f, 0f, 14f);
var platformTop = new Vector3(12f, 2f, -12f);
var islandTop = new Vector3(12f, 2f, -21f);

Sketch.Run(setup: w =>
{
    w.Camera.Perspective(fovDegrees: 55, near: 0.3f, far: 200f);
    w.Camera.Position = new Vector3(0, 26f, 26f);
    w.Camera.Target = new Vector3(0, 0, 0);
    w.Environment.Sky = new Vector3(0.42f, 0.50f, 0.62f);
    w.Environment.Ambient = new Vector3(0.34f, 0.35f, 0.40f);
    w.Add(new DirectionalLight(new Vector3(-0.4f, -1f, -0.35f)));
    w.Environment.Shadows.Distance = 40f;

    string floorMat = Materials.Standard("floor", new Color(96, 104, 96));
    string wallMat = Materials.Standard("wall", new Color(150, 128, 108));
    string rampMat = Materials.Standard("ramp", new Color(120, 132, 150));

    // ── ground: a real collider, because the bake reads the physics world, not the render list ──
    Box(w, "ground", new Vector3(0, -0.5f, 0), new Vector3(44, 1, 44), floorMat);

    // ── a wall across the middle with a doorway near +X ──
    Box(w, "wall_west", new Vector3(-6f, 1.5f, 0f), new Vector3(24, 3, 1), wallMat);
    Box(w, "wall_east", new Vector3(11.5f, 1.5f, 0f), new Vector3(7, 3, 1), wallMat);
    // gap between them spans x in [6, 8]: 2m wide, comfortably passable for a 0.35m-radius agent

    // ── a sealed chamber: four walls, no door. Nothing may path into it. ──
    Box(w, "cell_n", new Vector3(0f, 1.5f, 17f), new Vector3(8, 3, 1), wallMat);
    Box(w, "cell_s", new Vector3(0f, 1.5f, 11f), new Vector3(8, 3, 1), wallMat);
    Box(w, "cell_w", new Vector3(-4f, 1.5f, 14f), new Vector3(1, 3, 7), wallMat);
    Box(w, "cell_e", new Vector3(4f, 1.5f, 14f), new Vector3(1, 3, 7), wallMat);

    // ── a platform (z from -16 to -8, top at y=2) with steps climbing to its edge ──
    Box(w, "platform", new Vector3(12f, 1f, -12f), new Vector3(8, 2, 8), rampMat);
    // Steps ascend toward the platform and must stay OUTSIDE its footprint, or the ones that would
    // reach the top are buried inside it and the climb dead-ends partway up.
    for (int i = 0; i < 6; i++)
    {
        float top = 2f * (i + 1) / 6f;                       // 0.33 .. 2.00, one step per 0.33m
        Box(w, $"step{i}", new Vector3(12f, top * 0.5f, -3f - i * 0.9f),
            new Vector3(4, top, 1), rampMat);
    }

    // ── an island the same height as the platform, across a 2m gap. Reachable ONLY by jumping:
    //    it stands 2m above the ground and MaxJumpUp is well under that, so there is no way up
    //    from below and no walkable route at all. ──
    Box(w, "island", new Vector3(12f, 1f, -21f), new Vector3(6, 2, 6), rampMat);

    // ── the agent ──
    walker = new SimObject(9500, "walker");
    walker.Transform.Position = start;
    walker.AddComponent(new MeshComponent(Primitives.Sphere(14, 10).Mesh,
                                          Materials.Standard("agent", new Color(220, 90, 70))));
    walker.Transform.Scale = new Vector3(0.7f);
    var capsule = new CapsuleCollider(0.35f, 1.8f, new Vector3(0, 0.9f, 0));
    walker.AddComponent(capsule);
    walker.AddComponent(new CharacterController(capsule));
    w.Add(walker);

    // Colliders register on AddComponent, but their world bounds come from baked matrices — the
    // bake raycasts against them, so it must happen after the transforms are current.
    w.Scene.RebakeMatrices();

    nav = NavMeshBuilder.Build(new NavBakeSettings
    {
        Bounds = new Bounds(new Vector3(-24, -2, -26), new Vector3(24, 12, 24)),
        CellSize = 0.4f,
        AgentRadius = 0.35f,
        AgentHeight = 1.8f,
        SlopeLimitDegrees = 50f,
        StepHeight = 0.45f,
    });

    agent = new NavAgent(nav) { Speed = 6f };
    walker.AddComponent(agent);

    RunChecks();
});

void RunChecks()
{
    Console.WriteLine();
    // Machine-readable, separately from the human-formatted bake line: scraping "10,868" out of a
    // thousands-separated summary is how a gate ends up parsing "868".
    Console.WriteLine($"NAV POLYS={nav.Polys.Count} PORTALS={nav.PortalCount()} " +
                      $"AREA={nav.TotalArea():F0} CELLS={nav.WalkableCells}");

    Check("mesh has polygons", nav.Polys.Count > 0, $"{nav.Polys.Count} polys");
    Check("polygons are connected", nav.PortalCount() > 0, $"{nav.PortalCount()} portals");

    // ── the wall forces a detour ──
    var path = new List<Vector3>();
    bool found = nav.FindPath(start, goal, path);
    float straight = Vector3.Distance(start, goal);
    float walked = Length(path);

    Console.WriteLine($"NAV PATH found={found} waypoints={path.Count} length={walked:F2} straight={straight:F2}");
    Check("path across the wall exists", found && path.Count >= 2, $"{path.Count} waypoints");

    // Where the path crosses the wall line (z=0) is the real assertion. A length ratio only says
    // the route is longer than a straight line and needs a magic threshold -- and the honest
    // threshold here is ~1.11x, since that IS the optimal detour through this doorway, so any
    // stricter bound demands a path worse than the best one. Crossing position is exact: the wall
    // spans z=0 everywhere except x in [6, 8], so a crossing outside that gap went through it.
    int crossings = 0, throughWall = 0;
    for (int i = 0; i < path.Count - 1; i++)
    {
        var p0 = path[i];
        var p1 = path[i + 1];
        if (p0.Z == p1.Z || (p0.Z < 0f) == (p1.Z < 0f)) continue;
        crossings++;
        float t = -p0.Z / (p1.Z - p0.Z);
        float x = p0.X + t * (p1.X - p0.X);
        if (x < 5.9f || x > 8.1f) throughWall++;
    }
    Check("crosses only at the doorway", crossings > 0 && throughWall == 0,
          $"{crossings} crossing(s) of z=0, {throughWall} outside the x=[6,8] gap");
    Check("path is a detour, not a straight line", found && walked > straight * 1.05f,
          $"{walked:F1}m vs {straight:F1}m straight ({walked / straight:F2}x)");

    // Every waypoint must be somewhere an agent can stand. A path that leaves the mesh is a path
    // through a wall.
    int offMesh = 0;
    foreach (var p in path) if (!nav.IsWalkable(p, 1.0f)) offMesh++;
    Check("every waypoint is on the mesh", offMesh == 0, $"{offMesh} of {path.Count} off-mesh");

    // Sample along each leg too: the corners can be legal while the line between them is not.
    int offLeg = 0, sampled = 0;
    for (int i = 0; i < path.Count - 1; i++)
        for (float t = 0.1f; t < 0.95f; t += 0.1f)
        {
            sampled++;
            if (!nav.IsWalkable(Vector3.Lerp(path[i], path[i + 1], t), 1.2f)) offLeg++;
        }
    Check("legs stay on the mesh", offLeg == 0, $"{offLeg} of {sampled} samples off-mesh");

    // ── the sealed chamber must be unreachable ──
    var blocked = new List<Vector3>();
    bool reachedCell = nav.FindPath(start, sealedSpot, blocked);
    float endGap = reachedCell && blocked.Count > 0
        ? Vector3.Distance(blocked[^1], sealedSpot) : 0f;
    Check("sealed chamber is unreachable", !reachedCell || endGap > 2f,
          reachedCell ? $"path ended {endGap:F1}m short (never entered)" : "no path returned");

    // ── the ramp links two levels ──
    var up = new List<Vector3>();
    bool climbed = nav.FindPath(start, platformTop, up);
    Check("ramp connects to the platform", climbed && up.Count > 1 && up[^1].Y > 1.2f,
          climbed ? $"reaches y={up[^1].Y:F2} in {up.Count} waypoints" : "no path up");

    // ── straight line where nothing is in the way ──
    var open = new List<Vector3>();
    var a = new Vector3(-14f, 0f, -14f);
    var b = new Vector3(-4f, 0f, -4f);
    nav.FindPath(a, b, open);
    float openLen = Length(open), openStraight = Vector3.Distance(a, b);
    Check("open ground gives a straight path", openLen < openStraight * 1.05f,
          $"{openLen:F2}m vs {openStraight:F2}m straight ({open.Count} waypoints)");

    // ── off-mesh links ──────────────────────────────────────────────────────
    // The island is across a gap and 2m above the ground, so walking cannot reach it. That has to
    // be true BEFORE links are generated, or the rest of this proves nothing.
    var toIsland = new List<Vector3>();
    bool walkable = nav.FindPath(start, islandTop, toIsland);
    bool reachedIsland = walkable && toIsland.Count > 0
                         && Vector3.Distance(toIsland[^1], islandTop) < 3f;
    Check("island unreachable on foot", !reachedIsland, "no walkable route across the gap");

    int links = NavMeshBuilder.GenerateLinks(nav, new NavLinkSettings
    {
        MaxJumpDistance = 3f, MaxDropHeight = 4f, MaxJumpUp = 0.8f,
        SampleSpacing = 0.8f, AgentHeight = 1.8f,
    });
    Console.WriteLine($"NAV LINKS={links}");
    Check("links were generated", links > 0, $"{links} links from open edges");

    // THE assertion for link generation. Two polys either side of a wall are near each other and
    // unconnected, which is exactly the shape the generator hunts for -- so without the clearance
    // ray it links straight through walls, and the sealed chamber springs open.
    var stillSealed = new NavPath();
    bool cellNowReachable = nav.FindPath(start, sealedSpot, stillSealed)
                            && stillSealed.Count > 0
                            && Vector3.Distance(stillSealed[^1].Position, sealedSpot) < 2f;
    Check("links never cross walls", !cellNowReachable,
          cellNowReachable ? "SEALED CHAMBER BREACHED" : "sealed chamber still sealed");

    var jumpPath = new NavPath();
    bool nowReachable = nav.FindPath(start, islandTop, jumpPath);
    bool landed = nowReachable && jumpPath.Count > 0
                  && Vector3.Distance(jumpPath[^1].Position, islandTop) < 3f;
    Check("island reachable via a link", landed && jumpPath.UsesLinks,
          landed ? $"{jumpPath.Count} waypoints, usesLinks={jumpPath.UsesLinks}" : "still unreachable");

    int linkSteps = 0;
    NavLinkKind usedKind = NavLinkKind.Custom;
    for (int i = 0; i < jumpPath.Count; i++)
        if (jumpPath[i].IsLinkStart) { linkSteps++; usedKind = jumpPath[i].Link!.Kind; }
    Check("path marks the link transition", linkSteps > 0,
          $"{linkSteps} link step(s), kind={usedKind}");

    // Drops are one-way: what falls off the island cannot climb back up the same way.
    bool anyOneWay = false;
    foreach (var l in nav.Links) if (!l.Bidirectional && l.Kind == NavLinkKind.Drop) anyOneWay = true;
    Check("drops are one-way", anyOneWay, "at least one non-reversible drop link exists");

    // ── the agent actually walks it, on a fixed timestep so the result is deterministic ──
    agent.SetDestination(goal);
    bool routed = !agent.PathFailed;
    int steps = 0;
    while (!agent.Arrived && steps < 2000)
    {
        walker.Update(1.0 / 60.0);
        steps++;
    }
    var end = walker.Transform.Position;
    float miss = new Vector2(end.X - goal.X, end.Z - goal.Z).Length();
    Console.WriteLine($"NAV WALK routed={routed} steps={steps} ended=({end.X:F2},{end.Z:F2}) miss={miss:F2}");
    Check("agent walks the path", routed && agent.Arrived && miss < 1.5f,
          $"{steps} steps ({steps / 60f:F1}s), {miss:F2}m from the goal");

    // ── and traverses a link end to end ──
    walker.Transform.Position = start;
    agent.SetDestination(islandTop);
    bool jumped = false;
    int jumpSteps = 0;
    while (!agent.Arrived && jumpSteps < 4000)
    {
        walker.Update(1.0 / 60.0);
        if (agent.TraversingLink != null) jumped = true;
        jumpSteps++;
    }
    var landedAt = walker.Transform.Position;
    float islandMiss = new Vector2(landedAt.X - islandTop.X, landedAt.Z - islandTop.Z).Length();
    Console.WriteLine($"NAV JUMP arrived={agent.Arrived} traversed={jumped} steps={jumpSteps} " +
                      $"ended=({landedAt.X:F2},{landedAt.Y:F2},{landedAt.Z:F2})");
    Check("agent traverses the link", agent.Arrived && jumped && islandMiss < 3f && landedAt.Y > 1.2f,
          $"{jumpSteps} steps, landed y={landedAt.Y:F2}, {islandMiss:F2}m from target");

    Console.WriteLine();
    Console.WriteLine(failed == 0
        ? $"NAVMESH PASS — {checks} checks, {nav.Polys.Count} polys"
        : $"NAVMESH FAIL — {failed} of {checks} checks failed");
}

void Check(string name, bool ok, string detail)
{
    checks++;
    if (!ok) failed++;
    Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name,-36} {detail}");
}

static float Length(List<Vector3> p)
{
    float d = 0f;
    for (int i = 0; i < p.Count - 1; i++) d += Vector3.Distance(p[i], p[i + 1]);
    return d;
}

static void Box(World w, string name, Vector3 center, Vector3 size, string material)
{
    var o = new SimObject(w.NextId(), name);
    o.Transform.Position = center;
    o.Transform.Scale = size;
    o.AddComponent(new MeshComponent(Primitives.Cube().Mesh, material));
    o.AddComponent(new BoxCollider(Vector3.One));   // size 1 local, scaled by the transform
    w.Add(o);
}
