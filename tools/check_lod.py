#!/usr/bin/env python3
"""LOD gate: geometry cost falls a lot, and the picture only changes where you can't tell.

    python3 tools/check_lod.py

Renders the same corridor of trees with and without LOD, then asserts three things:

  1. vertices drawn fall substantially (the point of the feature),
  2. the frame barely changes overall, and
  3. NOTHING changes in the near half of the frame.

The third is the real assertion. LOD is allowed to alter the picture — it swaps geometry — but only
at distance. A threshold set too aggressively shows up immediately as near-field change, which a
whole-frame percentage would happily average away.
"""
import re
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parent.parent
MIN_VERT_REDUCTION = 0.50
MAX_FRAME_CHANGE = 0.02      # 2% of pixels


def run(name, flags):
    out = f"/tmp/lodgate_{name}.png"
    p = subprocess.run(["dotnet", "run", "sketches/lod_test.cs", "--",
                        "--shot", out, "--frames", "40", *flags],
                       capture_output=True, text=True, cwd=ROOT)
    verts = re.search(r"VERTS=(\d+)", p.stdout)
    calls = re.search(r"CALLS=(\d+)", p.stdout)
    levels = re.search(r"LEVELS (.+)", p.stdout)
    if not verts:
        print(f"  {name}: no report\n{p.stdout}\n{p.stderr}")
        sys.exit(1)
    return out, int(verts.group(1)), int(calls.group(1)), (levels.group(1) if levels else "")


def main():
    on_png, on_v, on_c, levels = run("on", [])
    off_png, off_v, off_c, _ = run("off", ["--nolod"])
    print(f"  lod on : {on_v:>8,} verts  {on_c} calls   {levels}")
    print(f"  lod off: {off_v:>8,} verts  {off_c} calls")

    a = Image.open(on_png).convert("RGB")
    b = Image.open(off_png).convert("RGB")
    w, h = a.size
    box = (0, int(h * 0.11), w, h)          # skip the stats overlay
    d = ImageChops.difference(a.crop(box), b.crop(box))
    dw, dh = d.size

    changed = sum(1 for p in d.getdata() if p != (0, 0, 0))
    total = dw * dh
    near = sum(1 for y in range(dh // 2, dh) for x in range(0, dw, 2)
               if d.getpixel((x, y)) != (0, 0, 0))

    reduction = 1 - on_v / off_v if off_v else 0
    frac = changed / total

    checks = [
        ("vertices fall", reduction >= MIN_VERT_REDUCTION, f"{100 * reduction:.0f}% fewer"),
        ("frame barely changes", frac <= MAX_FRAME_CHANGE,
         f"{changed}/{total} px ({100 * frac:.2f}%)"),
        ("near field untouched", near == 0, f"{near} changed samples in the near half"),
    ]

    print()
    ok = True
    for name, passed, detail in checks:
        print(f"  {'ok  ' if passed else 'FAIL'} {name:24s} {detail}")
        ok &= passed

    print()
    print("LOD PASS — cost falls, only the distance changes" if ok else "LOD FAIL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
