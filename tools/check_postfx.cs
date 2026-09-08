#:project GateKit/GateKit.csproj
// 3D post-processing gate: each knob does its own job, measurably.
//
//   dotnet run tools/check_postfx.cs
//
// Renders the same fixed scene once per configuration and measures the pixels. The scene is static
// and the camera never moves, so two runs differing in one flag differ only because of that flag.
//
// The assertion that matters most is the tonemap one. Without it, ~10% of this frame is pure white
// — a flat blob where the light pool hits the ground, with every bit of shape inside it gone. ACES
// has to make that region readable again while leaving the rest of the image broadly alone, and
// "fewer fully-clipped pixels" is exactly that claim in a number.
using GateKit;

var baseline = Render("base");
var off      = Render("off", "--off");
var nobloom  = Render("nobloom", "--nobloom");
var tmNone   = Render("tmnone", "--tonemap", "none");
var bright   = Render("bright", "--exposure", "2.3");
var grey     = Render("grey", "--saturation", "0");
var vign     = Render("vignette", "--vignette", "0.9");

double bloomEffect = 100.0 * GateImage.CountDiffering(baseline, nobloom, 6) / baseline.PixelCount;
double offEffect   = 100.0 * GateImage.CountDiffering(baseline, off, 6) / baseline.PixelCount;
double clipNone = 100 * tmNone.FractionAtLeast(250);
double clipAces = 100 * baseline.FractionAtLeast(250);

// Vignette must darken the edges WITHOUT touching the middle; comparing both regions against the
// un-vignetted render is what separates "darkened the corners" from "darkened everything".
double cornerRef = Region(baseline, 0f, 0f, .12f, .25f), corner = Region(vign, 0f, 0f, .12f, .25f);
double centreRef = Region(baseline, .4f, .4f, .6f, .6f), centre = Region(vign, .4f, .4f, .6f, .6f);
double cornerDrop = cornerRef > 0 ? 1 - corner / cornerRef : 0;
double centreDrop = centreRef > 0 ? 1 - centre / centreRef : 0;

Gate.Info($"post-fx off / on        : mean {off.MeanBrightness():F1} -> {baseline.MeanBrightness():F1}");
Gate.Info($"clipped to white        : {clipNone:F2}% (no tonemap) -> {clipAces:F2}% (ACES)");
Gate.Info($"bloom affects           : {bloomEffect:F2}% of pixels");
Gate.Info($"saturation 1 -> 0 spread: {baseline.ChannelSpread():F1} -> {grey.ChannelSpread():F1}");
Gate.Info($"vignette corner/centre  : -{100 * cornerDrop:F0}% / -{100 * centreDrop:F0}%");
Gate.Blank();

Gate.Check("post-fx changes the frame", offEffect > 20,
           $"{offEffect:F1}% of pixels differ from the unprocessed render");
Gate.Check("tonemap rescues clipping", clipAces < clipNone * 0.5 && clipNone > 1.0,
           $"{clipNone:F2}% -> {clipAces:F2}% fully-white pixels");
Gate.Check("bloom adds a local glow", bloomEffect > 0.5 && bloomEffect < 40,
           $"{bloomEffect:F2}% affected (local, not a whole-frame wash)");
Gate.Check("exposure brightens", bright.MeanBrightness() > baseline.MeanBrightness() + 8,
           $"mean {baseline.MeanBrightness():F1} -> {bright.MeanBrightness():F1} at 2x exposure");
Gate.Check("saturation 0 is greyscale", grey.ChannelSpread() < 2.0,
           $"channel spread {grey.ChannelSpread():F2} (colour would be >20)");
Gate.Check("vignette darkens corners only", cornerDrop > 0.25 && centreDrop < 0.05,
           $"corners -{100 * cornerDrop:F0}%, centre -{100 * centreDrop:F0}%");

return Gate.Report("POSTFX PASS — HDR, bloom, tonemap and grading each do their own job",
                   "POSTFX FAIL");

static double Region(GateImage im, float x0, float y0, float x1, float y1)
    => im.Crop(x0, y0, x1, y1).MeanBrightness();

static GateImage Render(string name, params string[] extra)
{
    string png = $"/tmp/pfxgate_{name}.png";
    var args = new List<string> { "--shot", png, "--frames", "20" };
    args.AddRange(extra);
    string output = Gate.RunSketch("sketches/postfx_test.cs", args.ToArray());
    if (!Gate.Has(output, "POSTFX"))
    {
        Console.Error.WriteLine($"  {name}: no report\n{output}");
        Environment.Exit(1);
    }
    return GateImage.Load(png).Crop(0f, 0.10f, 1f, 1f);   // crop away the stats overlay
}
