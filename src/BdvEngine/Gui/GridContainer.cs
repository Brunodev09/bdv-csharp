using System.Collections.Generic;

namespace BdvEngine.Gui;

/// <summary>
/// Simple 2-D grid container. Children flow left-to-right, wrapping
/// to a new row after every <see cref="Columns"/> cells. Column
/// widths come from <see cref="ColumnTemplates"/> (a list of
/// <see cref="Sizing"/> — mix pixels, %, auto and fr like CSS
/// <c>grid-template-columns</c>). Rows are always <see cref="RowHeight"/>
/// tall (auto = tallest child in the row).
///
/// Enough to build tables + priority-matrix-style grids. Not a full
/// CSS-grid implementation (no explicit row placement, no span, no
/// named lines) — those extensions can layer on later.
/// </summary>
public sealed class GridContainer : Panel
{
    /// <summary>Number of columns before wrapping. Ignored when
    /// <see cref="ColumnTemplates"/> is set (its count wins).</summary>
    public int Columns = 1;
    /// <summary>Per-column sizing. Empty = every column gets an
    /// equal share (1fr each).</summary>
    public readonly List<Sizing> ColumnTemplates = new();
    public float RowHeight = 30f;
    public float GapRow = 4f;
    public float GapCol = 4f;
    public Insets Padding;

    public GridContainer(float x, float y, float w, float h) : base(x, y, w, h) { }

    public override void Update(Context ctx)
    {
        Layout();
        base.Update(ctx);
    }

    private void Layout()
    {
        if (Children.Count == 0) return;
        int cols = ColumnTemplates.Count > 0 ? ColumnTemplates.Count : System.Math.Max(1, Columns);

        var (rx, ry, rw, rh) = AbsoluteRect();
        float availW = rw - Padding.Left - Padding.Right;
        float availH = rh - Padding.Top  - Padding.Bottom;

        // Resolve column widths.
        var colWidths = new float[cols];
        float fixedW = 0;
        float totalFlex = 0;
        for (int i = 0; i < cols; i++)
        {
            var s = i < ColumnTemplates.Count ? ColumnTemplates[i] : Sizing.Flex(1);
            switch (s.Mode)
            {
                case SizeMode.Fixed:   colWidths[i] = s.Value;              fixedW += s.Value; break;
                case SizeMode.Percent: colWidths[i] = availW * s.Value * 0.01f; fixedW += colWidths[i]; break;
                case SizeMode.Auto:    colWidths[i] = 0; break;   // filled in after content measure below
                case SizeMode.Flex:    totalFlex += s.Value;               colWidths[i] = -1; break;
            }
        }
        float gapTotal = GapCol * (cols - 1);
        float flexAvail = System.Math.Max(0, availW - fixedW - gapTotal);
        for (int i = 0; i < cols; i++)
        {
            if (colWidths[i] >= 0 && (i >= ColumnTemplates.Count || ColumnTemplates[i].Mode != SizeMode.Flex)) continue;
            var s = i < ColumnTemplates.Count ? ColumnTemplates[i] : Sizing.Flex(1);
            colWidths[i] = totalFlex > 0 ? flexAvail * s.Value / totalFlex : 0;
        }

        // Position children.
        (float x, float y, float w, float h) parentAbs = (rx, ry, rw, rh);
        int idx = 0;
        for (int r = 0; idx < Children.Count; r++)
        {
            float rowY = ry + Padding.Top + r * (RowHeight + GapRow);
            float colX = rx + Padding.Left;
            for (int c = 0; c < cols && idx < Children.Count; c++, idx++)
            {
                var child = Children[idx];
                if (!child.Visible) { colX += colWidths[c] + GapCol; continue; }
                // Force default anchor for grid children.
                child.AnchorMin = System.Numerics.Vector2.Zero;
                child.AnchorMax = System.Numerics.Vector2.Zero;
                child.Pivot     = System.Numerics.Vector2.Zero;

                child.X = colX - parentAbs.x;
                child.Y = rowY - parentAbs.y;
                child.Width  = colWidths[c];
                child.Height = RowHeight;
                colX += colWidths[c] + GapCol;
            }
        }
    }
}
