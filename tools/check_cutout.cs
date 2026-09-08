#:project GateKit/GateKit.csproj
// Alpha-tested (cutout) shadow gate: a holed card must cast a holed shadow.
//
//   dotnet run tools/check_cutout.cs
//
// Renders the same holed card twice — once as BlendMode.Cutout, once as a plain opaque material —
// and compares.
//
// The card itself looks identical in both runs, which is the whole point of the bug. GL blending
// is enabled globally, so a texel with alpha 0 blends away in the colour pass whether or not the
// material is a cutout: the card LOOKS like a leaf either way. Only the depth pass differs, and
// without alpha testing it writes the full quad — so a leaf-shaped card casts a rectangular shadow.
//
// The measurement is therefore entirely about the shadow.
using GateKit;

const double MinShadowReduction = 0.40;   // the ring-with-gaps texture covers well under half the quad

var (cutPng, cutBlend, cutCutoff, cutCasts) = Run("cutout");
var (solPng, solBlend, solCutoff, solCasts) = Run("solid", "--solid");

Gate.Info($"cutout run: blend={cutBlend} cutoff={cutCutoff} castShadows={cutCasts}");
Gate.Info($"solid  run: blend={solBlend} cutoff={solCutoff} castShadows={solCasts}");
Gate.Blank();

var cut = GateImage.Load(cutPng);
var sol = GateImage.Load(solPng);

int sc = ShadowPixels(cut), ss = ShadowPixels(sol);
double reduction = ss > 0 ? 1.0 - (double)sc / ss : 0;
int darker = DarkerPixels(cut, sol);

Gate.Check("cutout material reports Cutout + casts", cutBlend == "Cutout" && cutCasts == "True",
           $"blend={cutBlend} castShadows={cutCasts}");
Gate.Check("shadow gains holes", reduction >= MinShadowReduction,
           $"{ss} -> {sc} shadow samples ({100 * reduction:F0}% less)");
Gate.Check("cutout never shadows MORE", darker == 0, $"{darker} px darker");

return Gate.Report("CUTOUT PASS — a holed card casts a holed shadow", "CUTOUT FAIL");

// Count neutral-grey pixels: the ground is warm-tinted, the shadow is neutral. Sampling every
// other pixel is plenty for an area measurement and keeps the gate quick.
static int ShadowPixels(GateImage im)
{
    int n = 0;
    for (int y = (int)(im.Height * 0.10); y < im.Height; y += 2)
    for (int x = 0; x < im.Width; x += 2)
    {
        var (r, g, b) = im[x, y];
        if (Math.Abs(r - g) < 12 && Math.Abs(g - b) < 12 && r < 190) n++;
    }
    return n;
}

// Pixels where the cutout run is materially darker than the solid one — a cutout must never
// shadow MORE than the shape it is cut from.
static int DarkerPixels(GateImage a, GateImage b)
{
    int n = 0;
    for (int y = (int)(a.Height * 0.10); y < a.Height; y += 3)
    for (int x = 0; x < a.Width; x += 3)
    {
        var (ar, ag, ab) = a[x, y];
        var (br, bg, bb) = b[x, y];
        if (ar + ag + ab < br + bg + bb - 25) n++;
    }
    return n;
}

static (string Png, string Blend, string Cutoff, string Casts) Run(string name, params string[] extra)
{
    string png = $"/tmp/cutgate_{name}.png";
    var args = new List<string> { "--shot", png, "--frames", "40" };
    args.AddRange(extra);
    string output = Gate.RunSketch("sketches/cutout_test.cs", args.ToArray());
    return (png,
            Gate.Text(output, @"blend=(\w+) cutoff=") ?? "?",
            Gate.Text(output, @"cutoff=([\d.]+)") ?? "?",
            Gate.Text(output, @"castShadows=(\w+)") ?? "?");
}
