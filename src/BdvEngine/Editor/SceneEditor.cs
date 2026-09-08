using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;

namespace BdvEngine;

/// <summary>
/// The editor is a MODE, not an application — an ImGui overlay inside the running game, toggled
/// with F1. There is no separate editor binary, no project window, no asset database: the game is
/// the editor, and the thing it edits is the <c>.scene.json</c> from
/// <see cref="SceneSerializer"/>.
///
/// <para>The loop it exists to create: <b>click an object → drag a value → see it immediately →
/// press Save</b>. No compiler anywhere in that sentence. That is the whole reason Unity feels
/// faster than a hand-rolled engine, and it is a few hundred lines rather than a rewrite because
/// the pieces were already here — <see cref="World.Pick"/> for click-select, ImGui for the
/// widgets, <see cref="Inspector"/> to generate them, and the serialiser to persist the
/// result.</para>
///
/// <para>Deliberately NOT here (scope discipline — this is not a Unity clone): docking layouts,
/// an undo stack, multi-select, play/pause/step. Nothing is written to disk until you press Save,
/// so quitting without saving is the undo.</para>
/// </summary>
public sealed class SceneEditor
{
    /// <summary>The editor hosted by the running engine, or null when it's disabled. A dev-only
    /// convenience so a sketch or game can drive the editor without threading a reference through
    /// (<c>SceneEditor.Active?.Select(node)</c>).</summary>
    public static SceneEditor? Active { get; internal set; }

    public bool Visible;
    public SimObject? Selected { get; private set; }

    /// <summary>True when the process was launched with <c>--editor</c>, so any game opens straight
    /// into level-editing (and headless <c>--shot</c> captures include the overlay). F1 toggles it
    /// either way.</summary>
    public static bool RequestedOnCommandLine
        => Array.IndexOf(Environment.GetCommandLineArgs(), "--editor") >= 0;

    /// <summary>Select a node from code — jumps the inspector to it, same as clicking it.</summary>
    public void Select(SimObject? o) => Selected = o;

    /// <summary>Write the live scene to <see cref="ScenePath"/> — what the Save button calls.
    /// Public so a game can bind its own shortcut, and so the loop is testable without a mouse.
    /// Returns false (with the reason in the editor's status line) if it couldn't write.</summary>
    public bool Save(World world)
    {
        if (string.IsNullOrWhiteSpace(ScenePath)) { Status("Set a path first."); return false; }
        try
        {
            // Save the loaded container if this path is the one it came from; otherwise bake the
            // whole world (the "turn generated content into a file" path).
            var root = ScenePath == world.LoadedScenePath ? world.LoadedSceneRoot : null;
            world.SaveScene(ScenePath, root);
            Status($"Saved {ScenePath}");
            return true;
        }
        catch (Exception e) { Status($"Save failed: {e.Message}"); return false; }
    }

    /// <summary>Duplicate the selected node — the Duplicate button, callable from code.</summary>
    public SimObject? Duplicate(World world)
    {
        if (Selected == null) return null;
        try
        {
            // Duplicate IS serialise-then-deserialise, so a copy can only contain what a save
            // would keep — no divergence between what you see and what the file gets.
            var copy = SceneSerializer.NodeFromJson(SceneSerializer.NodeToJson(Selected), world.NextId);
            copy.Name = Selected.Name + "_copy";
            (Selected.Parent ?? world.Scene.Root).AddChild(copy);
            copy.Load();
            Selected = copy;
            Status("Duplicated");
            return copy;
        }
        catch (Exception e) { Status($"Duplicate failed: {e.Message}"); return null; }
    }

    /// <summary>Where Save writes. Defaults to whatever <see cref="World.LoadScene"/> last loaded;
    /// editable in the toolbar so a code-built world can be baked to a new file.</summary>
    public string ScenePath = "";

    /// <summary>True while a gizmo handle is being dragged — the engine folds this into
    /// <see cref="InputManager.UiWantsMouse"/> so the camera doesn't orbit at the same time.</summary>
    public bool IsManipulating => _dragAxis >= 0;

    private const float HandleGrabPx = 12f;

    private Vector2 _lastMouse;
    private bool _lastLeftDown;
    private bool _lastF1Down;
    private int _dragAxis = -1;
    private string _status = "";
    private double _statusUntil;
    private bool _showEnvironment = true;
    private string _prefabPath = "prefabs/new.prefab.json";

