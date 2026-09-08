#:project ../src/BdvEngine/BdvEngine.csproj
// Spatialised audio gate.
//
//   dotnet run sketches/audio3d_test.cs -- --frames 90 --shot /tmp/audio3d.png
//   dotnet run sketches/audio3d_test.cs -- --frames 90 --side left
//
// Places a looping tone on one side of the listener and reports what OpenAL was actually told:
// the source's world position read back from the driver, whether it is world-positioned rather
// than head-relative, and the predicted gain at several distances.
//
// --side left|right|front moves the emitter; --move slides it at a known speed.
//
// WHAT THIS DOES NOT TEST: OpenAL's mixer. Capturing the rendered audio would need OpenAL Soft's
// wave-writer backend, which the bundled native build ignores (it accepts neither ALSOFT_CONF nor
// ALSOFT_DRIVERS here). So the gate checks everything on THIS side of the boundary -- the listener
// frame, the source position and relative flag the driver receives, the derived velocity, and the
// attenuation curve, which mirrors AL's InverseDistanceClamped exactly. Those are the parts that
// can be wrong because of engine code.
using BdvEngine;
using System;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
string side = Arg("--side") ?? "right";

AudioSourceComponent? emitter = null;
SimObject? mover = null;
int frames = 0;
bool reported = false;
bool move = Array.IndexOf(cli, "--move") >= 0;
const float MoveSpeed = 4f;   // m/s along +X, a known value the gate checks the derived velocity against

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 50, near: 0.2f, far: 200f);
        // Listener sits at the origin looking down -Z, which is the OpenAL convention and makes
        // "+X is to the right" true rather than something to reason about.
        w.Camera.Position = Vector3.Zero;
        w.Camera.Target = new Vector3(0, 0, -1);
        w.Environment.Sky = new Vector3(0.10f, 0.12f, 0.16f);
        w.Environment.Ambient = new Vector3(0.35f, 0.35f, 0.40f);
        w.Add(new DirectionalLight(new Vector3(-0.4f, -1f, -0.4f)));

        AudioManager.Load("tone", ContentPath.Resolve("sketches/assets/tone.wav"));

        var pos = side switch
        {
            "left"  => new Vector3(-6f, 0f, 0f),
            "front" => new Vector3(0f, 0f, -6f),
            _       => new Vector3(6f, 0f, 0f),
        };

        mover = new SimObject(9400, "emitter");
        mover.Transform.Position = pos;
        mover.AddComponent(new MeshComponent(Primitives.Sphere(16, 12).Mesh,
                                             Materials.Standard("emit", new Color(230, 180, 90))));
        emitter = new AudioSourceComponent
        {
            Clip = "tone", Loop = true, PlayOnLoad = true, Volume = 0.5f,
            ReferenceDistance = 3f, MaxDistance = 60f, Rolloff = 1f,
            Falloff = AudioFalloff.Inverse,
        };
        mover.AddComponent(emitter);
        w.Add(mover);

        Console.WriteLine($"[audio] emitter at {Fmt(pos)}, side={side}");
    },

    update: (w, dt) =>
    {
        // Move the emitter at a known speed so the derived-velocity path (which is what makes
        // Doppler work without the game reporting speeds) has something to measure.
        if (move && mover != null)
            mover.Transform.Position += new Vector3(MoveSpeed * (float)dt, 0, 0);

        if (++frames < 30 || reported) return;
        reported = true;

        var listener = AudioManager.Listener;
        var handle = emitter!.Handle;

        Console.WriteLine($"AUDIO LISTENER pos={Fmt(listener.Position)} fwd={Fmt(listener.Forward)} up={Fmt(listener.Up)}");
        Console.WriteLine($"AUDIO DEVICE available={(handle != null)}");

        if (handle != null)
        {
            Console.WriteLine($"AUDIO SOURCE pos={Fmt(handle.GetPosition())} spatial={handle.IsSpatial()} playing={handle.IsPlaying()}");
        }
        Console.WriteLine($"AUDIO EMITTER world={Fmt(emitter.WorldPosition)} gain={emitter.AudibleGain:F4}");

        // Panning is decided by where the source sits in the LISTENER'S frame, not by world axes.
        // Reporting that projection is what lets the gate assert "this ends up on the right" without
        // reimplementing OpenAL's mixer.
        var right = Vector3.Normalize(Vector3.Cross(listener.Forward, listener.Up));
        var rel = emitter.WorldPosition - listener.Position;
        Console.WriteLine($"AUDIO RELATIVE lateral={Vector3.Dot(rel, right):F3} " +
                          $"depth={Vector3.Dot(rel, listener.Forward):F3} " +
                          $"vertical={Vector3.Dot(rel, listener.Up):F3}");

        if (handle != null && move)
            Console.WriteLine($"AUDIO VELOCITY {Fmt(handle.GetVelocity())} expected_x={MoveSpeed:F1}");

        // The attenuation curve, sampled. Reference distance 3 must be exactly 1.0, and the curve
        // must fall monotonically and never go negative.
        var sp = emitter.SpatialSettings;
        var samples = new[] { 0f, 1.5f, 3f, 6f, 12f, 30f, 60f, 200f };
        foreach (var d in samples)
            Console.WriteLine($"AUDIO GAIN d={d:F1} g={sp.GainAt(d):F4}");

        // Linear falloff must reach silence exactly at MaxDistance -- the property that makes it
        // worth having despite being unphysical.
        var lin = sp; lin.Falloff = AudioFalloff.Linear;
        Console.WriteLine($"AUDIO LINEAR at_max={lin.GainAt(sp.MaxDistance):F4} at_ref={lin.GainAt(sp.ReferenceDistance):F4}");

        var none = sp; none.Falloff = AudioFalloff.None;
        Console.WriteLine($"AUDIO NONE far={none.GainAt(9999f):F4}");
    }
);

static string Fmt(Vector3 v) => $"({v.X:F2},{v.Y:F2},{v.Z:F2})";

string? Arg(string flag)
{
    int i = Array.IndexOf(cli, flag);
    return i >= 0 && i + 1 < cli.Length ? cli[i + 1] : null;
}
