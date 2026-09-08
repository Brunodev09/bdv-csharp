#!/usr/bin/env python3
"""Generate a minimal rigged .glb for testing skinned animation — no Blender needed.

    python3 tools/make_test_rig.py sketches/assets/bendy.glb

Produces a 2-joint capsule that bends: a 12-segment cylinder from y=0 to y=2, weighted so the
bottom follows joint0 (the root), the top follows joint1 (at y=1), and the middle blends between
them. Two clips drive joint1 over 2 seconds: "Bend" (about Z) and "Twist" (about Y) — different
axes, so a crossfade between them is unambiguous to check.

Deliberately exercises the parts most likely to be wrong:
  * inverse bind matrices that are NOT identity (joint1 is offset), so a bad matrix order shows up
    as the mesh flying off rather than as a subtle error
  * JOINTS_0 as unsigned byte and WEIGHTS_0 as float — the mixed component types real exporters use
  * blended weights across the middle rings, so linear blend skinning is actually exercised
  * a mesh node that is NOT the skeleton root, which is the case the mesh-world inverse exists for
"""
import json
import math
import struct
import sys
from pathlib import Path

SEGMENTS = 12
RINGS = [0.0, 0.5, 1.0, 1.5, 2.0]
RADIUS = 0.30


def build_geometry():
    positions, normals, uvs, joints, weights = [], [], [], [], []
    for ri, y in enumerate(RINGS):
        # Blend from joint0 to joint1 across the middle of the capsule.
        w1 = min(max((y - 0.5) / 1.0, 0.0), 1.0)
        for s in range(SEGMENTS + 1):
            a = (s / SEGMENTS) * math.tau
            nx, nz = math.cos(a), math.sin(a)
            positions.append((nx * RADIUS, y, nz * RADIUS))
            normals.append((nx, 0.0, nz))
            uvs.append((s / SEGMENTS, ri / (len(RINGS) - 1)))
            joints.append((0, 1, 0, 0))
            weights.append((1.0 - w1, w1, 0.0, 0.0))

    indices = []
    per_ring = SEGMENTS + 1
    for ri in range(len(RINGS) - 1):
        for s in range(SEGMENTS):
            a = ri * per_ring + s
            b = a + per_ring
            indices += [a, b, a + 1, a + 1, b, b + 1]
    return positions, normals, uvs, joints, weights, indices


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "sketches/assets/bendy.glb")
    positions, normals, uvs, joints, weights, indices = build_geometry()

    # ── animations: two clips, so crossfading between them can be tested ──
    times = [0.0, 0.5, 1.0, 1.5, 2.0]

    def quat(axis, deg):
        h = math.radians(deg) / 2.0
        s_ = math.sin(h)
        return (axis[0] * s_, axis[1] * s_, axis[2] * s_, math.cos(h))

    # "Bend" swings about Z; "Twist" spins about Y. Different axes so a blend between them is
    # unambiguous — a crossfade bug shows up as the pose snapping rather than as a subtle lag.
    bend_rot = [quat((0, 0, 1), d) for d in (0.0, 60.0, 0.0, -60.0, 0.0)]
    twist_rot = [quat((0, 1, 0), d) for d in (0.0, 90.0, 180.0, 270.0, 360.0)]

    # ── inverse bind matrices, COLUMN-major as glTF requires ──
    # joint0 sits at the origin; joint1 is at y=1, so its inverse bind translates by -1.
    ibm = [
        [1, 0, 0, 0,  0, 1, 0, 0,  0, 0, 1, 0,  0,  0, 0, 1],
        [1, 0, 0, 0,  0, 1, 0, 0,  0, 0, 1, 0,  0, -1, 0, 1],
    ]

    blobs, views, accessors = [], [], []

    def add(data: bytes, target=None):
        """Append a buffer view, padded to a 4-byte boundary."""
        while sum(len(b) for b in blobs) % 4:
            blobs.append(b"\x00")
        offset = sum(len(b) for b in blobs)
        blobs.append(data)
        v = {"buffer": 0, "byteOffset": offset, "byteLength": len(data)}
        if target is not None:
            v["target"] = target
        views.append(v)
        return len(views) - 1

    def accessor(view, comp_type, count, type_, **extra):
        a = {"bufferView": view, "componentType": comp_type, "count": count, "type": type_}
        a.update(extra)
        accessors.append(a)
        return len(accessors) - 1

    FLOAT, UBYTE, USHORT = 5126, 5121, 5123

    a_pos = accessor(
        add(b"".join(struct.pack("<3f", *p) for p in positions), 34962),
        FLOAT, len(positions), "VEC3",
        # POSITION requires min/max per spec; some viewers reject the file without them.
        min=[min(p[i] for p in positions) for i in range(3)],
        max=[max(p[i] for p in positions) for i in range(3)])
    a_nrm = accessor(add(b"".join(struct.pack("<3f", *n) for n in normals), 34962),
                     FLOAT, len(normals), "VEC3")
    a_uv = accessor(add(b"".join(struct.pack("<2f", *u) for u in uvs), 34962),
                    FLOAT, len(uvs), "VEC2")
    a_jnt = accessor(add(b"".join(struct.pack("<4B", *j) for j in joints), 34962),
                     UBYTE, len(joints), "VEC4")
    a_wgt = accessor(add(b"".join(struct.pack("<4f", *w) for w in weights), 34962),
                     FLOAT, len(weights), "VEC4")
    a_idx = accessor(add(struct.pack(f"<{len(indices)}H", *indices), 34963),
                     USHORT, len(indices), "SCALAR")
    a_ibm = accessor(add(b"".join(struct.pack("<16f", *m) for m in ibm)),
                     FLOAT, len(ibm), "MAT4")
    a_time = accessor(add(struct.pack(f"<{len(times)}f", *times)),
                      FLOAT, len(times), "SCALAR", min=[min(times)], max=[max(times)])
    a_bend = accessor(add(b"".join(struct.pack("<4f", *r) for r in bend_rot)),
                      FLOAT, len(bend_rot), "VEC4")
    a_twist = accessor(add(b"".join(struct.pack("<4f", *r) for r in twist_rot)),
                       FLOAT, len(twist_rot), "VEC4")

    gltf = {
        "asset": {"version": "2.0", "generator": "bdv make_test_rig"},
        "scene": 0,
        "scenes": [{"nodes": [0, 2]}],
        "nodes": [
            {"name": "joint0", "translation": [0, 0, 0], "children": [1]},
            {"name": "joint1", "translation": [0, 1, 0]},
            {"name": "capsule", "mesh": 0, "skin": 0},
        ],
        "skins": [{"name": "rig", "joints": [0, 1], "inverseBindMatrices": a_ibm, "skeleton": 0}],
        "meshes": [{
            "name": "capsule",
            "primitives": [{
                "attributes": {"POSITION": a_pos, "NORMAL": a_nrm, "TEXCOORD_0": a_uv,
                               "JOINTS_0": a_jnt, "WEIGHTS_0": a_wgt},
                "indices": a_idx,
                "material": 0,
            }],
        }],
        "materials": [{
            "name": "skin",
            "pbrMetallicRoughness": {
                "baseColorFactor": [0.85, 0.55, 0.35, 1.0],
                "metallicFactor": 0.0,
                "roughnessFactor": 0.7,
            },
        }],
        "animations": [
            {
                "name": "Bend",
                "samplers": [{"input": a_time, "output": a_bend, "interpolation": "LINEAR"}],
                "channels": [{"sampler": 0, "target": {"node": 1, "path": "rotation"}}],
            },
            {
                "name": "Twist",
                "samplers": [{"input": a_time, "output": a_twist, "interpolation": "LINEAR"}],
                "channels": [{"sampler": 0, "target": {"node": 1, "path": "rotation"}}],
            },
        ],
        "bufferViews": views,
        "accessors": accessors,
        "buffers": [{"byteLength": sum(len(b) for b in blobs)}],
    }

    bin_chunk = b"".join(blobs)
    bin_chunk += b"\x00" * ((4 - len(bin_chunk) % 4) % 4)
    json_chunk = json.dumps(gltf, separators=(",", ":")).encode()
    json_chunk += b" " * ((4 - len(json_chunk) % 4) % 4)

    total = 12 + 8 + len(json_chunk) + 8 + len(bin_chunk)
    glb = (struct.pack("<III", 0x46546C67, 2, total)
           + struct.pack("<II", len(json_chunk), 0x4E4F534A) + json_chunk
           + struct.pack("<II", len(bin_chunk), 0x004E4942) + bin_chunk)

    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(glb)
    print(f"wrote {out} ({len(glb)} bytes) — {len(positions)} verts, 2 joints, "
          f"clips 'Bend' + 'Twist' ({times[-1]:g}s each)")


if __name__ == "__main__":
    main()
