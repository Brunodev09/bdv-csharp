#!/usr/bin/env python3
"""Frustum-culling + instancing gate: cost must drop, the picture must not change.

    python3 tools/check_culling.py

Renders the same scene four ways (naive / culling only / instancing only / both), then asserts
that draw calls fall and that the images match the naive reference.

Why a threshold instead of byte-equality: the instanced vertex stage reads the model and normal
matrices from attributes and multiplies them in a different association order than the uniform
path, so a handful of pixels on shadow and silhouette edges land on the opposite side of a
floating-point tie. That is expected and harmless. A REAL bug — a transposed matrix, a wrong
instance offset — moves whole surfaces, which this threshold still catches easily.
"""
import re
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageChops

# Fraction of pixels allowed to differ from the naive reference.
MAX_DIFF_FRACTION = 0.0005    # 0.05%; observed is ~0.0003%
CONFIGS = [
    ("naive", ["--naive"]),
    ("cull", ["--no-inst"]),
    ("inst", ["--no-cull"]),
    ("both", []),
]


def run(name, flags):
    out = f"/tmp/cullgate_{name}.png"
    proc = subprocess.run(
        ["dotnet", "run", "sketches/culling_test.cs", "--",
         "--shot", out, "--frames", "40", *flags],
        capture_output=True, text=True, cwd=Path(__file__).resolve().parent.parent)
    m = re.search(r"DRAWCALLS=(\d+)", proc.stdout)
    if not m:
        print(f"  {name}: no draw-call report\n{proc.stdout}\n{proc.stderr}")
        sys.exit(1)
    return int(m.group(1)), out


def compare(ref_path, path):
    a = Image.open(ref_path).convert("RGB")
    b = Image.open(path).convert("RGB")
    w, h = a.size
    # Exclude the stats overlay: its FPS text legitimately differs between runs.
    box = (0, int(h * 0.11), w, h)
    diff = ImageChops.difference(a.crop(box), b.crop(box))
    changed = sum(1 for p in diff.getdata() if p != (0, 0, 0))
    total = (box[3] - box[1]) * w
    peak = max((max(p) for p in diff.getdata() if p != (0, 0, 0)), default=0)
    return changed, total, peak


def main():
    results = {}
    for name, flags in CONFIGS:
        calls, path = run(name, flags)
        results[name] = (calls, path)
        print(f"  {name:6s} draw calls: {calls}")

    ref_calls, ref_path = results["naive"]
    ok = True
    print()
    for name, (calls, path) in results.items():
        if name == "naive":
            continue
        changed, total, peak = compare(ref_path, path)
        frac = changed / total
        cheaper = calls < ref_calls
        clean = frac <= MAX_DIFF_FRACTION
        ok &= cheaper and clean
        print(f"  {name:6s}: {calls} vs {ref_calls} calls ({100 * (1 - calls / ref_calls):.0f}% fewer)"
              f"  |  {changed}/{total} px differ ({100 * frac:.4f}%), peak delta {peak}"
              f"  -> {'ok' if cheaper and clean else 'FAIL'}")

    print()
    print("CULLING+INSTANCING PASS — cost falls, picture holds" if ok
          else "CULLING+INSTANCING FAIL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
