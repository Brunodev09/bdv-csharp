using System.Numerics;
using ImGuiNET;

namespace BdvEngine;

public enum UIAnchor { TopLeft, TopRight, BottomLeft, BottomRight, Center }

public abstract class UIWidget
{
    internal abstract void Draw();
}

public sealed class UIPanel
{
    public string Title { get; set; } = "";
    public UIAnchor Anchor { get; set; } = UIAnchor.TopLeft;
    public Vector2 Margin { get; set; } = new(8, 8);
    public Vector2 Size { get; set; } = new(280, 0); // 0 = auto-fit
    public bool Visible { get; set; } = true;
    public List<UIWidget> Widgets { get; } = new();
    internal int Id;
}

public sealed class UIHeading : UIWidget
{
    public string Text;
    public UIHeading(string text) => Text = text;
    internal override void Draw() { ImGui.TextColored(new Vector4(1, 1, 1, 1), Text); ImGui.Separator(); }
}

public sealed class UIText : UIWidget
{
    public Func<string> Provider;
    public Vector4 Color = new(0.9f, 0.9f, 0.9f, 1);
    public UIText(Func<string> provider) => Provider = provider;
    public UIText(string text) => Provider = () => text;
    internal override void Draw() => ImGui.TextColored(Color, Provider());
}

public sealed class UIButton : UIWidget
{
    public string Label;
    public Action OnClick;
    public UIButton(string label, Action onClick) { Label = label; OnClick = onClick; }
    internal override void Draw() { if (ImGui.Button(Label)) OnClick(); }
}

public sealed class UISlider : UIWidget
{
    public string Label;
    public float Min, Max;
    public float Value;
    public Action<float> OnChange;
    public UISlider(string label, float min, float max, float def, Action<float> onChange)
    { Label = label; Min = min; Max = max; Value = def; OnChange = onChange; }
    internal override void Draw()
    {
        float v = Value;
        if (ImGui.SliderFloat(Label, ref v, Min, Max)) { Value = v; OnChange(v); }
    }
}

public sealed class UICheckbox : UIWidget
{
    public string Label;
    public bool Value;
    public Action<bool> OnChange;
    public UICheckbox(string label, bool def, Action<bool> onChange) { Label = label; Value = def; OnChange = onChange; }
    internal override void Draw()
    {
        bool v = Value;
        if (ImGui.Checkbox(Label, ref v)) { Value = v; OnChange(v); }
    }
}

public sealed class UIInput : UIWidget
{
    public string Label;
    public string Value;
    public Action<string> OnChange;
    public UIInput(string label, string def, Action<string> onChange)
    { Label = label; Value = def ?? ""; OnChange = onChange; }
    internal override void Draw()
    {
        string v = Value;
        if (ImGui.InputText(Label, ref v, 64)) { Value = v; OnChange(v); }
    }
}

public sealed class UISpacer : UIWidget
{
    internal override void Draw() => ImGui.Dummy(new Vector2(0, 6));
}

public sealed class UIRow : UIWidget
{
    public List<UIWidget> Items { get; } = new();
    internal override void Draw()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (i > 0) ImGui.SameLine();
            Items[i].Draw();
        }
    }
}

public static class UI
{
    private static readonly List<UIPanel> _panels = new();
    private static int _nextId;

