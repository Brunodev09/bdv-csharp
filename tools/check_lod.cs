#:project GateKit/GateKit.csproj
// LOD gate: geometry cost falls a lot, and the picture only changes where you can't tell.
//
//   dotnet run tools/check_lod.cs
//
// Renders the same corridor of trees with and without LOD, then asserts three things:
//
//   1. vertices drawn fall substantially (the point of the feature),
//   2. the frame barely changes overall, and
//   3. NOTHING changes in the near half of the frame.
//
// The third is the real assertion. LOD is allowed to alter the picture — it swaps geometry — but
// only at distance. A threshold set too aggressively shows up immediately as near-field change,
// which a whole-frame percentage would happily average away.
using GateKit;

const float MinVertReduction = 0.50f;
const float MaxFrameChange = 0.02f;      // 2% of pixels

var (onPng, onV, onC, levels) = Run("on");
var (offPng, offV, offC, _) = Run("off", "--nolod");

Gate.Info($"lod on : {onV,8:N0} verts  {onC} calls   {levels}");
Gate.Info($"lod off: {offV,8:N0} verts  {offC} calls");
Gate.Blank();

// Skip the stats overlay across the top; it reports different numbers per run by design.
var a = GateImage.Load(onPng).Crop(0f, 0.11f, 1f, 1f);
var b = GateImage.Load(offPng).Crop(0f, 0.11f, 1f, 1f);

int changed = GateImage.CountDiffering(a, b);
int near = GateImage.CountDifferingIn(a, b, 0f, 0.5f, 1f, 1f);
float reduction = offV > 0 ? 1f - (float)onV / offV : 0f;
float frac = (float)changed / a.PixelCount;

Gate.Check("vertices fall", reduction >= MinVertReduction, $"{100 * reduction:F0}% fewer");
Gate.Check("frame barely changes", frac <= MaxFrameChange,
           $"{changed}/{a.PixelCount} px ({100 * frac:F2}%)");
Gate.Check("near field untouched", near == 0, $"{near} changed pixels in the near half");

return Gate.Report("LOD PASS — cost falls, only the distance changes", "LOD FAIL");

static (string Png, int Verts, int Calls, string Levels) Run(string name, params string[] extra)
{
    string png = $"/tmp/lodgate_{name}.png";
    var args = new List<string> { "--shot", png, "--frames", "40" };
    args.AddRange(extra);
    string output = Gate.RunSketch("sketches/lod_test.cs", args.ToArray());
    return (png,
            Gate.Int(output, @"VERTS=(\d+)"),
            Gate.Int(output, @"CALLS=(\d+)"),
            Gate.Text(output, @"LEVELS (.+)") ?? "");
}
