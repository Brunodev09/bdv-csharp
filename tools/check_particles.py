#!/usr/bin/env python3
"""Particle gate: many particles cost few draw calls, and off-screen systems cost none.

    python3 tools/check_particles.py

Two runs of sketches/particles_test.cs. The default one puts all four systems in view; --behind
moves one of them behind the camera. Asserts three things:

  1. the sketch's own in-process checks all pass (steady-state counts, caps, bounds, local space),
  2. hundreds of particles draw in a handful of calls -- the point of instancing them, and
  3. a system outside the frustum drops a draw call rather than being uploaded and clipped.

The third is the one a screenshot can't show: an off-screen emitter still SIMULATES (its particles
must be in place when it comes back into view), so "nothing visible" is not evidence it was culled.
The draw-call count is.
"""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MAX_CALLS = 20
MIN_PARTICLES = 150


def run(name, flags):
    out = f"/tmp/pfx_{name}.png"
    p = subprocess.run(["dotnet", "run", "sketches/particles_test.cs", "--",
                        "--shot", out, "--frames", "60", *flags],
                       capture_output=True, text=True, cwd=ROOT)
    m = re.search(r"PARTICLES CALLS=(\d+) LIVE=(\d+)", p.stdout)
    if not m:
        print(f"  {name}: no report\n{p.stdout}\n{p.stderr}")
        sys.exit(1)
    return int(m.group(1)), int(m.group(2)), ("PARTICLES PASS" in p.stdout)


def main():
    calls, live, passed = run("all", [])
    cull_calls, cull_live, cull_passed = run("behind", ["--behind"])

    print(f"  all visible   : {calls} calls, {live} particles")
    print(f"  one off-screen: {cull_calls} calls, {cull_live} particles")
    print()

    checks = [
        ("sketch checks pass", passed and cull_passed, "both runs reported PASS"),
        ("cost is per system", calls <= MAX_CALLS and live >= MIN_PARTICLES,
         f"{live} particles in {calls} calls (one-per-particle would be {live})"),
        ("off-screen system is culled", cull_calls < calls,
         f"{calls} -> {cull_calls} calls with one emitter behind the camera"),
        ("culled system still simulates", cull_live > MIN_PARTICLES,
         f"{cull_live} particles alive while one system is off-screen"),
    ]

    ok = True
    for name, good, detail in checks:
        print(f"  {'ok  ' if good else 'FAIL'} {name:30s} {detail}")
        ok &= good

    print()
    print("PARTICLES PASS — cost is per system, off-screen systems draw nothing"
          if ok else "PARTICLES FAIL")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
