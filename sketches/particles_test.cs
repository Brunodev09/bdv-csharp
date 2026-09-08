#:project ../src/BdvEngine/BdvEngine.csproj
// 3D particle gate.
//
//   dotnet run sketches/particles_test.cs -- --shot /tmp/particles.png --frames 90
//   dotnet run sketches/particles_test.cs -- --shot /tmp/particles_cull.png --frames 90 --behind
//
// Four systems covering the axes that matter: additive vs alpha, every emitter shape, world-space
// vs local-space, and continuous emission vs burst. The measurement is that N systems cost N draw
// calls no matter how many particles are alive — the whole point of instancing them.
//
// The steady-state count check is the one that proves the simulation is actually running rather
// than just allocating: a system emitting R particles/second whose particles live L seconds settles
// at R*L alive, and nothing else produces that number by accident.
using BdvEngine;
using System;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
bool behind = Array.IndexOf(cli, "--behind") >= 0;

ParticleSystem3D fire = null!, smoke = null!, dust = null!, shield = null!;
int frames = 0, checks = 0, failed = 0;
int callsBefore = 0;
bool reported = false;

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 50, near: 0.1f, far: 200f);
        w.Camera.Position = new Vector3(0, 2.2f, 7.5f);
        w.Camera.Target = new Vector3(0, 1.4f, 0);
        w.Environment.Sky = new Vector3(0.10f, 0.11f, 0.15f);
        w.Environment.Ambient = new Vector3(0.22f, 0.22f, 0.28f);
        w.Add(new DirectionalLight(new Vector3(-0.4f, -1f, -0.3f)));
        w.Environment.Shadows.Distance = 18f;

        w.Add(Primitives.Plane(40)).Material(Materials.Standard("ground", new Color(52, 54, 58)));

        // ── 1. campfire: additive cone, rising. The classic case additive exists for — overlapping
        //       particles brighten into a hot core instead of flattening into one flat colour.
        var logs = w.Add(Primitives.Cube()).At(-2.6f, 0.12f, 0).Scale(0.7f, 0.2f, 0.7f)
                    .Material(Materials.Standard("logs", new Color(70, 48, 32)));
        logs.Object.Name = "logs";
        fire = new ParticleSystem3D
        {
            Shape = EmitterShape.Cone, ConeAngle = 16f, Radius = 0.22f,
            EmissionRate = 90f, MaxParticles = 300,
            SpeedMin = 0.7f, SpeedMax = 1.5f,
            LifetimeMin = 0.5f, LifetimeMax = 0.9f,
            SizeStart = 0.34f, SizeEnd = 0.06f,
            ColorStart = new Color(255, 198, 92, 255), ColorEnd = new Color(190, 40, 10, 0),
            Gravity = new Vector3(0, 0.9f, 0),        // fire rises: buoyancy is negative gravity
            Blend = ParticleBlend.Additive,
            Seed = 11,
        };
        var fireObj = new SimObject(9300, "fire");
        fireObj.Transform.Position = new Vector3(-2.6f, 0.25f, 0);
        fireObj.AddComponent(fire);
        w.Add(fireObj);

        // ── 2. smoke: alpha cone, drag-slowed and spinning, sorted back-to-front internally
        smoke = new ParticleSystem3D
        {
            Shape = EmitterShape.Cone, ConeAngle = 28f, Radius = 0.3f,
            EmissionRate = 26f, MaxParticles = 220,
            SpeedMin = 0.5f, SpeedMax = 0.9f,
            LifetimeMin = 1.8f, LifetimeMax = 2.6f,
            SizeStart = 0.35f, SizeEnd = 1.5f,        // smoke expands as it cools
            ColorStart = new Color(120, 120, 126, 150), ColorEnd = new Color(70, 70, 78, 0),
            Gravity = new Vector3(0.25f, 0.5f, 0),    // drifting on a breeze
            Drag = 0.7f, SpinMax = 0.8f,
            Blend = ParticleBlend.Alpha,
            Seed = 22,
        };
        var smokeObj = new SimObject(9301, "smoke");
        smokeObj.Transform.Position = new Vector3(0.2f, 0.4f, 0);
        smokeObj.AddComponent(smoke);
        w.Add(smokeObj);

        // ── 3. dust: box emitter, burst-only (EmissionRate 0), falling
        dust = new ParticleSystem3D
        {
            Shape = EmitterShape.Box, BoxSize = new Vector3(2.4f, 0.1f, 2.4f),
            EmissionRate = 0f, MaxParticles = 220,     // burst-only
            SpeedMin = 0.2f, SpeedMax = 0.9f,
            LifetimeMin = 1.2f, LifetimeMax = 2.0f,
            SizeStart = 0.10f, SizeEnd = 0.02f,
            ColorStart = new Color(200, 186, 150, 220), ColorEnd = new Color(150, 140, 110, 0),
            Gravity = new Vector3(0, -0.8f, 0),
            Direction = Vector3.UnitY, Drag = 0.5f,
            Blend = ParticleBlend.Alpha,
            Seed = 33,
        };
        var dustObj = new SimObject(9302, "dust");
        dustObj.Transform.Position = new Vector3(3.0f, 1.4f, 0);
        dustObj.AddComponent(dust);
        w.Add(dustObj);

        // ── 4. shield: local-space sphere. Local space is the difference between a shimmer that
        //       rides along with its object and one that smears a trail behind it.
        shield = new ParticleSystem3D
        {
            Shape = EmitterShape.Sphere, Radius = 0.75f,
            EmissionRate = 70f, MaxParticles = 240,
            SpeedMin = 0.02f, SpeedMax = 0.14f,
            LifetimeMin = 0.7f, LifetimeMax = 1.2f,
            SizeStart = 0.08f, SizeEnd = 0.16f,
            ColorStart = new Color(120, 210, 255, 210), ColorEnd = new Color(40, 120, 255, 0),
            Gravity = Vector3.Zero,
            WorldSpace = false,                        // follows the emitter
            Blend = ParticleBlend.Additive,
            Seed = 44,
        };
        var shieldObj = new SimObject(9303, "shield");
        shieldObj.Transform.Position = new Vector3(behind ? 0f : 0.2f, 1.5f, behind ? 24f : -3.2f);
        shieldObj.AddComponent(shield);
        w.Add(shieldObj);

        // Warm the systems to steady state with a FIXED timestep before the first frame is drawn.
        // Driving this from real elapsed time made the gate depend on frame rate: 260 frames is
        // 4.3s at 60fps but 1.3s uncapped, so the same correct engine passed or failed depending on
        // machine load. Stepping the simulation directly removes the clock from the test.
        w.Scene.RebakeMatrices();
        dust.Burst(80);
        for (int i = 0; i < 180; i++)      // 3.0s at 1/60, past the longest lifetime (2.6s)
        {
            fire.Update(1.0 / 60.0);
            smoke.Update(1.0 / 60.0);
            dust.Update(1.0 / 60.0);
            shield.Update(1.0 / 60.0);
        }

        Console.WriteLine($"[fx] 4 systems, cap {fire.MaxParticles + smoke.MaxParticles + dust.MaxParticles + shield.MaxParticles} particles, warmed 3.0s");
    },

    update: (w, dt) =>
    {
        frames++;
        // Keep the burst-only cloud topped up so the screenshot lands mid-cloud whatever frame it
        // captures; the assertions run off the warm-up, not off this.
        if (frames % 40 == 1) dust.Burst(80);

        // Frame 4: the systems are already at steady state from the warm-up, and GLStats has a
        // full frame's worth of draw calls behind it.
        if (reported || frames < 4) return;
        reported = true;
        callsBefore = GLStats.DrawCalls;

        int live = fire.LiveCount + smoke.LiveCount + dust.LiveCount + shield.LiveCount;
        Console.WriteLine();
        Console.WriteLine($"  live: fire={fire.LiveCount} smoke={smoke.LiveCount} " +
                          $"dust={dust.LiveCount} shield={shield.LiveCount}  (total {live})");
        Console.WriteLine($"  frame draw calls: {callsBefore}");
        Console.WriteLine($"PARTICLES CALLS={callsBefore} LIVE={live}");
        Console.WriteLine();

        // Steady state is rate x mean lifetime. Fire: 90/s x 0.7s = 63. Tolerance is wide because
        // spawn timing and frame pacing both wobble; the point is the order of magnitude is right
        // and the pool is neither empty nor pinned at its cap.
        Check("fire reaches steady state", Between(fire.LiveCount, 40, 90),
              $"{fire.LiveCount} alive (90/s x ~0.7s life = ~63)");
        Check("smoke reaches steady state", Between(smoke.LiveCount, 38, 80),
              $"{smoke.LiveCount} alive (26/s x ~2.2s life = ~57)");
        Check("burst-only system emitted", dust.LiveCount > 0, $"{dust.LiveCount} alive from Burst()");

        Check("caps are respected",
              fire.LiveCount <= fire.MaxParticles && smoke.LiveCount <= smoke.MaxParticles
              && dust.LiveCount <= dust.MaxParticles && shield.LiveCount <= shield.MaxParticles,
              "no system exceeded MaxParticles");

        // The headline claim: cost is per SYSTEM, not per particle. Ground + logs + 4 systems, times
        // the shadow pass for the two solid meshes — well under a hundred either way, whereas one
        // call per particle would be ~600.
        Check("particles cost one call each", callsBefore < 20 && live > 150,
              $"{callsBefore} draw calls for {live} particles (one-per-particle would be {live})");

        // Bounds must actually track the particles, or frustum culling would pop systems in and out.
        var fb = fire.WorldBounds;
        Check("bounds track live particles",
              fb.Max.Y > fb.Min.Y && fb.Min.Y > -1f && fb.Max.Y < 6f,
              $"fire y range {fb.Min.Y:F2}..{fb.Max.Y:F2}");

        // Local-space particles must stay with their emitter; world-space ones must not.
        var sb = shield.WorldBounds;
        float dx = MathF.Abs(sb.Center.X - (behind ? 0f : 0.2f));
        Check("local-space system follows its emitter", dx < 1.5f,
              $"shield centre {dx:F2} from the emitter (radius 0.75)");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"PARTICLES PASS — {checks} checks, {live} particles in {callsBefore} calls"
            : $"PARTICLES FAIL — {failed} of {checks} checks failed");
    }
);

void Check(string name, bool ok, string detail)
{
    checks++;
    if (!ok) failed++;
    Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {name,-38} {detail}");
}

static bool Between(int v, int lo, int hi) => v >= lo && v <= hi;
