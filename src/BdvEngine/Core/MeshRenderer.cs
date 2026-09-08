using System.Numerics;
using Silk.NET.OpenGL;

namespace BdvEngine;

/// <summary>
/// The 3D dispatch: walks the scene collecting mesh draws AND lights, then groups the draws by
/// shader family (<see cref="Material.Shading"/> → Unlit / Lit / PBR-lite, or a material's custom
/// <see cref="MeshShader"/>), binds each shader once per frame (with the collected lights), and
/// draws — setting cull state per material. Lighting is the scene's lights: <c>Environment.Sun</c>
/// as light 0, plus every <see cref="LightComponent"/> in the graph (Phase 6), up to
/// <see cref="MeshShader.MaxLights"/>.
/// </summary>
internal sealed class MeshRenderer : IDisposable
{
    private readonly GL _gl = Gfx.Gl;
    private readonly UnlitMeshShader _unlit = new();
    private readonly LitMeshShader _lit = new();
    private readonly PbrMeshShader _pbr = new();
    private readonly SkinnedLitMeshShader _skinnedLit = new();
    private readonly SkinnedPbrMeshShader _skinnedPbr = new();
    private readonly DepthShader _depth = new();
    private readonly SkinnedDepthShader _skinnedDepth = new();
    private ShadowMap? _shadowMap;
    private readonly BillboardShader _billboard = new();
    private readonly Mesh _quad = UnitQuad();

    private readonly List<(Matrix4x4 world, Mesh mesh, Material mat)> _queue = new();
    // Skinned draws are a separate lane: each needs its own joint palette bound, so they can't be
    // batched by shader the way static draws are.
    private readonly List<(Matrix4x4 world, SkinnedMeshComponent smc)> _skinned = new();
    private int _frame;
    private readonly Dictionary<MeshShader, List<(Matrix4x4 world, Mesh mesh, Material mat)>> _groups = new();
    private readonly List<(Vector3 anchor, BillboardComponent bb)> _billboards = new();
    private readonly GpuLight[] _lights = new GpuLight[MeshShader.MaxLights];
    private int _lightCount;

    private static Mesh UnitQuad()
    {
        // pos(3) + normal(3, unused) + uv(2), centred, v flipped so textures are upright.
        var v = new float[]
        {
            -0.5f, -0.5f, 0f,  0f, 0f, 1f,  0f, 1f,
             0.5f, -0.5f, 0f,  0f, 0f, 1f,  1f, 1f,
             0.5f,  0.5f, 0f,  0f, 0f, 1f,  1f, 0f,
            -0.5f,  0.5f, 0f,  0f, 0f, 1f,  0f, 0f,
        };
        return new Mesh(v, new ushort[] { 0, 1, 2, 0, 2, 3 });
    }

    private MeshShader ShaderFor(Material m)
        => m.CustomShader as MeshShader
           ?? m.Shading switch
           {
               MaterialShading.Unlit => _unlit,
               MaterialShading.Pbr => _pbr,
               _ => _lit,
           };

