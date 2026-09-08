#:project ../src/BdvEngine/BdvEngine.csproj
// 3D collision gate: the behaviours a character controller has to get right.
//
//   dotnet run sketches/physics_test.cs -- --shot /tmp/physics.png --frames 200
//
// Each case runs the real controller for a fixed number of fixed-step frames and asserts on where
// the character ends up. Fixed steps rather than real dt so the numbers are reproducible.
using BdvEngine;
using System;
using System.Collections.Generic;
using System.Numerics;

const float Dt = 1f / 60f;

var results = new List<(string name, bool pass, string detail)>();
World world = null!;

Sketch.Run(setup: w =>
{
    world = w;
    w.Camera.Perspective(fovDegrees: 50, near: 0.1f, far: 200f);
    w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 1.2f, 0), distance: 22f,
                                           yaw: 0.7f, pitch: 0.30f));
    w.Environment.Sky = new Vector3(0.50f, 0.59f, 0.72f);
    w.Environment.Ambient = new Vector3(0.34f, 0.35f, 0.40f);
    w.Add(new DirectionalLight(new Vector3(0.62f, -0.65f, 0.45f)));
    w.Environment.Shadows.Distance = 18f;

    // ── the level: floor, a wall, a low step, a ramp ──
    Box(w, "floor", new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40), new Color(122, 132, 116));
    Box(w, "wall", new Vector3(4.5f, 1.0f, 0), new Vector3(0.6f, 2.0f, 8), new Color(168, 108, 92));
    Box(w, "step", new Vector3(-4.0f, 0.15f, 0), new Vector3(2.0f, 0.30f, 6), new Color(150, 150, 158));
    Box(w, "tall", new Vector3(-8.0f, 0.60f, 0), new Vector3(1.5f, 1.20f, 6), new Color(120, 120, 132));

    RunAllCases(w);

    Console.WriteLine(new string('-', 72));
    bool all = true;
    foreach (var (name, pass, detail) in results)
    {
        Console.WriteLine($"  {(pass ? "ok  " : "FAIL")} {name,-34} {detail}");
        all &= pass;
    }
    Console.WriteLine(all ? "COLLISION PASS — character controller behaves on all cases"
                          : "COLLISION FAIL");
    Console.WriteLine(new string('-', 72));
});

static void Box(World w, string name, Vector3 pos, Vector3 size, Color color)
{
    var h = w.Add(Primitives.Cube()).At(pos).Scale(size.X, size.Y, size.Z)
             .Material(Materials.Standard(name, color));
    h.Object.Name = name;
    // Size 1 in LOCAL units: the object's own scale already carries the dimensions, exactly as a
    // Unity BoxCollider on a scaled cube would.
    h.Object.AddComponent(new BoxCollider(Vector3.One));
}

// A character: capsule collider + controller, centred so its feet sit at the object's origin.
static (SimObject obj, CharacterController cc) Spawn(World w, Vector3 feet, float radius = 0.35f,
                                                     float height = 1.8f)
{
    var o = new SimObject(w.NextId(), "character");
    o.Transform.Position = feet;
    w.Add(o);
    var capsule = new CapsuleCollider(radius, height, new Vector3(0, height * 0.5f, 0));
    o.AddComponent(capsule);
    var cc = new CharacterController(capsule);
    o.AddComponent(cc);
    w.Scene.RebakeMatrices();
    return (o, cc);
}

static void Step(CharacterController cc, Vector3 vel, int frames)
{
    for (int i = 0; i < frames; i++) cc.Move(vel, Dt);
}

void Check(string name, bool pass, string detail) => results.Add((name, pass, detail));