    /// <summary>Call once per frame from the engine's ImGui pass. Safe to call every frame whether
    /// or not the editor is showing — it handles its own F1 toggle.</summary>
    public void Draw(World world, int vw, int vh)
    {
        // Own edge detection: InputManager.WasKeyPressed is cleared by EndFrame() at the end of the
        // update tick, and this runs in the render pass — so the press flag is always gone by now.
        bool f1 = InputManager.IsKeyDown(Key.F1);
        if (f1 && !_lastF1Down && !InputManager.UiWantsKeyboard) Visible = !Visible;
        _lastF1Down = f1;
        if (!Visible)
        {
            _dragAxis = -1;
            _lastLeftDown = InputManager.IsLeftDown;
            _lastMouse = InputManager.GetMousePosition();
            return;
        }

        if (Selected != null && !IsInScene(world, Selected)) Selected = null;   // survived a hot reload?
        if (string.IsNullOrEmpty(ScenePath)) ScenePath = world.LoadedScenePath ?? "";

        var mouse = InputManager.GetMousePosition();
        bool leftDown = InputManager.IsLeftDown;
        bool overUi = ImGui.GetIO().WantCaptureMouse;

        HandleGizmo(world, mouse, leftDown, overUi, vw, vh);
        HandlePick(world, mouse, leftDown, overUi, vw, vh);

        DrawHierarchy(world, vh);
        DrawInspector(world, vw, vh);
        DrawGizmo(world, vw, vh);

        _lastMouse = mouse;
        _lastLeftDown = leftDown;
    }

    private static bool IsInScene(World world, SimObject o)
    {
        for (var n = o; n != null; n = n.Parent)
            if (n == world.Scene.Root) return true;
        return false;
    }

    private void Status(string msg)
    {
        _status = msg;
        _statusUntil = Time.Total + 4.0;
    }

    // ── picking ──────────────────────────────────────────────────────────────

    private void HandlePick(World world, Vector2 mouse, bool leftDown, bool overUi, int vw, int vh)
    {
        if (overUi || _dragAxis >= 0) return;
        if (!leftDown || _lastLeftDown) return;             // press edge only
        Selected = world.Pick(world.Camera.ScreenRay(mouse.X, mouse.Y, vw, vh));
    }

    // ── translate gizmo ──────────────────────────────────────────────────────
    //
    // Three world-axis handles drawn at constant screen size. Dragging one moves the node along
    // that axis by projecting the mouse delta onto the axis's screen-space direction. Translate
    // only for now — it covers most of the value; rotate/scale can follow.

    private float GizmoLength(World world, Vector3 origin)
        => Vector3.Distance(world.Camera.Position, origin) * 0.14f;

    private void HandleGizmo(World world, Vector2 mouse, bool leftDown, bool overUi, int vw, int vh)
    {
        if (Selected == null) { _dragAxis = -1; return; }

        if (!leftDown) { _dragAxis = -1; return; }

        var origin = Selected.WorldMatrix.Translation;
        float len = GizmoLength(world, origin);
        var s0 = world.Camera.WorldToScreen(origin, vw, vh, out bool front);
        if (!front) { _dragAxis = -1; return; }

        // Start a drag: press edge, not over a panel, near a handle.
        if (!_lastLeftDown && !overUi)
        {
            for (int a = 0; a < 3; a++)
            {
                var s1 = world.Camera.WorldToScreen(origin + Axis(a) * len, vw, vh, out bool f1);
                if (!f1) continue;
                if (DistanceToSegment(mouse, s0, s1) <= HandleGrabPx) { _dragAxis = a; break; }
            }
        }

        if (_dragAxis < 0) return;

        // Continue a drag: project the mouse delta onto the axis's screen direction.
        var end = world.Camera.WorldToScreen(origin + Axis(_dragAxis) * len, vw, vh, out bool fe);
        if (!fe) return;
        var screenDir = end - s0;
        float denom = Vector2.Dot(screenDir, screenDir);
        if (denom < 1e-6f) return;

        float t = Vector2.Dot(mouse - _lastMouse, screenDir) / denom;
        var worldDelta = Axis(_dragAxis) * len * t;

        // Transform.Position is LOCAL, so a world-space drag has to come back through the parent.
        var parent = Selected.Parent;
        if (parent != null && Matrix4x4.Invert(parent.WorldMatrix, out var inv))
            worldDelta = Vector3.TransformNormal(worldDelta, inv);
        Selected.Transform.Position += worldDelta;
    }

