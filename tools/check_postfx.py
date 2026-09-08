#!/usr/bin/env python3
"""3D post-processing gate: each knob does its own job, measurably.

    python3 tools/check_postfx.py

Renders the same fixed scene once per configuration and measures the pixels. The scene is static
and the camera never moves, so two runs differing in one flag differ only because of that flag.

The assertion that matters most is the tonemap one. Without it, 5% of this frame is pure white --
a flat blob where the light pool hits the ground, with every bit of shape inside it gone. ACES has
to make that region readable again while leaving the rest of the image broadly alone, and "fewer
fully-clipped pixels" is exactly that claim in a number.
"""
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageStat

ROOT = Path(__file__).resolve().parent.parent


def render(name, flags):
    out = f"/tmp/pfxgate_{name}.png"
    p = subprocess.run(["dotnet", "run", "sketches/postfx_test.cs", "--",
                        "--shot", out, "--frames", "20", *flags],
                       capture_output=True, text=True, cwd=ROOT)
    if "POSTFX" not in p.stdout:
        print(f"  {name}: no report\n{p.stdout}\n{p.stderr}")
        sys.exit(1)
    im = Image.open(out).convert("RGB")
    w, h = im.size
    return im.crop((0, int(h * 0.10), w, h))      # crop away the stats overlay


def clipped(im):
    px = list(im.getdata())
    return 100.0 * sum(1 for p in px if min(p) >= 250) / len(px)


def differing(a, b, tol=6):
    px = list(ImageChops.difference(a, b).getdata())
    return 100.0 * sum(1 for p in px if sum(p) > tol) / len(px)


def mean(im):
    return sum(ImageStat.Stat(im).mean) / 3.0


def channel_spread(im):
    """Mean |max channel - min channel|: 0 for greyscale, large for saturated colour."""
    px = list(im.getdata())
    step = max(len(px) // 200_000, 1)
    sample = px[::step]
    return sum(max(p) - min(p) for p in sample) / len(sample)


def region(im, box):
    w, h = im.size
    return mean(im.crop((int(w * box[0]), int(h * box[1]), int(w * box[2]), int(h * box[3]))))


def main():
    base = render("base", [])
    off = render("off", ["--off"])
    nobloom = render("nobloom", ["--nobloom"])
    tm_none = render("tmnone", ["--tonemap", "none"])
    bright = render("bright", ["--exposure", "2.3"])
    grey = render("grey", ["--saturation", "0"])
    vign = render("vignette", ["--vignette", "0.9"])

    bloom_effect = differing(base, nobloom)
    clip_none, clip_aces = clipped(tm_none), clipped(base)

    v_centre_ref, v_corner_ref = region(base, (.4, .4, .6, .6)), region(base, (0, 0, .12, .25))
    v_centre, v_corner = region(vign, (.4, .4, .6, .6)), region(vign, (0, 0, .12, .25))
    corner_drop = 1 - (v_corner / v_corner_ref) if v_corner_ref else 0
    centre_drop = 1 - (v_centre / v_centre_ref) if v_centre_ref else 0

    print(f"  post-fx off / on        : mean {mean(off):.1f} -> {mean(base):.1f}")
    print(f"  clipped to white        : {clip_none:.2f}% (no tonemap) -> {clip_aces:.2f}% (ACES)")
    print(f"  bloom affects           : {bloom_effect:.2f}% of pixels")
    print(f"  saturation 1 -> 0 spread: {channel_spread(base):.1f} -> {channel_spread(grey):.1f}")
    print(f"  vignette corner/centre  : -{100*corner_drop:.0f}% / -{100*centre_drop:.0f}%")
    print()

    checks = [
        ("post-fx changes the frame", differing(base, off) > 20,
         f"{differing(base, off):.1f}% of pixels differ from the unprocessed render"),
        ("tonemap rescues clipping", clip_aces < clip_none * 0.5 and clip_none > 1.0,
         f"{clip_none:.2f}% -> {clip_aces:.2f}% fully-white pixels"),
        ("bloom adds a local glow", 0.5 < bloom_effect < 40,
         f"{bloom_effect:.2f}% affected (local, not a whole-frame wash)"),
        ("exposure brightens", mean(bright) > mean(base) + 8,
         f"mean {mean(base):.1f} -> {mean(bright):.1f} at 2x exposure"),
        ("saturation 0 is greyscale", channel_spread(grey) < 2.0,
         f"channel spread {channel_spread(grey):.2f} (colour would be >20)"),
        ("vignette darkens corners only", corner_drop > 0.25 and centre_drop < 0.05,
         f"corners -{100*corner_drop:.0f}%, centre -{100*centre_drop:.0f}%"),
    ]

    ok = True
    for name, good, detail in checks:
        print(f"  {'ok  ' if good else 'FAIL'} {name:30s} {detail}")
        ok &= good

    print()
    print("POSTFX PASS — HDR, bloom, tonemap and grading each do their own job"
          if ok else "POSTFX FAIL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
