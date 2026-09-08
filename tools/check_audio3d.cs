#:project GateKit/GateKit.csproj
// Spatialised audio gate.
//
//   dotnet run tools/check_audio3d.cs
//
// HEADS UP: this plays a quiet 330Hz tone out of your speakers for a few seconds.
//
// Runs sketches/audio3d_test.cs with the emitter on each side of the listener and checks
// everything the engine is responsible for: the listener frame handed to OpenAL, the source
// position and world-relative flag the driver actually received, the velocity derived from
// movement, and the attenuation curve.
//
// WHERE THE LINE IS. This does not test OpenAL's mixer — capturing rendered audio needs OpenAL
// Soft's wave-writer backend, which the bundled native build ignores (it accepts neither
// ALSOFT_CONF nor ALSOFT_DRIVERS here). That half was verified BY EAR instead: with the emitter at
// x=+6 the tone comes from the right speaker and at x=-6 from the left, confirmed 2026-09-08.
// Everything below is the part that can regress from a code change.
using GateKit;

const float MoveSpeed = 4f;

string right  = Run("--side", "right");
string left   = Run("--side", "left");
string front  = Run("--side", "front");
string moving = Run("--side", "right", "--move");

bool haveDevice = Gate.Has(right, "AUDIO DEVICE available=True");
bool spatial = Gate.Text(right, @"AUDIO SOURCE pos=\([^)]*\) spatial=(\w+)") == "True";

float srcX = Gate.Float(right, @"AUDIO SOURCE pos=\((-?[\d.]+),");
float latR = Gate.Float(right, @"AUDIO RELATIVE lateral=(-?[\d.]+)");
float latL = Gate.Float(left,  @"AUDIO RELATIVE lateral=(-?[\d.]+)");
float latF = Gate.Float(front, @"AUDIO RELATIVE lateral=(-?[\d.]+)");
float depthF = Gate.Float(front, @"depth=(-?[\d.]+)");
float velX = Gate.Float(moving, @"AUDIO VELOCITY \((-?[\d.]+),");

float linMax = Gate.Float(right, @"AUDIO LINEAR at_max=([\d.]+)");
float linRef = Gate.Float(right, @"at_ref=([\d.]+)");
float noneFar = Gate.Float(right, @"AUDIO NONE far=([\d.]+)");

var gains = Gains(right);
var ordered = gains.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
bool monotonic = ordered.Zip(ordered.Skip(1), (a, b) => a >= b - 1e-6f).All(x => x);

Gate.Info($"device            : {(haveDevice ? "available" : "ABSENT")}");
Gate.Info($"source position   : x={srcX:F1}  spatial={spatial}");
Gate.Info($"lateral r/l/front : {latR:+0.00;-0.00} / {latL:+0.00;-0.00} / {latF:+0.00;-0.00} (front depth {depthF:+0.00;-0.00})");
Gate.Info("gain curve        : " + string.Join("  ", gains.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:g}m={kv.Value:F3}")));
Gate.Info($"derived velocity  : x={velX:F2} (emitter moving at {MoveSpeed})");
Gate.Blank();

Gate.Check("audio device opened", haveDevice, "OpenAL initialised and the clip played");
Gate.Check("source is world-positioned", spatial, "SourceRelative=false, so the listener matters");
Gate.Check("driver got the right position", Math.Abs(srcX - 6f) < 1e-3, $"x={srcX:F2}");
Gate.Check("right/left are opposite signs", latR > 1 && latL < -1,
           $"lateral {latR:+0.0;-0.0} vs {latL:+0.0;-0.0} in listener space");
Gate.Check("front is centred and ahead", Math.Abs(latF) < 0.01f && depthF > 1,
           $"lateral {latF:+0.00;-0.00}, depth {depthF:+0.0;-0.0}");
Gate.Check("full volume at reference", Near(gains[3f], 1f) && Near(gains[1.5f], 1f),
           "g=1.0 at and inside 3m");
Gate.Check("inverse law holds", Near(gains[6f], 0.5f) && Near(gains[12f], 0.25f),
           "2x distance halves gain (ref 3, rolloff 1)");
Gate.Check("curve never rises", monotonic, "monotonically non-increasing");
Gate.Check("clamped past max distance", Math.Abs(gains[200f] - gains[60f]) < 1e-6f,
           $"g stops falling at MaxDistance ({gains[60f]:F3})");
Gate.Check("linear hits silence at max", linMax < 1e-4f && Near(linRef, 1f), "0.0 at max, 1.0 at reference");
Gate.Check("no-falloff stays full", Near(noneFar, 1f), "gain 1.0 at 9999m");
Gate.Check("velocity derived from motion", Math.Abs(velX - MoveSpeed) < MoveSpeed * 0.35f,
           $"x={velX:F2} vs {MoveSpeed} m/s");

return Gate.Report("AUDIO3D PASS — listener, positions and attenuation are all correct", "AUDIO3D FAIL");

static bool Near(float a, float b) => Math.Abs(a - b) < 1e-3f;

static Dictionary<float, float> Gains(string output)
{
    var g = new Dictionary<float, float>();
    foreach (System.Text.RegularExpressions.Match m in
             System.Text.RegularExpressions.Regex.Matches(output, @"AUDIO GAIN d=([\d.]+) g=([\d.]+)"))
        g[float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)] =
            float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
    return g;
}

static string Run(params string[] extra)
{
    var args = new List<string> { "--shot", "/tmp/audiogate.png", "--frames", "45" };
    args.AddRange(extra);
    string output = Gate.RunSketch("sketches/audio3d_test.cs", args.ToArray());
    if (!Gate.Has(output, "AUDIO EMITTER"))
    {
        Console.Error.WriteLine($"  no report for [{string.Join(' ', extra)}]\n{output}");
        Environment.Exit(1);
    }
    return output;
}