    private void DrawGizmo(World world, int vw, int vh)
    {
        if (Selected == null) return;
        var origin = Selected.WorldMatrix.Translation;
        var s0 = world.Camera.WorldToScreen(origin, vw, vh, out bool front);
        if (!front) return;

        float len = GizmoLength(world, origin);
        var dl = ImGui.GetForegroundDrawList();
        uint[] cols =
        {
            ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.30f, 0.30f, 1f)),   // X
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 1.00f, 0.40f, 1f)),   // Y
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.60f, 1.00f, 1f)),   // Z
        };

        for (int a = 0; a < 3; a++)
        {
            var s1 = world.Camera.WorldToScreen(origin + Axis(a) * len, vw, vh, out bool f1);
            if (!f1) continue;
            bool hot = _dragAxis == a;
            dl.AddLine(s0, s1, cols[a], hot ? 4.5f : 2.5f);
            dl.AddCircleFilled(s1, hot ? 7f : 5f, cols[a]);
        }
        dl.AddCircleFilled(s0, 3.5f, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.9f)));
    }

    private static Vector3 Axis(int a) => a == 0 ? Vector3.UnitX : a == 1 ? Vector3.UnitY : Vector3.UnitZ;

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = Vector2.Dot(ab, ab);
        if (len2 < 1e-6f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    // ── hierarchy ────────────────────────────────────────────────────────────

    private void DrawHierarchy(World world, int vh)
    {
        ImGui.SetNextWindowPos(new Vector2(8, 8), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(300, Math.Min(vh - 16, 560)), ImGuiCond.FirstUseEver);
        ImGui.Begin("Hierarchy##bdv_editor", ImGuiWindowFlags.NoSavedSettings);

        DrawToolbar(world);
        ImGui.Separator();

        ImGui.BeginChild("tree", new Vector2(0, 0), ImGuiChildFlags.None);
        foreach (var child in world.Scene.Root.Children.ToList()) DrawNode(child, depth: 0);
        ImGui.EndChild();

        ImGui.End();
    }

    private void DrawToolbar(World world)
    {
        ImGui.TextDisabled("F1 closes the editor");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##path", "levels/my.scene.json", ref ScenePath, 260);

        if (ImGui.Button("Save")) Save(world);
        ImGui.SameLine();
        if (ImGui.Button("Reload"))
        {
            if (world.LoadedScenePath == null || world.LoadedSceneRoot == null) Status("Nothing loaded to reload.");
            else
                try
                {
                    world.ReloadScene(world.LoadedScenePath, world.LoadedSceneRoot);
                    Selected = null;
                    Status("Reloaded from disk");
                }
                catch (Exception e) { Status($"Reload failed: {e.Message}"); }
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"{CountNodes(world.Scene.Root) - 1} nodes");

        if (_status.Length > 0 && Time.Total < _statusUntil)
            ImGui.TextColored(new Vector4(0.55f, 0.85f, 1f, 1f), _status);
    }

    private static int CountNodes(SimObject o)
    {
        int n = 1;
        foreach (var c in o.Children) n += CountNodes(c);
        return n;
    }

    private void DrawNode(SimObject o, int depth)
    {
        ImGui.PushID(o.Id);

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (o.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (o == Selected) flags |= ImGuiTreeNodeFlags.Selected;
        if (depth < 2) flags |= ImGuiTreeNodeFlags.DefaultOpen;   // show the level, not a closed folder

        bool open = ImGui.TreeNodeEx(Label(o), flags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) Selected = o;

        if (open && o.Children.Count > 0)
        {
            foreach (var c in o.Children.ToList()) DrawNode(c, depth + 1);
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    // A node's icon says what it IS, so the tree is scannable without clicking every row.
    // ASCII only: ImGui's default font atlas has no glyphs beyond it, and anything else draws as '?'.
    private static string Label(SimObject o)
    {
        string icon = ".";
        foreach (var c in o.Components)
            icon = c switch
            {
                LightComponent => "*",
                BillboardComponent => "=",
                MeshComponent => "#",
                _ => icon,
            };
        if (o.SourceKind == AssetKind.Model) icon = "@";        // imported model
        if (o.SourceKind == AssetKind.Prefab) icon = "&";       // prefab instance
        if (o.Name.StartsWith("scene:", StringComparison.Ordinal)) icon = "+";
        return $"{icon} {o.Name}";
    }

    // ── inspector ────────────────────────────────────────────────────────────

    private void DrawInspector(World world, int vw, int vh)
    {
        ImGui.SetNextWindowPos(new Vector2(vw - 348, 8), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(340, Math.Min(vh - 16, 620)), ImGuiCond.FirstUseEver);
        ImGui.Begin("Inspector##bdv_editor", ImGuiWindowFlags.NoSavedSettings);

        DrawEnvironment(world);

        if (Selected == null)
        {
            ImGui.TextDisabled("Click an object in the viewport,");
            ImGui.TextDisabled("or pick one in the Hierarchy.");
            ImGui.End();
            return;
        }

        ImGui.Separator();

        string name = Selected.Name;
        if (ImGui.InputText("Name", ref name, 96)) Selected.Name = name;

        DrawNodeActions(world);
        DrawPrefabRow(world);
        ImGui.Separator();

        DrawTransform(Selected.Transform);

        foreach (var c in Selected.Components) DrawMember(c, c.Name, MemberKind(c), (c as BaseComponent)?.Data);
        foreach (var b in Selected.Behaviors) DrawMember(b, b.Name, "behavior", (b as BaseBehavior)?.Data);

        ImGui.End();
    }

    private static string MemberKind(IComponent c) => c switch
    {
        MeshComponent => "mesh",
        LightComponent => "light",
        BillboardComponent => "billboard",
        _ => "component",
    };

    private void DrawNodeActions(World world)
    {
        if (ImGui.Button("Duplicate")) Duplicate(world);
        ImGui.SameLine();
        if (ImGui.Button("Delete"))
        {
            var parent = Selected!.Parent;
            parent?.RemoveChild(Selected);
            Selected = null;
            Status("Deleted (not written until you Save)");
            return;
        }
        ImGui.SameLine();
        if (ImGui.Button("Add child"))
        {
            var child = new SimObject(world.NextId(), "node");
            Selected!.AddChild(child);
            child.Load();
            Selected = child;
        }
    }

    /// <summary>Prefab controls for the selected node: what it's an instance of (and how to break
    /// that link), or how to turn it into a prefab. An instance saves as its path plus a transform,
    /// so anything else you change on it is NOT persisted — say so plainly rather than letting an
    /// edit quietly vanish on the next save.</summary>
    private void DrawPrefabRow(World world)
    {
        var sel = Selected!;

        if (sel.SourceKind == AssetKind.Prefab)
        {
            ImGui.TextColored(new Vector4(0.65f, 0.85f, 1f, 1f), $"prefab instance: {sel.Source}");
            ImGui.TextDisabled("Only name + transform are saved on an instance.");
            if (ImGui.Button("Unpack"))
            {
                sel.Unpack();
                Status("Unpacked — now an ordinary node, saved in full");
            }
            ImGui.SameLine();
            if (ImGui.Button("Reload prefab"))
            {
                SceneSerializer.ClearPrefabCache();
                Status("Prefab cache cleared — reload the scene to see changes");
            }
            return;
        }
        if (sel.SourceKind == AssetKind.Model) return;

        ImGui.SetNextItemWidth(-72);
        ImGui.InputTextWithHint("##prefabpath", "prefabs/thing.prefab.json", ref _prefabPath, 260);
        ImGui.SameLine();
        if (ImGui.Button("Save as prefab"))
        {
            if (string.IsNullOrWhiteSpace(_prefabPath)) Status("Set a prefab path first.");
            else
                try
                {
                    world.SavePrefab(_prefabPath, sel);
                    Status($"Saved prefab {_prefabPath}");
                }
                catch (Exception e) { Status($"Prefab save failed: {e.Message}"); }
        }
    }

    private static void DrawTransform(Transform t)
    {
        if (!ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen)) return;

        var p = t.Position;
        if (ImGui.DragFloat3("Position", ref p, 0.01f)) t.Position = p;

        if (t.UseOrientation)
        {
            var q = new Vector4(t.Orientation.X, t.Orientation.Y, t.Orientation.Z, t.Orientation.W);
            if (ImGui.DragFloat4("Quaternion", ref q, 0.01f))
            {
                var quat = new Quaternion(q.X, q.Y, q.Z, q.W);
                t.Orientation = quat.LengthSquared() > 1e-8f ? Quaternion.Normalize(quat) : Quaternion.Identity;
            }
            if (ImGui.SmallButton("Use Euler instead")) t.UseOrientation = false;
        }
        else
        {
            var r = t.Rotation;
            if (ImGui.DragFloat3("Rotation (rad)", ref r, 0.01f)) t.Rotation = r;
        }

        var s = t.Scale;
        if (ImGui.DragFloat3("Scale", ref s, 0.01f)) t.Scale = s;
    }

    /// <summary>One collapsible section per component/behavior. Native types (mesh/light/billboard)
    /// get hand-written rows because their state is objects, not fields; everything else is
    /// generated by <see cref="Inspector"/> — which is the point: adding a tunable costs no UI.</summary>
    private void DrawMember(object member, string name, string kind, object? data)
    {
        ImGui.PushID(member.GetHashCode());
        if (ImGui.CollapsingHeader($"{name}  ({kind})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            switch (member)
            {
                case MeshComponent mc:
                    ImGui.TextDisabled($"mesh: {mc.Mesh.Source ?? "(built in code — will not save)"}");
                    ImGui.TextDisabled($"material: {mc.Material.Name}");
                    DrawMaterial(mc.Material);
                    break;

                case BillboardComponent bc:
                    ImGui.DragFloat("Width", ref bc.Width, 0.01f);
                    ImGui.DragFloat("Height", ref bc.Height, 0.01f);
                    ImGui.DragFloat3("Offset", ref bc.Offset, 0.01f);
                    DrawMaterial(bc.Material);
                    break;

                default:
                {
                    // Live object first so its fields win, then any data-bag field it does NOT
                    // mirror (construction params that only live on the bag) — same precedence the
                    // serialiser uses, so what you edit is what gets written.
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    Inspector.DrawFields(member, name, seen);
                    if (data != null && !ReferenceEquals(data, member))
                        Inspector.DrawFields(data, name + ".data", seen);
                    break;
                }
            }
        }
        ImGui.PopID();
    }

    private static void DrawMaterial(Material m)
    {
        ImGui.PushID(m.Name);
        var col = new Vector4(m.Color.RFloat, m.Color.GFloat, m.Color.BFloat, m.Color.AFloat);
        if (ImGui.ColorEdit4("Colour", ref col)) m.Color = Color.FromFloats(col.X, col.Y, col.Z, col.W);

        int shading = (int)m.Shading;
        if (ImGui.Combo("Shading", ref shading, ["Unlit", "Lit", "Pbr"], 3)) m.Shading = (MaterialShading)shading;

        if (m.Shading == MaterialShading.Pbr)
        {
            float metallic = m.Metallic, rough = m.Roughness;
            if (ImGui.SliderFloat("Metallic", ref metallic, 0f, 1f)) m.Metallic = metallic;
            if (ImGui.SliderFloat("Roughness", ref rough, 0f, 1f)) m.Roughness = rough;
        }
        bool ds = m.DoubleSided;
        if (ImGui.Checkbox("Double sided", ref ds)) m.DoubleSided = ds;
        ImGui.PopID();
    }

    private void DrawEnvironment(World world)
    {
        ImGui.SetNextItemOpen(_showEnvironment, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader("Environment")) return;
        _showEnvironment = true;

        var env = world.Environment;
        var sky = new Vector3(env.Sky.X, env.Sky.Y, env.Sky.Z);
        if (ImGui.ColorEdit3("Sky", ref sky)) env.Sky = sky;

        var amb = env.Ambient;
        if (ImGui.ColorEdit3("Ambient", ref amb)) env.Ambient = amb;

        var dir = env.Sun.Direction;
        if (ImGui.DragFloat3("Sun direction", ref dir, 0.01f) && dir.LengthSquared() > 1e-6f)
            env.Sun.Direction = Vector3.Normalize(dir);

        var sunCol = env.Sun.Color;
        if (ImGui.ColorEdit3("Sun colour", ref sunCol)) env.Sun.Color = sunCol;
    }
}
