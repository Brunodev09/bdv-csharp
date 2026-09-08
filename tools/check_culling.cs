#:project GateKit/GateKit.csproj
// Frustum-culling + instancing gate: cost must drop, the picture must not change.
//
//   dotnet run tools/check_culling.cs
//
// Renders the same scene four ways (naive / culling only / instancing only / both), then asserts
// that draw calls fall and that the images match the naive reference.
//
// Why a threshold instead of byte-equality: the instanced vertex stage reads the model and normal
// matrices from attributes and multiplies them in a different association order than the uniform
// path, so a handful of pixels on shadow and silhouette edges land on the opposite side of a
// floating-point tie. That is expected and harmless. A REAL bug — a transposed matrix, a wrong
// instance offset — moves whole surfaces, which this threshold still catches easily.
using GateKit;

const double MaxDiffFraction = 0.0005;   // 0.05%; observed is ~0.0003%

var configs = new (string Name, string[] Flags)[]
{
    ("naive", new[] { "--naive" }),
    ("cull",  new[] { "--no-inst" }),
    ("inst",  new[] { "--no-cull" }),
    ("both",  Array.Empty<string>()),
};

var results = new Dictionary<string, (int Calls, string Png)>();
foreach (var (name, flags) in configs)
{
    var r = Run(name, flags);
    results[name] = r;
    Gate.Info($"{name,-6} draw calls: {r.Calls}");
}

var (refCalls, refPng) = results["naive"];
// Exclude the stats overlay: its FPS text legitimately differs between runs.
var reference = GateImage.Load(refPng).Crop(0f, 0.11f, 1f, 1f);

Gate.Blank();
foreach (var (name, _) in configs)
{
    if (name == "naive") continue;
    var (calls, png) = results[name];
    var img = GateImage.Load(png).Crop(0f, 0.11f, 1f, 1f);

    int changed = GateImage.CountDiffering(reference, img);
    int peak = GateImage.PeakDelta(reference, img);
    double frac = (double)changed / reference.PixelCount;

    Gate.Check(name,
               calls < refCalls && frac <= MaxDiffFraction,
               $"{calls} vs {refCalls} calls ({100 * (1 - (double)calls / refCalls):F0}% fewer)  |  " +
               $"{changed}/{reference.PixelCount} px differ ({100 * frac:F4}%), peak delta {peak}");
}

return Gate.Report("CULLING+INSTANCING PASS — cost falls, picture holds", "CULLING+INSTANCING FAIL");

static (int Calls, string Png) Run(string name, string[] extra)
{
    string png = $"/tmp/cullgate_{name}.png";
    var args = new List<string> { "--shot", png, "--frames", "40" };
    args.AddRange(extra);
    string output = Gate.RunSketch("sketches/culling_test.cs", args.ToArray());
    return (Gate.Int(output, @"DRAWCALLS=(\d+)"), png);
}