    public void Render(Scene scene, Camera cam, WorldEnvironment env, int vw, int vh)
    {
        _frame++;
        _queue.Clear();
        _skinned.Clear();
        _billboards.Clear();

        // Light 0 is always the environment sun (keeps day/night etc. working).
        _lightCount = 0;
        _lights[_lightCount++] = new GpuLight
        {
            Type = 0,
            Vec = Vector3.Normalize(-env.Sun.Direction),   // "toward light"
            Color = env.Sun.Color,
        };

        Collect(scene.Root);

        // ── shadow pass: render depth from the sun before anything shades ──
        var shadowCfg = env.Shadows;
        bool shadows = shadowCfg.Enabled && (_queue.Count > 0 || _skinned.Count > 0);
        if (shadows)
        {
            _shadowMap ??= new ShadowMap(shadowCfg.Resolution);
            _shadowMap.Resize(shadowCfg.Resolution);
            RenderShadowPass(cam, env);
        }

        var frame = new FrameParams(
            cam.ProjectionMatrix(vw, vh), cam.ViewMatrix, cam.Position,
            env.Ambient, _lights, _lightCount,
            shadowsOn: shadows,
            lightViewProj: _shadowMap?.LightViewProj ?? Matrix4x4.Identity,
            shadowBias: shadowCfg.Bias,
            shadowTexel: _shadowMap != null ? 1f / _shadowMap.Resolution : 0f,
            shadowSoftness: shadowCfg.SoftnessTexels,
            shadowStrength: shadowCfg.Strength);

        if (shadows) _shadowMap!.BindForReading();

        // Group draws by shader so each program is bound (and lit) once.
        foreach (var list in _groups.Values) list.Clear();
        foreach (var r in _queue)
        {
            var sh = ShaderFor(r.mat);
            if (!_groups.TryGetValue(sh, out var list)) { list = new(); _groups[sh] = list; }
            list.Add(r);
        }

        foreach (var (shader, list) in _groups)
        {
            if (list.Count == 0) continue;
            shader.Use();
            shader.SetFrame(frame);
            foreach (var (world, mesh, mat) in list)
            {
                if (mat.DoubleSided) _gl.Disable(EnableCap.CullFace);
                else _gl.Enable(EnableCap.CullFace);

                var nrm = Matrix4x4.Invert(world, out var inv) ? Matrix4x4.Transpose(inv) : world;
                shader.SetObject(world, nrm, mat);
                mesh.Draw();
            }
        }

        DrawSkinned(frame);
        DrawBillboards(frame, cam);
    }

    /// <summary>
    /// Depth-only pass from the sun's point of view, filling the shadow map. Runs over the same
    /// two queues the main pass uses, so anything that draws also casts — no separate opt-in to
    /// forget.
    ///
    /// <para>The light frustum is centred on the camera's target rather than the whole scene: a box
    /// big enough for an island would waste nearly all its texels on geometry nobody is looking at.
    /// The trade is that shadows stop at <see cref="ShadowSettings.Distance"/> from the focus.</para>
    /// </summary>
    private void RenderShadowPass(Camera cam, WorldEnvironment env)
    {
        var map = _shadowMap!;
        map.BeginPass(cam.Target, env.Sun.Direction, env.Shadows.Distance);

        if (_queue.Count > 0)
        {
            _depth.Use();
            _depth.SetFrame(map.LightViewProj);
            foreach (var (world, mesh, mat) in _queue)
            {
                // A double-sided material has no back faces to hide acne behind, so the front-face
                // culling in BeginPass would drop it out of the shadow map entirely.
                if (mat.DoubleSided) _gl.Disable(EnableCap.CullFace);
                else _gl.Enable(EnableCap.CullFace);
                _depth.SetObject(world);
                mesh.Draw();
            }
        }

        if (_skinned.Count > 0)
        {
            _skinnedDepth.Use();
            _skinnedDepth.SetFrame(map.LightViewProj);
            foreach (var (world, smc) in _skinned)
            {
                if (smc.Material.DoubleSided) _gl.Disable(EnableCap.CullFace);
                else _gl.Enable(EnableCap.CullFace);
                smc.Skin.UpdatePalette(world, _frame);
                _skinnedDepth.SetJoints(smc.Skin.JointMatrices, smc.Skin.JointCount);
                _skinnedDepth.SetObject(world);
                smc.Mesh.Draw();
            }
        }

        map.EndPass();
    }