    public static void ApplyDefaultStyle()
    {
        var style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(10, 10);
        style.ItemSpacing = new Vector2(8, 6);
        style.WindowRounding = 6f;
        style.FrameRounding = 4f;
        style.GrabRounding = 4f;
        style.ScrollbarRounding = 4f;
        style.WindowBorderSize = 1f;

        var c = style.Colors;
        c[(int)ImGuiCol.Text]                  = new Vector4(0.95f, 0.96f, 0.98f, 1.00f);
        c[(int)ImGuiCol.TextDisabled]          = new Vector4(0.55f, 0.55f, 0.60f, 1.00f);
        c[(int)ImGuiCol.WindowBg]              = new Vector4(0.05f, 0.07f, 0.12f, 0.85f);
        c[(int)ImGuiCol.ChildBg]               = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        c[(int)ImGuiCol.PopupBg]               = new Vector4(0.10f, 0.12f, 0.18f, 0.95f);
        c[(int)ImGuiCol.Border]                = new Vector4(0.30f, 0.42f, 0.65f, 0.50f);
        c[(int)ImGuiCol.FrameBg]               = new Vector4(0.16f, 0.20f, 0.30f, 0.80f);
        c[(int)ImGuiCol.FrameBgHovered]        = new Vector4(0.26f, 0.36f, 0.50f, 0.85f);
        c[(int)ImGuiCol.FrameBgActive]         = new Vector4(0.40f, 0.55f, 0.75f, 0.85f);
        c[(int)ImGuiCol.TitleBg]               = new Vector4(0.08f, 0.10f, 0.16f, 0.95f);
        c[(int)ImGuiCol.TitleBgActive]         = new Vector4(0.18f, 0.30f, 0.50f, 0.95f);
        c[(int)ImGuiCol.Button]                = new Vector4(0.20f, 0.35f, 0.60f, 0.85f);
        c[(int)ImGuiCol.ButtonHovered]         = new Vector4(0.32f, 0.50f, 0.78f, 0.95f);
        c[(int)ImGuiCol.ButtonActive]          = new Vector4(0.42f, 0.60f, 0.88f, 1.00f);
        c[(int)ImGuiCol.SliderGrab]            = new Vector4(0.45f, 0.70f, 1.00f, 0.90f);
        c[(int)ImGuiCol.SliderGrabActive]      = new Vector4(0.65f, 0.85f, 1.00f, 1.00f);
        c[(int)ImGuiCol.CheckMark]             = new Vector4(0.45f, 0.70f, 1.00f, 1.00f);
        c[(int)ImGuiCol.Header]                = new Vector4(0.20f, 0.30f, 0.50f, 0.80f);
        c[(int)ImGuiCol.HeaderHovered]         = new Vector4(0.30f, 0.45f, 0.70f, 0.90f);
        c[(int)ImGuiCol.HeaderActive]          = new Vector4(0.40f, 0.60f, 0.85f, 1.00f);
    }

    public static UIPanel Panel(UIAnchor anchor = UIAnchor.TopLeft, string title = "")
    {
        var p = new UIPanel { Anchor = anchor, Title = title, Id = _nextId++ };
        _panels.Add(p);
        return p;
    }

    public static UIHeading Heading(UIPanel p, string text)
    { var w = new UIHeading(text); p.Widgets.Add(w); return w; }

    public static UIText Text(UIPanel p, string text)
    { var w = new UIText(text); p.Widgets.Add(w); return w; }

    public static UIText TextLive(UIPanel p, Func<string> provider)
    { var w = new UIText(provider); p.Widgets.Add(w); return w; }

    public static UIButton Button(UIPanel p, string label, Action onClick)
    { var w = new UIButton(label, onClick); p.Widgets.Add(w); return w; }

    public static UISlider Slider(UIPanel p, string label, float min, float max, float def, Action<float> onChange)
    { var w = new UISlider(label, min, max, def, onChange); p.Widgets.Add(w); return w; }

    public static UICheckbox Checkbox(UIPanel p, string label, bool def, Action<bool> onChange)
    { var w = new UICheckbox(label, def, onChange); p.Widgets.Add(w); return w; }

    public static UIInput Input(UIPanel p, string label, string def, Action<string> onChange)
    { var w = new UIInput(label, def, onChange); p.Widgets.Add(w); return w; }

    public static UISpacer Spacer(UIPanel p)
    { var w = new UISpacer(); p.Widgets.Add(w); return w; }

    public static UIRow Row(UIPanel p)
    { var w = new UIRow(); p.Widgets.Add(w); return w; }

    public static void RowItem(UIRow row, UIWidget widget) => row.Items.Add(widget);

    internal static void Render(int viewportW, int viewportH)
    {
        foreach (var panel in _panels)
        {
            if (!panel.Visible) continue;

            var pos = panel.Anchor switch
            {
                UIAnchor.TopLeft     => panel.Margin,
                UIAnchor.TopRight    => new Vector2(viewportW - panel.Size.X - panel.Margin.X, panel.Margin.Y),
                UIAnchor.BottomLeft  => new Vector2(panel.Margin.X, viewportH - 200 - panel.Margin.Y),
                UIAnchor.BottomRight => new Vector2(viewportW - panel.Size.X - panel.Margin.X, viewportH - 200 - panel.Margin.Y),
                UIAnchor.Center      => new Vector2(viewportW / 2f - panel.Size.X / 2f, viewportH / 2f - 100),
                _ => panel.Margin,
            };

            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);

            var flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize;
            if (string.IsNullOrEmpty(panel.Title))
                flags |= ImGuiWindowFlags.NoTitleBar;

            ImGui.Begin($"{panel.Title}##bdv_{panel.Id}", flags);
            foreach (var w in panel.Widgets) w.Draw();
            ImGui.End();
        }
    }
}
