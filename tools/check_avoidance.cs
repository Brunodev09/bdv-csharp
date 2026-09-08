#:project GateKit/GateKit.csproj
// Local avoidance gate.
//
//   dotnet run tools/check_avoidance.cs
//
// Twelve agents cross a ring, every route through the centre at the same moment. Runs it twice —
// with avoidance and without — and compares.
//
// The control run is the point. "Agents stayed 0.7m apart" means nothing unless the same scene
// without avoidance puts them at 0.0m, because a test that cannot fail is not measuring anything.
// The agents carry no colliders precisely so that physics cannot quietly do avoidance's job.
using GateKit;

string on = Run("on");
string off = Run("off", "--noavoid");

float sepOn = Gate.Float(on, @"MINSEP=([\d.]+)");
float sepOff = Gate.Float(off, @"MINSEP=([\d.]+)");
int overlapOn = Gate.Int(on, @"OVERLAPSTEPS=(\d+)");
int overlapOff = Gate.Int(off, @"OVERLAPSTEPS=(\d+)");
int arrivedOn = Gate.Int(on, @"ARRIVED=(\d+)");
int arrivedOff = Gate.Int(off, @"ARRIVED=(\d+)");
int stepsOn = Gate.Int(on, @"AVOID STEPS=(\d+)");
int stepsOff = Gate.Int(off, @"AVOID STEPS=(\d+)");
float missOn = Gate.Float(on, @"WORSTMISS=([\d.]+)");

bool sketchOn = Gate.Has(on, "AVOIDANCE PASS");
bool sketchOff = Gate.Has(off, "AVOIDANCE PASS");
double detour = stepsOff > 0 ? (double)stepsOn / stepsOff : 0;

Gate.Info($"closest approach : {sepOff:F2}m without -> {sepOn:F2}m with (bodies are 0.70m combined)");
Gate.Info($"overlapping steps: {overlapOff} without -> {overlapOn} with");
Gate.Info($"arrived          : {arrivedOff}/12 without -> {arrivedOn}/12 with");
Gate.Info($"time to clear    : {stepsOff} steps without -> {stepsOn} with ({detour:F2}x)");
Gate.Blank();

Gate.Check("both scenario runs pass", sketchOn && sketchOff, "in-sketch checks green either way");
Gate.Check("control actually collides", sepOff < 0.35f && overlapOff > 0,
           $"{sepOff:F2}m closest, {overlapOff} overlapping steps — the test can fail");
Gate.Check("avoidance separates them", sepOn > 0.52f,
           $"{sepOn:F2}m closest approach, up from {sepOff:F2}m");
Gate.Check("no interpenetration at all", overlapOn == 0, $"{overlapOn} overlapping steps");
Gate.Check("no deadlock", arrivedOn == 12, $"{arrivedOn}/12 crossed the ring");
Gate.Check("detour cost is modest", detour < 2.0,
           $"{detour:F2}x the time of walking straight through");
Gate.Check("agents finish where they aimed", missOn < 1.0f,
           $"worst agent {missOn:F2}m from its target");

return Gate.Report("AVOIDANCE PASS — a crowd crosses itself without touching or locking up",
                   "AVOIDANCE FAIL");

static string Run(string name, params string[] extra)
{
    var args = new List<string> { "--shot", $"/tmp/avoidgate_{name}.png", "--frames", "30" };
    args.AddRange(extra);
    string output = Gate.RunSketch("sketches/avoidance_test.cs", args.ToArray());
    if (!Gate.Has(output, "AVOID STEPS="))
    {
        Console.Error.WriteLine($"  {name}: no report\n{output}");
        Environment.Exit(1);
    }
    return output;
}
