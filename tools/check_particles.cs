#:project GateKit/GateKit.csproj
// Particle gate: many particles cost few draw calls, and off-screen systems cost none.
//
//   dotnet run tools/check_particles.cs
//
// Two runs of sketches/particles_test.cs. The default one puts all four systems in view; --behind
// moves one of them behind the camera. Asserts three things:
//
//   1. the sketch's own in-process checks all pass (steady-state counts, caps, bounds, local space),
//   2. hundreds of particles draw in a handful of calls — the point of instancing them, and
//   3. a system outside the frustum drops a draw call rather than being uploaded and clipped.
//
// The third is the one a screenshot can't show: an off-screen emitter still SIMULATES (its
// particles must be in place when it comes back into view), so "nothing visible" is not evidence
// it was culled. The draw-call count is.
using GateKit;

const int MaxCalls = 20;
const int MinParticles = 150;

var (calls, live, passed) = Run("all");
var (cullCalls, cullLive, cullPassed) = Run("behind", "--behind");

Gate.Info($"all visible   : {calls} calls, {live} particles");
Gate.Info($"one off-screen: {cullCalls} calls, {cullLive} particles");
Gate.Blank();

Gate.Check("sketch checks pass", passed && cullPassed, "both runs reported PASS");
Gate.Check("cost is per system", calls <= MaxCalls && live >= MinParticles,
           $"{live} particles in {calls} calls (one-per-particle would be {live})");
Gate.Check("off-screen system is culled", cullCalls < calls,
           $"{calls} -> {cullCalls} calls with one emitter behind the camera");
Gate.Check("culled system still simulates", cullLive > MinParticles,
           $"{cullLive} particles alive while one system is off-screen");

return Gate.Report("PARTICLES PASS — cost is per system, off-screen systems draw nothing",
                   "PARTICLES FAIL");

static (int Calls, int Live, bool Passed) Run(string name, params string[] extra)
{
    var args = new List<string> { "--shot", $"/tmp/pfx_{name}.png", "--frames", "60" };
    args.AddRange(extra);
    string output = Gate.RunSketch("sketches/particles_test.cs", args.ToArray());
    return (Gate.Int(output, @"PARTICLES CALLS=(\d+)"),
            Gate.Int(output, @"LIVE=(\d+)"),
            Gate.Has(output, "PARTICLES PASS"));
}