void RunAllCases(World w)
{
    // 1. Falls and lands on the floor, feet resting on the surface (floor top is y = 0).
    {
        var (o, cc) = Spawn(w, new Vector3(0, 4f, -6f));
        Step(cc, Vector3.Zero, 120);
        float feetY = o.Transform.Position.Y;
        Check("falls and lands on floor", cc.IsGrounded && MathF.Abs(feetY) < 0.06f,
              $"feetY={feetY:F3} grounded={cc.IsGrounded}");
        w.Scene.RemoveObject(o);
        PhysicsWorld.Unregister(cc.Capsule);
    }

    // 2. Walks into a wall: stopped, and never inside it.
    {
        var (o, cc) = Spawn(w, new Vector3(0f, 0.5f, 0f));
        Step(cc, Vector3.Zero, 30);                 // settle
        Step(cc, new Vector3(6f, 0, 0), 120);       // drive into the wall at x=4.5 (near face 4.2)
        float x = o.Transform.Position.X;
        Check("wall blocks movement", x < 4.2f - 0.35f + 0.1f && x > 3.0f,
              $"stopped at x={x:F3} (wall face 4.2, radius 0.35)");
        Check("wall reports HitWall", cc.HitWall, $"hitWall={cc.HitWall}");
        w.Scene.RemoveObject(o);
        PhysicsWorld.Unregister(cc.Capsule);
    }

    // 3. Steps up a 0.30 lip (StepOffset default 0.35).
    {
        var (o, cc) = Spawn(w, new Vector3(-2.0f, 0.5f, 0f));
        Step(cc, Vector3.Zero, 30);
        // 50 frames at 3 u/s covers 2.5 units, landing mid-step (the step spans x -5..-3). Walking
        // further would cross it and come down the far side, which is correct behaviour but tells
        // us nothing about whether it climbed.
        Step(cc, new Vector3(-3f, 0, 0), 50);
        float y = o.Transform.Position.Y;
        float x = o.Transform.Position.X;
        Check("climbs a 0.30 step", y > 0.24f && y < 0.40f && x < -3.0f && x > -5.0f,
              $"ended at x={x:F2} feetY={y:F3} (step spans -5..-3, top 0.30)");
        w.Scene.RemoveObject(o);
        PhysicsWorld.Unregister(cc.Capsule);
    }

    // 4. Does NOT climb a 1.20 wall-height block — that's a wall, not a step.
    {
        var (o, cc) = Spawn(w, new Vector3(-6.0f, 0.5f, 0f));
        Step(cc, Vector3.Zero, 30);
        Step(cc, new Vector3(-3f, 0, 0), 120);      // into the tall block at x=-8 (face -7.25)
        float y = o.Transform.Position.Y;
        Check("does not climb a 1.20 block", y < 0.30f,
              $"feetY={y:F3} (must stay near 0, block top 1.20)");
        w.Scene.RemoveObject(o);
        PhysicsWorld.Unregister(cc.Capsule);
    }

    // 5. Jump leaves the ground and gravity brings it back.
    {
        var (o, cc) = Spawn(w, new Vector3(0f, 0.5f, 6f));
        Step(cc, Vector3.Zero, 30);
        bool groundedBefore = cc.IsGrounded;
        cc.Jump(7f);
        Step(cc, Vector3.Zero, 6);
        float peak = o.Transform.Position.Y;
        bool airborne = !cc.IsGrounded && peak > 0.4f;
        Step(cc, Vector3.Zero, 150);
        float landed = o.Transform.Position.Y;
        Check("jump rises then lands",
              groundedBefore && airborne && cc.IsGrounded && MathF.Abs(landed) < 0.06f,
              $"peak={peak:F2} landed={landed:F3}");
        w.Scene.RemoveObject(o);
        PhysicsWorld.Unregister(cc.Capsule);
    }

    // 6. Terrain: a character walks up a heightfield hill and stays on the surface. This is the
    //    case that removes the hand-rolled grounding games currently write for themselves.
    {
        // 64 x 0.5 gives a 31.5-unit field spanning local +/-15.75, so the peak has to sit well
        // inside that — put it at x=8 and walk out to it from the centre.
        var terrain = new HeightmapTerrain(
            resolution: 64, cellSize: 0.5f,
            heightAt: (x, z) => 1.6f * MathF.Exp(-((x - 8f) * (x - 8f) + z * z) / 30f),
            colorAt: (x, z, h) => new Color(110, (byte)(140 + h * 40), 104),
            materialName: "hill");
        var tObj = terrain.CreateObject(w.NextId(), "hill");
        tObj.Transform.Position = new Vector3(0, 0, 24f);   // clear of the box level
        w.Add(tObj);
        tObj.AddComponent(new TerrainCollider(terrain));
        w.Scene.RebakeMatrices();

        // Terrain samples are in the terrain's own space; the object is offset in Z, so walk in
        // its local frame and compare against the same sampler the collider uses.
        var (o, cc) = Spawn(w, new Vector3(0f, 3f, 24f));
        Step(cc, Vector3.Zero, 90);                 // fall onto the hillside
        float restY = o.Transform.Position.Y;
        float expect0 = terrain.SampleHeight(0f, 0f);
        bool landedOnTerrain = cc.IsGrounded && MathF.Abs(restY - expect0) < 0.12f;
        string landDetail = $"restY={restY:F3} expected {expect0:F3} grounded={cc.IsGrounded}";

        Step(cc, new Vector3(3.2f, 0, 0), 150);     // walk uphill toward the peak at x=8
        float x2 = o.Transform.Position.X;
        float y2 = o.Transform.Position.Y;
        float expect2 = terrain.SampleHeight(x2, 0f);
        bool followedSurface = cc.IsGrounded && MathF.Abs(y2 - expect2) < 0.15f && y2 > restY + 0.3f;

        // Reported from the state captured at landing time, not after the walk.
        Check("lands on heightfield terrain", landedOnTerrain, landDetail);
        Check("walks uphill following surface", followedSurface,
              $"at x={x2:F2} feetY={y2:F3} terrain={expect2:F3}");

        w.Scene.RemoveObject(o);
        PhysicsWorld.Unregister(cc.Capsule);
    }

    // 7. Raycast: straight down from above the floor must hit it at the right distance.
    {
        bool hit = PhysicsWorld.Raycast(new Vector3(0, 5f, -10f), -Vector3.UnitY, 20f, out var rh);
        Check("raycast finds the floor",
              hit && MathF.Abs(rh.Distance - 5f) < 0.05f && rh.Normal.Y > 0.9f,
              hit ? $"dist={rh.Distance:F3} normal={rh.Normal:F2} on '{rh.Object?.Name}'" : "no hit");
    }

    // 8. Triggers are reported by overlap queries but never block movement.
    {
        var trig = new SimObject(w.NextId(), "trigger");
        trig.Transform.Position = new Vector3(0, 1f, -3f);
        w.Add(trig);
        trig.Transform.Scale = new Vector3(2, 2, 2);
        trig.AddComponent(new BoxCollider(Vector3.One) { IsTrigger = true });
        w.Scene.RebakeMatrices();

        var (o, cc) = Spawn(w, new Vector3(0f, 0.5f, -6f));
        Step(cc, Vector3.Zero, 30);

        // Sample every frame: the character passes THROUGH the trigger, so checking only at the
        // end sees nothing — which is exactly the point, since a trigger must not stop it.
        bool sawTrigger = false;
        for (int i = 0; i < 90; i++)
        {
            cc.Move(new Vector3(0, 0, 4f), Dt);
            var (a, b) = cc.Capsule.WorldSegment();
            foreach (var c in PhysicsWorld.OverlapCapsule(a, b, cc.Capsule.WorldRadius,
                                                          ignore: cc.Capsule, includeTriggers: true))
                if (c.IsTrigger) sawTrigger = true;
        }
        float z = o.Transform.Position.Z;
        Check("trigger overlaps but doesn't block", z > -0.5f && sawTrigger,
              $"walked through to z={z:F2}, trigger seen en route={sawTrigger}");

        w.Scene.RemoveObject(o);
        PhysicsWorld.Unregister(cc.Capsule);
    }

    // Leave a character standing ON the step for the screenshot — the case worth seeing.
    var (shown, shownCc) = Spawn(w, new Vector3(-2.0f, 1.2f, 0f));
    Step(shownCc, new Vector3(-3f, 0, 0), 50);
    // Stand-in body as a CHILD, so the marker sits at capsule height without moving the character
    // itself (which would put the collider somewhere the controller never placed it).
    var body = new SimObject(w.NextId(), "body");
    body.Transform.Position = new Vector3(0, 0.9f, 0);
    body.Transform.Scale = new Vector3(0.7f, 1.8f, 0.7f);
    body.AddComponent(new MeshComponent(Primitives.Sphere(20, 14).Mesh,
                                        Materials.Standard("body", new Color(214, 176, 122))));
    shown.AddChild(body);
}
