#!/usr/bin/env python3
"""Alpha-tested (cutout) shadow gate: a holed card must cast a holed shadow.

    python3 tools/check_cutout.py

Renders the same holed card twice — once as BlendMode.Cutout, once as a plain opaque material —
and compares.

The card itself looks identical in both runs, which is the whole point of the bug. GL blending is
enabled globally, so a texel with alpha 0 blends away in the colour pass whether or not the
material is a cutout: the card LOOKS like a leaf either way. Only the depth pass differs, and
without alpha testing it writes the full quad — so a leaf-shaped card casts a rectangular shadow.

The measurement is therefore entirely about the shadow.
"""
import re
import subprocess
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
MIN_SHADOW_REDUCTION = 0.40    # the ring-with-gaps texture covers well under half the quad


def run(name, flags):
    out = f"/tmp/cutgate_{name}.png"
    p = subprocess.run(["dotnet", "run", "sketches/cutout_test.cs", "--",
                        "--shot", out, "--frames", "40", *flags],
                       capture_output=True, text=True, cwd=ROOT)
    m = re.search(r"blend=(\w+) cutoff=([\d.]+) castShadows=(\w+)", p.stdout)
    if not m:
        print(f"  {name}: no report\n{p.stdout}\n{p.stderr}")
        sys.exit(1)
    return out, m.group(1), m.group(2), m.group(3)


def shadow_pixels(path):
    """Count neutral-grey pixels: the ground is warm-tinted, the shadow is neutral."""
    im = Image.open(path).convert("RGB")
    w, h = im.size
    n = 0
    for y in range(int(h * 0.10), h, 2):          # skip the stats overlay
        for x in range(0, w, 2):
            r, g, b = im.getpixel((x, y))
            if abs(r - g) < 12 and abs(g - b) < 12 and r < 190:
                n += 1
    return n


def darker_pixels(a_path, b_path):
    """Pixels where A is materially darker than B — a cutout must never shadow MORE."""
    a = Image.open(a_path).convert("RGB")
    b = Image.open(b_path).convert("RGB")
    w, h = a.size
    return sum(1 for y in range(int(h * 0.10), h, 3) for x in range(0, w, 3)
               if sum(a.getpixel((x, y))) < sum(b.getpixel((x, y))) - 25)


def main():
    cut_png, cut_blend, cut_cutoff, cut_casts = run("cutout", [])
    sol_png, sol_blend, sol_cutoff, sol_casts = run("solid", ["--solid"])
    print(f"  cutout run: blend={cut_blend} cutoff={cut_cutoff} castShadows={cut_casts}")
    print(f"  solid  run: blend={sol_blend} cutoff={sol_cutoff} castShadows={sol_casts}")

    sc, ss = shadow_pixels(cut_png), shadow_pixels(sol_png)
    reduction = 1 - sc / ss if ss else 0
    darker = darker_pixels(cut_png, sol_png)

    checks = [
        ("cutout material reports Cutout + casts", cut_blend == "Cutout" and cut_casts == "True",
         f"blend={cut_blend} castShadows={cut_casts}"),
        ("shadow gains holes", reduction >= MIN_SHADOW_REDUCTION,
         f"{ss} -> {sc} shadow samples ({100 * reduction:.0f}% less)"),
        ("cutout never shadows MORE", darker == 0, f"{darker} px darker"),
    ]

    print()
    ok = True
    for name, passed, detail in checks:
        print(f"  {'ok  ' if passed else 'FAIL'} {name:38s} {detail}")
        ok &= passed

    print()
    print("CUTOUT PASS — a holed card casts a holed shadow" if ok else "CUTOUT FAIL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
