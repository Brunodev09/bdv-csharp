using System;

namespace BdvEngine.Gui;

/// <summary>
/// CSS-flexbox-style container. Children flow along
/// <see cref="Direction"/>; <see cref="Justify"/> distributes
/// leftover space along the main axis; <see cref="Align"/> handles
/// the cross axis. Every child's <see cref="Element.WidthSpec"/> /
/// <see cref="Element.HeightSpec"/> is consulted per axis; unset
/// specs fall back to the raw pixel <see cref="Element.Width"/> /
/// <see cref="Element.Height"/> so widgets constructed the old way
/// still work.
///
/// Layout runs in <see cref="Update"/> before <see cref="Element.Render"/>,
/// so children's absolute rects reflect the flex-assigned size the
/// same frame they're computed. Cheap: single pass with two child
/// walks per container.
/// </summary>
public class FlexContainer : Panel
{
    public FlexDirection Direction = FlexDirection.Row;
    public FlexJustify   Justify   = FlexJustify.Start;
    /// <summary>Cross-axis default is <see cref="FlexAlign.Stretch"/>
    /// to match CSS. Without this, a child that hadn't declared its
    /// cross-axis size collapses to 0 on that axis (it falls back to
    /// the raw pixel Width/Height field, which is zero for widgets
    /// built from JSON without an explicit rect).</summary>
    public FlexAlign     Align     = FlexAlign.Stretch;
    public float         Gap;
    public Insets        Padding;
    public bool          Wrap;

    public FlexContainer(float x, float y, float w, float h) : base(x, y, w, h) { }

    /// <summary>Fluent presets so game code doesn't have to touch
    /// individual fields when building a container manually.</summary>
    public FlexContainer AsRow()    { Direction = FlexDirection.Row;    return this; }
    public FlexContainer AsColumn() { Direction = FlexDirection.Column; return this; }
    public FlexContainer WithGap(float g)    { Gap = g;         return this; }
    public FlexContainer WithPadding(float p){ Padding = Insets.All(p); return this; }

    public override void Update(Context ctx)
    {
        Layout();
        base.Update(ctx);
    }

