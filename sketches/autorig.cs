#:project ../src/BdvEngine/BdvEngine.csproj
// Procedurally rig and animate a STATIC model, to see the skinning path run on real content.
//
//   dotnet run sketches/autorig.cs -- --model "/path/to/model.glb" --shot /tmp/rig.png --frames 60
//   dotnet run sketches/autorig.cs -- --model "..." --bones 10 --bend 22 --shot /tmp/rig.png
//
// Most downloaded models are static sculpts: no skeleton, no clips, nothing to play. This builds a
// throwaway spine up the model's height, weights every vertex to the two nearest bones by height,
// and drives it with a travelling sine wave.
//
// This is a TEST RIG, not animation. It proves the pipeline end to end on a real mesh and gives
// something moving to look at; it has no anatomy, no joints where limbs actually bend, and it will
// smear an arm exactly as a height-based weighting must. For real animation the model needs
// rigging in Blender (or use an already-rigged character).
using BdvEngine;
using System;
using System.Collections.Generic;
using System.Numerics;

var cli = Environment.GetCommandLineArgs();
string path = Arg("--model") ?? "sketches/assets/bendy.glb";
int boneCount = int.TryParse(Arg("--bones"), out var bc) ? Math.Clamp(bc, 2, Skin.MaxJoints) : 8;
float bendDeg = float.TryParse(Arg("--bend"), out var bd) ? bd : 14f;

