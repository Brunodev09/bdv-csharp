#:project ../src/BdvEngine/BdvEngine.csproj
// Skinned glTF animation gate.
//
//   python3 tools/make_test_rig.py sketches/assets/bendy.glb
//   dotnet run sketches/skin_test.cs -- --shot /tmp/skin.png --frames 90
//
// Loads a 2-joint rigged capsule and checks the whole chain: the skin parsed, the clip parsed,
// the Animator drives the joint, and the joint palette actually deforms vertices. The palette is
// verified by transforming a known vertex on the CPU with the same matrices the shader gets — so
// a wrong matrix order fails here with numbers rather than as a mesh that looks "sort of off".
using BdvEngine;
using System;
using System.Linq;
using System.Numerics;

ObjectHandle model = null!;
Animator anim = null!;
Skin skin = null!;
double t = 0;
int stage = 0;
Quaternion poseAt06 = Quaternion.Identity;
bool playbackAdvances = false, crossFadeBlends = false;

Sketch.Run(
    setup: w =>
    {
        w.Camera.Perspective(fovDegrees: 50);
        w.Camera.AddControls(new OrbitControls(target: new Vector3(0, 1.1f, 0), distance: 5.2f,
                                               yaw: 0.9f, pitch: 0.22f));
        w.Environment.Sky = new Vector3(0.46f, 0.55f, 0.68f);
        w.Environment.Ambient = new Vector3(0.35f, 0.35f, 0.40f);
        w.Add(new DirectionalLight(new Vector3(-0.4f, -1f, -0.45f)));
        w.AddPointLight(new Vector3(2.5f, 3f, 2.5f), Color.White, intensity: 5, range: 14);
        w.Add(GridHelper.Create(8, 8));

        model = w.Load("sketches/assets/bendy.glb");

        anim = model.Object.GetComponent<Animator>()
               ?? throw new InvalidOperationException("no Animator — animations did not parse");
        skin = FindSkin(model.Object)
               ?? throw new InvalidOperationException("no SkinnedMeshComponent — skin did not parse");

        Console.WriteLine($"[skin] clips: {string.Join(", ", anim.ClipNames)}");
        Console.WriteLine($"[skin] joints: {skin.JointCount} ({string.Join(", ", skin.Joints.Select(j => j.Name))})");
        anim.Play("Bend");
    },
    update: (w, dt) =>
    {
        t += dt;

        // ── free-running playback: the Animator must advance on its own, not just on Seek ──
        if (stage == 0 && t > 0.35) { stage = 1; poseAt06 = skin.Joints[1].Transform.Orientation; }
        else if (stage == 1 && t > 0.85)
        {
            stage = 2;
            var now = skin.Joints[1].Transform.Orientation;
            playbackAdvances = AngleBetween(poseAt06, now) > 5f;
            Console.WriteLine($"[skin] playback advanced {AngleBetween(poseAt06, now):F1} deg in 0.5s");

            // ── crossfade: mid-blend the pose must be BETWEEN the two clips, not either one ──
            anim.Play("Bend");
            anim.Seek(0.5f);                       // Bend's +60 deg about Z
            var pureBend = skin.Joints[1].Transform.Orientation;
            anim.Play("Twist");
            anim.Seek(0.5f);                       // Twist's +90 deg about Y
            var pureTwist = skin.Joints[1].Transform.Orientation;

            anim.Play("Bend");
            anim.Seek(0.5f);
            anim.CrossFade("Twist", 1.0f);
            anim.Update(0.5);                      // half-way through the fade
            var blended = skin.Joints[1].Transform.Orientation;

            float toBend = AngleBetween(blended, pureBend);
            float toTwist = AngleBetween(blended, pureTwist);
            crossFadeBlends = toBend > 3f && toTwist > 3f;
            Console.WriteLine($"[skin] mid-crossfade pose is {toBend:F1} deg from Bend, " +
                              $"{toTwist:F1} deg from Twist (must differ from both)");
        }
        else if (stage == 2) { stage = 3; Report(w); }
    }
);

static float AngleBetween(Quaternion a, Quaternion b)
    => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), -1f, 1f)) * 180f / MathF.PI;

static Skin? FindSkin(SimObject o)
{
    foreach (var c in o.Components) if (c is SkinnedMeshComponent s) return s.Skin;
    foreach (var ch in o.Children) { var r = FindSkin(ch); if (r != null) return r; }
    return null;
}

