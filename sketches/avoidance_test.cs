#:project ../src/BdvEngine/BdvEngine.csproj
// Local avoidance gate.
//
//   dotnet run sketches/avoidance_test.cs -- --shot /tmp/avoid.png --frames 30
//   dotnet run sketches/avoidance_test.cs -- --shot /tmp/avoid_off.png --frames 30 --noavoid
//
// Twelve agents on a circle, each walking to the point diametrically opposite. Every route passes
// through the centre at the same moment, which is the worst case on purpose — it is the standard
// stress test for reciprocal avoidance because a naive solver either lets everyone interpenetrate
// or locks the whole crowd solid.
//
// Agents deliberately have NO colliders. If they did, physics would keep them apart and the test
// would pass whether or not avoidance worked at all; without them, every metre of separation
// measured here came from the steering.
//
// --noavoid is the control. It has to FAIL to separate, or the measurement proves nothing.
using BdvEngine;
using System;
using System.Collections.Generic;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
bool avoid = Array.IndexOf(cli, "--noavoid") < 0;

const int Count = 12;
const float Ring = 9f;
const float AgentRadius = 0.35f;
const float Combined = AgentRadius * 2f;

var agents = new List<NavAgent>();
var bodies = new List<SimObject>();
int checks = 0, failed = 0;

Sketch.Run(setup: w =>
{
    w.Camera.Perspective(fovDegrees: 52, near: 0.3f, far: 200f);
    w.Camera.Position = new Vector3(0, 22f, 20f);
    w.Camera.Target = new Vector3(0, 0, 0);
    w.Environment.Sky = new Vector3(0.44f, 0.52f, 0.63f);
    w.Environment.Ambient = new Vector3(0.36f, 0.37f, 0.42f);
    w.Add(new DirectionalLight(new Vector3(-0.4f, -1f, -0.35f)));
    w.Environment.Shadows.Distance = 34f;

    var ground = new SimObject(w.NextId(), "ground");
    ground.Transform.Position = new Vector3(0, -0.5f, 0);
    ground.Transform.Scale = new Vector3(48, 1, 48);
    ground.AddComponent(new MeshComponent(Primitives.Cube().Mesh,
                                          Materials.Standard("ground", new Color(92, 100, 94))));
    ground.AddComponent(new BoxCollider(Vector3.One));
    w.Add(ground);
    w.Scene.RebakeMatrices();

    var nav = NavMeshBuilder.Build(new NavBakeSettings
    {
        Bounds = new Bounds(new Vector3(-24, -2, -24), new Vector3(24, 8, 24)),
        CellSize = 0.6f, AgentRadius = AgentRadius, AgentHeight = 1.8f, Verbose = false,
    });

    AvoidanceWorld.Clear();

    for (int i = 0; i < Count; i++)
    {
        float angle = i / (float)Count * MathF.Tau;
        var start = new Vector3(MathF.Cos(angle) * Ring, 0f, MathF.Sin(angle) * Ring);

        var body = new SimObject(9600 + i, $"agent{i}");
        body.Transform.Position = start;
        body.Transform.Scale = new Vector3(AgentRadius * 2f);
        body.AddComponent(new MeshComponent(Primitives.Sphere(14, 10).Mesh,
                                            Materials.Standard($"a{i % 3}",
                                                i % 3 == 0 ? new Color(214, 96, 78)
                                              : i % 3 == 1 ? new Color(96, 158, 214)
                                                           : new Color(214, 190, 96))));

        var agent = new NavAgent(nav)
        {
            Speed = 2.4f, Radius = AgentRadius, Avoidance = avoid,
            ArriveRadius = 0.4f, StoppingDistance = 0.25f,
        };
        body.AddComponent(agent);
        w.Add(body);
        body.Load();                       // registers with AvoidanceWorld

        agent.SetDestination(-start);      // straight across the ring, through the middle
        agents.Add(agent);
        bodies.Add(body);
    }

    Console.WriteLine($"[avoid] {Count} agents crossing a {Ring * 2:F0}m ring, avoidance={avoid}");
    Simulate();
});

void Simulate()
{
    // Fixed timestep: the result must not depend on how fast the machine renders.
    const double Dt = 1.0 / 60.0;
    const int MaxSteps = 2400;

    float minSeparation = float.MaxValue;
    int overlapSteps = 0;
    int steps = 0;

    while (steps < MaxSteps)
    {
        foreach (var b in bodies) b.Update(Dt);
        steps++;

        bool anyOverlap = false;
        for (int i = 0; i < bodies.Count; i++)
        for (int j = i + 1; j < bodies.Count; j++)
        {
            var a = bodies[i].Transform.Position;
            var b = bodies[j].Transform.Position;
            float d = new Vector2(a.X - b.X, a.Z - b.Z).Length();
            if (d < minSeparation) minSeparation = d;
            if (d < Combined * 0.9f) anyOverlap = true;
        }
        if (anyOverlap) overlapSteps++;

        bool allDone = true;
        foreach (var a in agents) if (!a.Arrived) { allDone = false; break; }
        if (allDone) break;
    }

    int arrived = 0;
    float worstMiss = 0f;
    for (int i = 0; i < agents.Count; i++)
    {
        if (agents[i].Arrived) arrived++;
        float angle = i / (float)Count * MathF.Tau;
        var target = new Vector3(-MathF.Cos(angle) * Ring, 0f, -MathF.Sin(angle) * Ring);
        var p = bodies[i].Transform.Position;
        worstMiss = MathF.Max(worstMiss, new Vector2(p.X - target.X, p.Z - target.Z).Length());
    }

    Console.WriteLine($"AVOID STEPS={steps} MINSEP={minSeparation:F3} OVERLAPSTEPS={overlapSteps} " +
                      $"ARRIVED={arrived} WORSTMISS={worstMiss:F2}");
    Console.WriteLine();

    if (avoid)
    {
        // Bodies are 0.7m across combined. ORCA permits a little compression under crowd pressure,
        // so the bar is "clearly separated", not "never within a hair of touching".
        Check("agents keep their distance", minSeparation > Combined * 0.75f,
              $"closest approach {minSeparation:F2}m (bodies are {Combined:F2}m combined)");
        Check("no sustained interpenetration", overlapSteps < steps * 0.02,
              $"{overlapSteps} of {steps} steps with any overlap");
        Check("everyone arrives", arrived == Count, $"{arrived}/{Count} reached the far side");
        Check("no deadlock", steps < 2000, $"resolved in {steps} steps ({steps / 60f:F1}s)");
        Check("paths stay efficient", worstMiss < 1.0f,
              $"worst agent ended {worstMiss:F2}m from its target");
    }
    else
    {
        // The control. Without avoidance every agent drives straight through the middle at once,
        // so they must pile into each other -- if they somehow don't, this whole measurement is
        // testing nothing and the numbers above are meaningless.
        Check("control: agents DO collide without it", minSeparation < Combined * 0.5f,
              $"closest approach {minSeparation:F2}m (bodies are {Combined:F2}m combined)");
        Check("control: overlap is sustained", overlapSteps > 0,
              $"{overlapSteps} of {steps} steps with overlap");
    }

    Console.WriteLine();
    Console.WriteLine(failed == 0
        ? $"AVOIDANCE PASS — {checks} checks (avoidance={avoid})"
        : $"AVOIDANCE FAIL — {failed} of {checks} checks failed");
}

void Check(string name, bool ok, string detail)
{
    checks++;
    if (!ok) failed++;
    Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name,-38} {detail}");
}