Sketch.Run(setup: w =>
{
    w.Environment.Sky = new Vector3(0.50f, 0.58f, 0.70f);
    w.Environment.Ambient = new Vector3(0.40f, 0.41f, 0.46f);
    w.Add(new DirectionalLight(new Vector3(0.45f, -0.72f, 0.52f)));

    var model = w.Load(path);
    w.Scene.RebakeMatrices();

    // ── find the biggest mesh: on a sculpt export that's the whole body ──
    SimObject? host = null;
    MeshComponent? src = null;
    Pick(model.Object, ref host, ref src);
    if (src == null) { Console.Error.WriteLine("[rig] no mesh found"); return; }

    var mesh = src.Mesh;
    if (mesh.IsSkinned) { Console.Error.WriteLine("[rig] already skinned — nothing to do"); return; }

    // Bounds in the mesh's own space, which is what the vertex data is in.
    float minY = mesh.BoundsMin.Y, maxY = mesh.BoundsMax.Y;
    float height = MathF.Max(maxY - minY, 1e-4f);
    float step = height / (boneCount - 1);
    Console.WriteLine($"[rig] {mesh.VertexCount:N0} verts, height {height:F2}, {boneCount} bones");

    // ── rebuild the mesh with joints + weights ──
    var srcData = mesh.VertexData;
    var verts = new float[mesh.VertexCount * Mesh.SkinnedFloatsPerVertex];
    for (int i = 0; i < mesh.VertexCount; i++)
    {
        int si = i * mesh.Stride;
        int di = i * Mesh.SkinnedFloatsPerVertex;
        for (int k = 0; k < 8; k++) verts[di + k] = srcData[si + k];   // pos, normal, uv

        // Blend between the two bones bracketing this vertex's height. Linear in height, which is
        // why a horizontal arm smears: every vertex along it sits at one height and so follows one
        // bone regardless of how far out it reaches.
        float t = (srcData[si + 1] - minY) / step;
        int b0 = Math.Clamp((int)MathF.Floor(t), 0, boneCount - 1);
        int b1 = Math.Clamp(b0 + 1, 0, boneCount - 1);
        float f = Math.Clamp(t - b0, 0f, 1f);

        verts[di + 8] = b0; verts[di + 9] = b1; verts[di + 10] = 0; verts[di + 11] = 0;
        verts[di + 12] = 1f - f; verts[di + 13] = f; verts[di + 14] = 0; verts[di + 15] = 0;
    }

    Mesh skinnedMesh = mesh.Indices32.Length > 0
        ? new Mesh(verts, mesh.Indices32.ToArray(), skinned: true)
        : new Mesh(verts, mesh.Indices16.ToArray(), skinned: true);

    // ── the spine: each bone a child of the one below, so a rotation carries everything above ──
    var joints = new SimObject[boneCount];
    var inverseBind = new Matrix4x4[boneCount];
    SimObject? parent = null;
    for (int i = 0; i < boneCount; i++)
    {
        var b = new SimObject(w.NextId(), $"spine{i}");
        b.Transform.Position = i == 0 ? new Vector3(0, minY, 0) : new Vector3(0, step, 0);
        if (parent == null) host!.AddChild(b); else parent.AddChild(b);
        parent = b;
        joints[i] = b;
        // Bind pose is the un-posed spine, so the inverse simply undoes the bone's rest height.
        inverseBind[i] = Matrix4x4.CreateTranslation(0, -(minY + i * step), 0);
    }
    w.Scene.RebakeMatrices();

    // Swap the static mesh for the skinned one ON THE SAME OBJECT, so the rigged copy inherits the
    // host's transform and sits exactly where the original did. Add before removing: the new
    // component takes a material reference before the old one releases its own.
    var skin = new Skin(joints, inverseBind);
    var rigged = host!;
    rigged.AddComponent(new SkinnedMeshComponent(skinnedMesh, src.Material.Name, skin));
    rigged.RemoveComponent(src);

    // ── a travelling wave up the spine ──
    var channels = new List<AnimationChannel>();
    const int Keys = 33;
    var times = new float[Keys];
    for (int k = 0; k < Keys; k++) times[k] = k / (float)(Keys - 1) * 2f;

    for (int i = 0; i < boneCount; i++)
    {
        // Bone 0 stays put so the model doesn't slide off its feet; the rest lean progressively
        // more, with a phase offset that makes the bend travel upward.
        float amount = MathF.Sin(i / (float)(boneCount - 1) * MathF.PI * 0.5f) * bendDeg;
        var values = new float[Keys * 4];
        for (int k = 0; k < Keys; k++)
        {
            float phase = times[k] / 2f * MathF.Tau - i * 0.45f;
            float a = MathF.Sin(phase) * amount * MathF.PI / 180f;
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, a);
            values[k * 4 + 0] = q.X; values[k * 4 + 1] = q.Y;
            values[k * 4 + 2] = q.Z; values[k * 4 + 3] = q.W;
        }
        channels.Add(new AnimationChannel(joints[i], AnimationPath.Rotation,
                                          new AnimationSampler(times, values, 4, Interpolation.Linear)));
    }

    var anim = new Animator();
    anim.Add(new AnimationClip("Sway", channels));
    rigged.AddComponent(anim);
    anim.Play("Sway");

    // ── frame it ──
    var c = (mesh.BoundsMin + mesh.BoundsMax) * 0.5f;
    float radius = MathF.Max((mesh.BoundsMax - mesh.BoundsMin).Length() * 0.5f, 0.01f);
    w.Camera.Perspective(fovDegrees: 42f, near: radius * 0.01f, far: radius * 40f);
    w.Camera.AddControls(new OrbitControls(new Vector3(c.X, c.Y, c.Z),
                                           distance: radius * 2.4f, yaw: 0.4f, pitch: 0.10f));
    w.Environment.Shadows.Distance = radius * 1.8f;
    w.Add(Primitives.Plane(radius * 10f)).At(c.X, mesh.BoundsMin.Y, c.Z)
     .Material(Materials.Standard("ground", new Color(126, 134, 122)));

    Console.WriteLine($"[rig] rigged and playing 'Sway' ({bendDeg:F0} deg max lean)");
});

static void Pick(SimObject o, ref SimObject? host, ref MeshComponent? best)
{
    foreach (var c in o.Components)
        if (c is MeshComponent mc && (best == null || mc.Mesh.VertexCount > best.Mesh.VertexCount))
        {
            best = mc;
            host = o;
        }
    foreach (var ch in o.Children) Pick(ch, ref host, ref best);
}

string? Arg(string flag)
{
    int i = Array.IndexOf(cli, flag);
    return i >= 0 && i + 1 < cli.Length ? cli[i + 1] : null;
}
