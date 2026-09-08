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
    private readonly InstancedLitMeshShader _instLit = new();
    private readonly InstancedPbrMeshShader _instPbr = new();
    private readonly InstancedUnlitMeshShader _instUnlit = new();
    private readonly InstancedDepthShader _instDepth = new();
    private readonly SkyShader _sky = new();
    private ShadowMap? _shadowMap;

    /// <summary>Below this many copies of one (mesh, material), a plain loop beats filling and
    /// uploading an instance buffer. Above it, one draw call replaces N.</summary>
    private const int InstanceThreshold = 4;

    private uint _instanceVbo;
    private float[] _instanceData = new float[Mesh.FloatsPerInstance * 256];
    private readonly Dictionary<(Mesh, Material), List<Matrix4x4>> _batches = new();
    private readonly List<(Mesh mesh, Material mat, List<Matrix4x4> xforms)> _instanced = new();
    private readonly List<(Matrix4x4 world, Mesh mesh, Material mat)> _single = new();

    // Per-pass visible subsets of _queue / _skinned.
    private readonly List<(Matrix4x4 world, Mesh mesh, Material mat)> _visible = new();
    private readonly List<(Matrix4x4 world, Mesh mesh, Material mat, float depth)> _transparent = new();
    private readonly List<(Matrix4x4 world, SkinnedMeshComponent smc)> _visibleSkinned = new();
    private readonly List<(Matrix4x4 world, Mesh mesh, Material mat)> _casters = new();
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
        _visible.Clear();
        _visibleSkinned.Clear();
        _transparent.Clear();
        _casters.Clear();
        _billboards.Clear();

        // Light 0 is always the environment sun (keeps day/night etc. working).
        _lightCount = 0;
        _lights[_lightCount++] = new GpuLight
        {
            Type = 0,
            Vec = Vector3.Normalize(-env.Sun.Direction),   // "toward light"
            Color = env.Sun.Color,
        };

        // One scene walk, then a frustum filter PER PASS. What the sun sees and what the camera
        // sees are different sets: an object behind the camera still casts a shadow into view, so
        // culling the shadow pass with the camera's frustum would delete shadows that belong.
        _cullingEnabled = env.Culling;
        Collect(scene.Root, cam.Position);

        var camFrustum = new Frustum(cam.ViewMatrix * cam.ProjectionMatrix(vw, vh));
        _visible.Clear();
        _transparent.Clear();
        foreach (var r in _queue)
        {
            if (!InView(camFrustum, r.mesh, r.world)) continue;
            if (r.mat.IsTransparent)
            {
                // Sort key is distance from the camera to the object's centre. Per-object rather
                // than per-triangle: a proper solution would sort triangles, but that costs a
                // rebuild every frame and per-object is right for the cases this engine has
                // (water planes, glass panes, foliage cards).
                float d = Vector3.DistanceSquared(cam.Position, r.world.Translation);
                _transparent.Add((r.world, r.mesh, r.mat, d));
            }
            else _visible.Add(r);
        }

        _visibleSkinned.Clear();
        foreach (var s in _skinned)
            if (InView(camFrustum, s.smc.Mesh, s.world, pad: 0.5f)) _visibleSkinned.Add(s);

        // ── shadow pass: render depth from the sun before anything shades ──
        var shadowCfg = env.Shadows;
        bool shadows = shadowCfg.Enabled && (_queue.Count > 0 || _skinned.Count > 0);
        if (shadows)
        {
            _shadowMap ??= new ShadowMap(shadowCfg.Resolution);
            _shadowMap.Resize(shadowCfg.Resolution);
            RenderShadowPass(cam, env);
        }

        var sunToward = Vector3.Normalize(-env.Sun.Direction);
        var frame = new FrameParams(
            cam.ProjectionMatrix(vw, vh), cam.ViewMatrix, cam.Position,
            env.Ambient, _lights, _lightCount,
            shadowsOn: shadows,
            lightViewProj: _shadowMap?.LightViewProj ?? Matrix4x4.Identity,
            shadowBias: shadowCfg.Bias,
            shadowTexel: _shadowMap != null ? 1f / _shadowMap.Resolution : 0f,
            shadowSoftness: shadowCfg.SoftnessTexels,
            shadowStrength: shadowCfg.Strength,
            sky: env.SkyGradient, fog: env.Fog,
            sunToward: sunToward, sunTint: env.Sun.Color);

        // Sky first, in place of the clear: no depth trick needed, and it costs what the clear did.
        if (env.SkyGradient.Enabled) DrawSky(env, cam, sunToward, vw, vh);

        if (shadows) _shadowMap!.BindForReading();

        BuildBatches(env.Instancing, _visible);
        DrawInstancedBatches(frame);
        DrawSingles(frame);

        DrawSkinned(frame);
        DrawTransparent(frame);
        DrawBillboards(frame, cam);
    }

    /// <summary>Fullscreen gradient sky. Depth test and write are off — it fills every pixel and
    /// nothing has been drawn yet, so there is nothing to occlude or be occluded by.</summary>
    private void DrawSky(WorldEnvironment env, Camera cam, Vector3 sunToward, int vw, int vh)
    {
        var vp = cam.ViewMatrix * cam.ProjectionMatrix(vw, vh);
        if (!Matrix4x4.Invert(vp, out var invVp)) return;

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);

        _sky.Use();
        _sky.SetSky(env.SkyGradient, sunToward, env.Sun.Color);
        _sky.SetCamera(invVp, cam.Position);
        _quad.Draw();

        _gl.DepthMask(true);
        _gl.Enable(EnableCap.DepthTest);
    }

    /// <summary>Split the draw queue into batches that are worth instancing and the rest.
    /// Batching is by (mesh, material) — the pair that has to be identical for one draw call to
    /// stand in for many.</summary>
    private void BuildBatches(bool instancingEnabled, List<(Matrix4x4 world, Mesh mesh, Material mat)> source)
    {
        foreach (var l in _batches.Values) l.Clear();
        _instanced.Clear();
        _single.Clear();

        if (!instancingEnabled)
        {
            foreach (var r in source) _single.Add(r);
            return;
        }

        foreach (var (world, mesh, mat) in source)
        {
            var key = (mesh, mat);
            if (!_batches.TryGetValue(key, out var list)) { list = new List<Matrix4x4>(); _batches[key] = list; }
            list.Add(world);
        }

        foreach (var ((mesh, mat), xforms) in _batches)
        {
            if (xforms.Count == 0) continue;
            if (xforms.Count >= InstanceThreshold) _instanced.Add((mesh, mat, xforms));
            else foreach (var w in xforms) _single.Add((w, mesh, mat));
        }
    }

    private InstancedMeshShader InstancedShaderFor(Material m) => m.Shading switch
    {
        MaterialShading.Unlit => _instUnlit,
        MaterialShading.Pbr => _instPbr,
        _ => _instLit,
    };

    private void DrawInstancedBatches(in FrameParams frame)
    {
        if (_instanced.Count == 0) return;

        InstancedMeshShader? bound = null;
        foreach (var (mesh, mat, xforms) in _instanced)
        {
            // A material with a custom shader can't be instanced — its program has no per-instance
            // attributes — so it falls back rather than silently rendering with the wrong shader.
            if (mat.HasCustomShader)
            {
                foreach (var w in xforms) _single.Add((w, mesh, mat));
                continue;
            }

            UploadInstances(mesh, xforms);

            var shader = InstancedShaderFor(mat);
            if (!ReferenceEquals(shader, bound)) { shader.Use(); shader.SetFrame(frame); bound = shader; }

            if (mat.DoubleSided) _gl.Disable(EnableCap.CullFace);
            else _gl.Enable(EnableCap.CullFace);

            shader.SetMaterial(mat);
            mesh.DrawInstanced(xforms.Count);
        }
    }

    private void DrawSingles(in FrameParams frame)
    {
        if (_single.Count == 0) return;

        foreach (var list in _groups.Values) list.Clear();
        foreach (var r in _single)
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
    }

    /// <summary>Pack transforms into the shared instance buffer and point this mesh's VAO at it.</summary>
    private unsafe void UploadInstances(Mesh mesh, List<Matrix4x4> xforms)
    {
        if (_instanceVbo == 0) _instanceVbo = _gl.GenBuffer();

        int need = xforms.Count * Mesh.FloatsPerInstance;
        if (_instanceData.Length < need) _instanceData = new float[Math.Max(need, _instanceData.Length * 2)];

        for (int i = 0; i < xforms.Count; i++)
        {
            var m = xforms[i];
            int o = i * Mesh.FloatsPerInstance;
            _instanceData[o + 0] = m.M11; _instanceData[o + 1] = m.M12; _instanceData[o + 2] = m.M13; _instanceData[o + 3] = m.M14;
            _instanceData[o + 4] = m.M21; _instanceData[o + 5] = m.M22; _instanceData[o + 6] = m.M23; _instanceData[o + 7] = m.M24;
            _instanceData[o + 8] = m.M31; _instanceData[o + 9] = m.M32; _instanceData[o + 10] = m.M33; _instanceData[o + 11] = m.M34;
            _instanceData[o + 12] = m.M41; _instanceData[o + 13] = m.M42; _instanceData[o + 14] = m.M43; _instanceData[o + 15] = m.M44;

            var n = Matrix4x4.Invert(m, out var inv) ? Matrix4x4.Transpose(inv) : m;
            _instanceData[o + 16] = n.M11; _instanceData[o + 17] = n.M12; _instanceData[o + 18] = n.M13;
            _instanceData[o + 19] = n.M21; _instanceData[o + 20] = n.M22; _instanceData[o + 21] = n.M23;
            _instanceData[o + 22] = n.M31; _instanceData[o + 23] = n.M32; _instanceData[o + 24] = n.M33;
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        fixed (float* p = _instanceData)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(need * sizeof(float)), p,
                           BufferUsageARB.StreamDraw);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        mesh.BindInstanceBuffer(_instanceVbo);
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

        // Cull against the LIGHT's frustum, not the camera's — the shadow map only needs what can
        // land inside the light's box, but that includes plenty the camera can't see.
        var lightFrustum = new Frustum(map.LightViewProj);
        _casters.Clear();
        foreach (var r in _queue)
            if (r.mat.CastShadows && InView(lightFrustum, r.mesh, r.world)) _casters.Add(r);

        if (_casters.Count > 0)
        {
            BuildBatches(env.Instancing, _casters);

            if (_instanced.Count > 0)
            {
                _instDepth.Use();
                _instDepth.SetFrame(map.LightViewProj);
                foreach (var (mesh, mat, xforms) in _instanced)
                {
                    if (mat.HasCustomShader) { foreach (var w in xforms) _single.Add((w, mesh, mat)); continue; }
                    // A double-sided material has no back faces to hide acne behind, so the
                    // front-face culling in BeginPass would drop it out of the map entirely.
                    if (mat.DoubleSided) _gl.Disable(EnableCap.CullFace);
                    else _gl.Enable(EnableCap.CullFace);
                    _instDepth.SetMaterial(mat);
                    UploadInstances(mesh, xforms);
                    mesh.DrawInstanced(xforms.Count);
                }
            }

            if (_single.Count > 0)
            {
                _depth.Use();
                _depth.SetFrame(map.LightViewProj);
                foreach (var (world, mesh, mat) in _single)
                {
                    if (mat.DoubleSided) _gl.Disable(EnableCap.CullFace);
                    else _gl.Enable(EnableCap.CullFace);
                    _depth.SetObject(world, mat);
                    mesh.Draw();
                }
            }
        }

        if (_skinned.Count > 0)
        {
            _skinnedDepth.Use();
            _skinnedDepth.SetFrame(map.LightViewProj);
            foreach (var (world, smc) in _skinned)
            {
                if (!InView(lightFrustum, smc.Mesh, world, pad: 0.5f)) continue;
                if (smc.Material.DoubleSided) _gl.Disable(EnableCap.CullFace);
                else _gl.Enable(EnableCap.CullFace);
                smc.Skin.UpdatePalette(world, _frame);
                _skinnedDepth.SetJoints(smc.Skin.JointMatrices, smc.Skin.JointCount);
                _skinnedDepth.SetObject(world, smc.Material);
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
        if (_visibleSkinned.Count == 0) return;

        SkinnedMeshShader? bound = null;
        foreach (var (world, smc) in _visibleSkinned)
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

    /// <summary>
    /// Alpha-blended geometry, after everything opaque, sorted far-to-near, with depth WRITES off.
    ///
    /// <para>All three matter. Drawing after opaque means transparent surfaces composite over a
    /// finished background. Sorting far-to-near makes overlapping panes blend in the right order —
    /// unsorted, the result depends on which happened to be drawn first, so the same two panes look
    /// different from either side. And leaving depth writes on would let the nearest transparent
    /// surface reject everything behind it, which is exactly the bug where water hides the sea
    /// floor.</para>
    ///
    /// <para>No instancing here: a batch draws in one call and therefore in one order, which is the
    /// opposite of what sorting needs.</para>
    /// </summary>
    private void DrawTransparent(in FrameParams frame)
    {
        if (_transparent.Count == 0) return;

        _transparent.Sort(static (a, b) => b.depth.CompareTo(a.depth));   // far to near

        _gl.DepthMask(false);
        MeshShader? bound = null;

        foreach (var (world, mesh, mat, _) in _transparent)
        {
            var shader = ShaderFor(mat);
            if (!ReferenceEquals(shader, bound)) { shader.Use(); shader.SetFrame(frame); bound = shader; }

            // Transparent surfaces are usually meant to be seen from both sides — a pane of glass
            // with its back face culled vanishes when you walk around it.
            if (mat.DoubleSided) _gl.Disable(EnableCap.CullFace);
            else _gl.Enable(EnableCap.CullFace);

            var nrm = Matrix4x4.Invert(world, out var inv) ? Matrix4x4.Transpose(inv) : world;
            shader.SetObject(world, nrm, mat);
            mesh.Draw();
        }

        _gl.DepthMask(true);
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
    private void Collect(SimObject o, Vector3 camPos)
    {
        // Hidden subtrees cost one test, not a walk. Lights inside them go too, which is what you
        // want — a hidden lamp shouldn't still light the room.
        if (!o.Visible) return;

        var comps = o.Components;
        for (int i = 0; i < comps.Count; i++)
        {
            switch (comps[i])
            {
                case MeshComponent mc:
                    _queue.Add((o.WorldMatrix, mc.Mesh, mc.Material));
                    break;
                case LodComponent lod:
                {
                    // Resolve the level here and push the result as an ordinary draw, so frustum
                    // culling, instancing, transparency and shadows all work on it unchanged — and
                    // every instance that picked the same level batches into one call.
                    var world = o.WorldMatrix;
                    float dist = Vector3.Distance(camPos, world.Translation);
                    float scale = MathF.Max(new Vector3(world.M11, world.M12, world.M13).Length(), 1e-4f);
                    if (lod.Select(dist, scale, out var lodMesh, out var lodMat))
                        _queue.Add((world, lodMesh, lodMat));
                    break;
                }
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
        for (int i = 0; i < ch.Count; i++) Collect(ch[i], camPos);
    }

    /// <summary>Frustum test for one mesh under one transform. <paramref name="pad"/> grows the
    /// bounds by a fraction of their own size — used for skinned meshes, whose stored bounds are
    /// the BIND pose and so understate a limb mid-animation.</summary>
    private bool InView(in Frustum f, Mesh mesh, in Matrix4x4 world, float pad = 0f)
    {
        if (!_cullingEnabled) return true;
        var b = Frustum.TransformBounds(new Bounds(mesh.BoundsMin, mesh.BoundsMax), world);
        if (pad > 0f) b = b.Expanded(b.Size.Length() * pad);
        return f.Intersects(b);
    }

    private bool _cullingEnabled;

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
        _instLit.Dispose();
        _instPbr.Dispose();
        _instUnlit.Dispose();
        _instDepth.Dispose();
        _sky.Dispose();
        if (_instanceVbo != 0) _gl.DeleteBuffer(_instanceVbo);
        _shadowMap?.Dispose();
        _billboard.Dispose();
        _quad.Dispose();
    }
}