    /// <summary>Recompute each child's X / Y / Width / Height. Runs
    /// every frame — children may have changed size (e.g. label text
    /// grew) and we want the layout to follow. Cost is negligible
    /// (2 walks × N children) even for panels with dozens of widgets.</summary>
    private void Layout()
    {
        var rect = ContentRect();
        float availMain  = MainOf(rect.W, rect.H);
        float availCross = CrossOf(rect.W, rect.H);

        // Pass 1 — sum fixed / auto / percent sizes on main axis and
        // total flex weight. Cache measured sizes so we don't
        // re-measure in pass 2.
        Span<float> sizes = Children.Count <= 64
            ? stackalloc float[Children.Count]
            : new float[Children.Count];
        float fixedMain = 0;
        float totalFlex = 0;
        int   visibleN = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.Visible) { sizes[i] = 0; continue; }
            visibleN++;
            var spec = SpecOnAxis(c, main: true);
            float s = ResolveNonFlex(c, spec, availMain, main: true);
            sizes[i] = s;
            if (spec.HasValue && spec.Value.Mode == SizeMode.Flex)
                totalFlex += spec.Value.Value;
            else
                fixedMain += s;
        }
        float gapTotal = Gap * System.Math.Max(0, visibleN - 1);
        float mainRemaining = System.Math.Max(0, availMain - fixedMain - gapTotal);

        // Pass 2 — assign flex sizes, then position children along the
        // main axis with the chosen Justify.
        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.Visible) continue;
            var spec = SpecOnAxis(c, main: true);
            if (spec.HasValue && spec.Value.Mode == SizeMode.Flex)
                sizes[i] = totalFlex > 0 ? mainRemaining * spec.Value.Value / totalFlex : 0;
        }

        // ── Main-axis packing.
        float sumMain = 0;
        for (int i = 0; i < Children.Count; i++) if (Children[i].Visible) sumMain += sizes[i];
        float leftover = availMain - sumMain - gapTotal;
        float startOffset = Justify switch
        {
            FlexJustify.Center => leftover * 0.5f,
            FlexJustify.End    => leftover,
            _                  => 0,
        };
        float betweenBonus = Justify switch
        {
            FlexJustify.SpaceBetween => visibleN > 1 ? leftover / (visibleN - 1) : 0,
            FlexJustify.SpaceAround  => visibleN > 0 ? leftover / visibleN : 0,
            FlexJustify.SpaceEvenly  => visibleN > 0 ? leftover / (visibleN + 1) : 0,
            _                        => 0,
        };
        if (Justify == FlexJustify.SpaceAround) startOffset = betweenBonus * 0.5f;
        if (Justify == FlexJustify.SpaceEvenly) startOffset = betweenBonus;

        float cursor = MainOf(rect.X, rect.Y) + startOffset;

        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.Visible) continue;

            var wSpec = SpecOnAxis(c, main: false);
            float crossSize = ResolveCross(c, wSpec, availCross);
            float crossPos = CrossOf(rect.X, rect.Y) + Align switch
            {
                FlexAlign.Center => (availCross - crossSize) * 0.5f,
                FlexAlign.End    =>  availCross - crossSize,
                _                => 0,
            };
            if (Align == FlexAlign.Stretch && !wSpec.HasValue) crossSize = availCross;

            // Push the computed rect down onto the child. Flex
            // children ignore their own anchor — we drive absolute
            // position via X / Y with default (0,0) anchor.
            c.AnchorMin = System.Numerics.Vector2.Zero;
            c.AnchorMax = System.Numerics.Vector2.Zero;
            c.Pivot     = System.Numerics.Vector2.Zero;

            var (ax, ay) = MainCrossToXY(cursor, crossPos);
            var (aw, ah) = MainCrossToXY(sizes[i], crossSize);
            var (px, py, _, _) = AbsoluteRect();
            // Convert absolute back to parent-relative for the field.
            c.X = ax - px;
            c.Y = ay - py;
            c.Width = aw;
            c.Height = ah;

            cursor += sizes[i] + Gap + (Justify is FlexJustify.SpaceBetween or FlexJustify.SpaceAround or FlexJustify.SpaceEvenly ? betweenBonus : 0);
        }
    }

    // ── Helpers ────────────────────────────────────────────────

    private (float X, float Y, float W, float H) ContentRect()
    {
        var (rx, ry, rw, rh) = AbsoluteRect();
        return (rx + Padding.Left,
                ry + Padding.Top,
                rw - Padding.Left - Padding.Right,
                rh - Padding.Top  - Padding.Bottom);
    }

    private float MainOf(float w, float h)  => Direction == FlexDirection.Row ? w : h;
    private float CrossOf(float w, float h) => Direction == FlexDirection.Row ? h : w;
    private (float, float) MainCrossToXY(float main, float cross)
        => Direction == FlexDirection.Row ? (main, cross) : (cross, main);

    private Sizing? SpecOnAxis(Element c, bool main)
    {
        bool horizontal = Direction == FlexDirection.Row;
        return (main == horizontal) ? c.WidthSpec : c.HeightSpec;
    }

    /// <summary>Resolve a non-flex spec (Fixed/Auto/Percent) to a
    /// concrete pixel size. Auto measures the child's intrinsic size
    /// (labels: text width; containers: children total; else the raw
    /// Width/Height field).</summary>
    private float ResolveNonFlex(Element c, Sizing? spec, float avail, bool main)
    {
        if (!spec.HasValue)
        {
            // Fall back to the raw Width/Height field for legacy
            // widgets that don't use SizeSpec.
            return main
                ? (Direction == FlexDirection.Row  ? c.Width  : c.Height)
                : (Direction == FlexDirection.Row  ? c.Height : c.Width);
        }
        var s = spec.Value;
        return s.Mode switch
        {
            SizeMode.Fixed   => s.Value,
            SizeMode.Percent => avail * s.Value * 0.01f,
            SizeMode.Auto    => MeasureAuto(c, main),
            SizeMode.Flex    => 0,   // deferred to pass 2
            _                => 0,
        };
    }

    private float ResolveCross(Element c, Sizing? spec, float avail)
    {
        if (!spec.HasValue)
        {
            return Direction == FlexDirection.Row ? c.Height : c.Width;
        }
        var s = spec.Value;
        return s.Mode switch
        {
            SizeMode.Fixed   => s.Value,
            SizeMode.Percent => avail * s.Value * 0.01f,
            SizeMode.Auto    => MeasureAuto(c, main: false),
            SizeMode.Flex    => avail,
            _                => 0,
        };
    }

    /// <summary>Rough content-size measurement. Labels use font
    /// metrics if available; other widgets fall back to their raw
    /// pixel Width/Height. Good enough for the flex pass — refinement
    /// (multi-line labels, nested-container measurement) can be added
    /// once real overflow cases appear.</summary>
    private static float MeasureAuto(Element c, bool main)
    {
        if (c is Label lbl && lbl.Font != null)
        {
            // rough: charW ≈ Font.LineAdvance * 0.55 at scale 1
            float charW = lbl.Font.LineAdvance * 0.55f * lbl.Scale;
            float lineH = lbl.Font.LineAdvance         * lbl.Scale;
            return main ? System.Math.Max(1, lbl.Text.Length) * charW : lineH;
        }
        return main ? c.Width : c.Height;
    }
}