    /// <summary>Skinned meshes. Grouped by shader like the static lane, but each draw also binds
    /// its skeleton's joint palette. The palette is computed on the <see cref="Skin"/> and is
    /// frame-guarded, so a character split across several materials pays for it once.</summary>
    private void DrawSkinned(in FrameParams frame)
    {
        if (_skinned.Count == 0) return;

        SkinnedMeshShader? bound = null;
        foreach (var (world, smc) in _skinned)
        {
            var shader = smc.Material.Shading == MaterialShading.Pbr ? _skinnedPbr : (SkinnedMeshShader)_skinnedLit;
            if (!ReferenceEquals(shader, bound))
            {
                shader.Use();
                shader.SetFrame(frame);
                bound = shader;
            }

            if (smc.Material.DoubleSided) _gl.Disable(EnableCap.CullFace);
            else _gl.Enable(EnableCap.CullFace);

            smc.Skin.UpdatePalette(world, _frame);
            shader.SetJoints(smc.Skin.JointMatrices, smc.Skin.JointCount);

            var nrm = Matrix4x4.Invert(world, out var inv) ? Matrix4x4.Transpose(inv) : world;
            shader.SetObject(world, nrm, smc.Material);
            smc.Mesh.Draw();
        }
    }

    // Camera-facing sprites, drawn after the meshes: depth-tested (so they hide behind geometry)
    // but not depth-writing (so overlapping billboards blend), never culled.
    private void DrawBillboards(in FrameParams frame, Camera cam)
    {
        if (_billboards.Count == 0) return;

        var fwd = Vector3.Normalize(cam.Target - cam.Position);
        var right = Vector3.Normalize(Vector3.Cross(fwd, cam.Up));
        var up = Vector3.Cross(right, fwd);

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);

        _billboard.Use();
        _billboard.SetUniform("u_proj", frame.Proj);
        _billboard.SetUniform("u_view", frame.View);
        _billboard.SetUniform("u_camRight", right);
        _billboard.SetUniform("u_camUp", up);

        foreach (var (anchor, bb) in _billboards)
        {
            _billboard.SetUniform("u_worldPos", anchor);
            _billboard.SetUniform("u_size", new Vector2(bb.Width, bb.Height));
            _billboard.SetUniform("u_color", bb.Material.Color.ToVector4());
            if (bb.Material.DiffuseTexture != null)
            {
                bb.Material.DiffuseTexture.Activate(0);
                _billboard.SetUniform("u_diffuse", 0);
            }
            _quad.Draw();
        }

        _gl.DepthMask(true);
    }

    // Single walk: mesh components → draw queue, light components → light array.
    private void Collect(SimObject o)
    {
        var comps = o.Components;
        for (int i = 0; i < comps.Count; i++)
        {
            switch (comps[i])
            {
                case MeshComponent mc:
                    _queue.Add((o.WorldMatrix, mc.Mesh, mc.Material));
                    break;
                case SkinnedMeshComponent smc:
                    _skinned.Add((o.WorldMatrix, smc));
                    break;
                case LightComponent lc when _lightCount < MeshShader.MaxLights:
                    _lights[_lightCount++] = ToGpu(lc, o.WorldMatrix);
                    break;
                case BillboardComponent bb:
                    _billboards.Add((o.WorldMatrix.Translation + bb.Offset, bb));
                    break;
            }
        }
        var ch = o.Children;
        for (int i = 0; i < ch.Count; i++) Collect(ch[i]);
    }

    private static GpuLight ToGpu(LightComponent lc, in Matrix4x4 world) => new()
    {
        Type = lc.Type == LightType.Point ? 1 : 0,
        Vec = lc.Type == LightType.Point ? world.Translation : Vector3.Normalize(-lc.Direction),
        Color = lc.Color * lc.Intensity,
        Range = lc.Range,
    };

    public void Dispose()
    {
        _unlit.Dispose();
        _lit.Dispose();
        _pbr.Dispose();
        _skinnedLit.Dispose();
        _skinnedPbr.Dispose();
        _depth.Dispose();
        _skinnedDepth.Dispose();
        _shadowMap?.Dispose();
        _billboard.Dispose();
        _quad.Dispose();
    }
}