static SkinnedMeshComponent? FindSkinned(SimObject o)
{
    foreach (var c in o.Components) if (c is SkinnedMeshComponent s) return s;
    foreach (var ch in o.Children) { var r = FindSkinned(ch); if (r != null) return r; }
    return null;
}

void Report(World w)
{
    var smc = FindSkinned(model.Object)!;
    var joint1 = skin.Joints[1];

    // ── 1. the clip is driving the joint ──
    // Pin the clip explicitly: the crossfade check above left the Animator on "Twist", and these
    // numbers only mean anything against a known clip.
    anim.Play("Bend");
    anim.Seek(0f);
    w.Scene.RebakeMatrices();
    var restQ = joint1.Transform.Orientation;

    anim.Seek(0.5f);                                  // the clip's +60 deg key
    w.Scene.RebakeMatrices();
    var bentQ = joint1.Transform.Orientation;

    float deltaDeg = 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(restQ, bentQ)), -1f, 1f))
                     * 180f / MathF.PI;
    bool jointAnimated = deltaDeg > 55f && deltaDeg < 65f;

    // ── 2. the palette deforms a vertex the way the shader will ──
    // Skin the topmost ring (fully weighted to joint1) on the CPU with the same matrices.
    var meshWorld = smc.Owner.WorldMatrix;
    skin.UpdatePalette(meshWorld, frame: 12345);
    var tip = new Vector3(0.30f, 2.0f, 0f);           // a vertex on the top ring, weight = (0, 1)
    var bentTip = Vector3.Transform(tip, skin.JointMatrices[1]);

    anim.Seek(0f);
    w.Scene.RebakeMatrices();
    skin.UpdatePalette(meshWorld, frame: 12346);
    var restTip = Vector3.Transform(tip, skin.JointMatrices[1]);

    float moved = Vector3.Distance(restTip, bentTip);
    bool tipMoves = moved > 0.4f;
    // Rest pose must be a no-op: identity palette leaves the bind pose exactly where it was.
    bool restIsBind = Vector3.Distance(restTip, tip) < 1e-3f;

    // ── 3. the base of the capsule is weighted to joint0 and must NOT move ──
    anim.Seek(0.5f);
    w.Scene.RebakeMatrices();
    skin.UpdatePalette(meshWorld, frame: 12347);
    var baseV = new Vector3(0.30f, 0f, 0f);           // bottom ring, weight = (1, 0)
    float baseMoved = Vector3.Distance(Vector3.Transform(baseV, skin.JointMatrices[0]), baseV);
    bool baseAnchored = baseMoved < 1e-3f;

    bool meshIsSkinned = smc.Mesh.IsSkinned && smc.Mesh.Stride == Mesh.SkinnedFloatsPerVertex;

    Console.WriteLine(new string('-', 68));
    Console.WriteLine($"  skin + clips parsed from .glb  : {string.Join("+", anim.ClipNames)} / {skin.JointCount} joints");
    Console.WriteLine($"  mesh has skinned vertex layout : {meshIsSkinned}");
    Console.WriteLine($"  clip rotates the joint         : {jointAnimated} ({deltaDeg:F1} deg, expected 60)");
    Console.WriteLine($"  rest palette == bind pose      : {restIsBind}");
    Console.WriteLine($"  bend moves the weighted tip    : {tipMoves} ({moved:F3} units)");
    Console.WriteLine($"  root-weighted base stays put   : {baseAnchored} ({baseMoved:F4} units)");
    Console.WriteLine($"  playback advances on its own   : {playbackAdvances}");
    Console.WriteLine($"  crossfade blends between clips : {crossFadeBlends}");
    bool pass = anim.Has("Bend") && anim.Has("Twist") && meshIsSkinned && jointAnimated
                && restIsBind && tipMoves && baseAnchored && playbackAdvances && crossFadeBlends;
    Console.WriteLine(pass ? "SKINNING PASS — glTF skin + clip drive real vertex deformation"
                           : "SKINNING FAIL");
    Console.WriteLine(new string('-', 68));

    // Leave it mid-bend so the screenshot shows the deformation.
    anim.Play("Bend");
    anim.Seek(0.5f);
}
