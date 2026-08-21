namespace BdvEngine.Gui;

/// <summary>
/// 2D data plot widget. Holds a list of named series; each series produces its
/// data via a callback (re-evaluated every frame, like <see cref="LiveLabel"/>)
/// or from a static list. The plot computes axis ranges automatically across
/// all series unless overridden via <see cref="WithRange"/>.
///
/// Renders inside its absolute rect: optional background + border, optional
/// gridlines, optional axes with tick labels, then each series on top. All
/// drawing uses the existing engine primitives — <see cref="Draw.Line"/>,
/// <see cref="SpriteBatcher.DrawSolid"/>, <see cref="TextRenderer.DrawScreen"/>
/// — so no new shader is required.
///
/// Stage-B-Line scope: only line series. Bar / Area / Function series come in
/// later stages but the <see cref="PlotSeries"/> structure is already
/// extensible for them.
/// </summary>
public sealed class Plot : Element
{
    public Color? Background;
    public Color? Border;
    public float BorderThickness = 1f;

    /// <summary>Padding inside the plot rect that's reserved for axis labels
    /// + tick marks. Higher = more room for big numbers, less plotting area.</summary>
    public float Padding = 32f;

    /// <summary>Show numeric tick labels along the X axis.</summary>
    public bool ShowXLabels = true;
    /// <summary>Show numeric tick labels along the Y axis.</summary>
    public bool ShowYLabels = true;
    /// <summary>Draw a horizontal+vertical axis line at the plot's left+bottom edge.</summary>
    public bool ShowAxes = true;

    /// <summary>Number of grid divisions on each axis. Zero = no grid.</summary>
    public int GridX = 5;
    public int GridY = 4;

    public Color AxisColor  = new(180, 190, 210, 220);
    public Color GridColor  = new( 70,  85, 110, 110);
    public Color LabelColor = new(200, 210, 230, 230);
    public float LabelScale = 0.20f;
    /// <summary>Label format string passed to .ToString(). e.g. "F0" for
    /// integer ticks, "F2" for two-decimal. Default auto-picks based on range.</summary>
    public string? LabelFormat;
    public Font? Font;

    /// <summary>Manual range override. Null on either side = auto from series data.</summary>
    public float? XMin, XMax, YMin, YMax;

    public List<PlotSeries> Series { get; } = new();

    public Plot(float x, float y, float w, float h)
    { X = x; Y = y; Width = w; Height = h; Pickable = false; }

    // ── Fluent config ─────────────────────────────────────────────────────

    public Plot WithBackground(Color c) { Background = c; return this; }
    public Plot WithBorder(Color c, float t = 1f) { Border = c; BorderThickness = t; return this; }
    public Plot WithFont(Font f, float labelScale = 0.20f) { Font = f; LabelScale = labelScale; return this; }
    public Plot WithPadding(float pad) { Padding = pad; return this; }
    public Plot WithAxes(bool show) { ShowAxes = show; return this; }
    public Plot WithGrid(int xDiv, int yDiv) { GridX = xDiv; GridY = yDiv; return this; }
    public Plot WithLabels(bool x, bool y) { ShowXLabels = x; ShowYLabels = y; return this; }
    public Plot WithLabelFormat(string fmt) { LabelFormat = fmt; return this; }
    public Plot WithAxisColor(Color c) { AxisColor = c; return this; }
    public Plot WithGridColor(Color c) { GridColor = c; return this; }
    public Plot WithRange(float? xMin = null, float? xMax = null, float? yMin = null, float? yMax = null)
    { XMin = xMin; XMax = xMax; YMin = yMin; YMax = yMax; return this; }

    // ── Series builders ───────────────────────────────────────────────────

    /// <summary>Add a live line series — getter is invoked each frame so
    /// the plot tracks evolving data (population over time, etc.).</summary>
    public Plot AddLine(string name, Func<IList<(float X, float Y)>> getter, Color color, float width = 1f)
    {
        Series.Add(new PlotSeries
        {
            Name = name, Color = color, LineWidth = width,
            Kind = PlotSeriesKind.Line, PointsGetter = getter,
        });
        return this;
    }

    /// <summary>Add a static line series — points fixed at construction time.</summary>
    public Plot AddLine(string name, IList<(float X, float Y)> points, Color color, float width = 1f)
        => AddLine(name, () => points, color, width);

    /// <summary>Sample <paramref name="fn"/> at <paramref name="samples"/> evenly-spaced
    /// X values in [xMin, xMax] and plot as a line series.</summary>
    public Plot AddFunction(string name, Func<float, float> fn, float xMin, float xMax,
        int samples, Color color, float width = 1f)
    {
        var pts = new List<(float, float)>(samples);
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1);
            float x = xMin + (xMax - xMin) * t;
            pts.Add((x, fn(x)));
        }
        return AddLine(name, pts, color, width);
    }

    // ── Render ────────────────────────────────────────────────────────────

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var (rx, ry, rw, rh) = AbsoluteRect();
        if (rw <= 1f || rh <= 1f) { base.Render(ctx); return; }

        // Background + border first so series draw on top.
        var w = ctx.ToWorld(rx, ry);
        float ws = ctx.WorldScale;
        if (Background.HasValue)
            SpriteBatcher.DrawSolid(w.X, w.Y, rw * ws, rh * ws,
                GuiHelpers.Apply(Background.Value, this), SpriteLayer.UIBack);

        // Reserve padding for tick labels on the left/bottom (and a thin
        // breath of margin on the right/top so the data line never hugs
        // the border). Y labels need horizontal room; X labels need
        // vertical room.
        float padL = ShowYLabels ? Padding : 6f;
        float padR = 6f;
        float padT = 6f;
        float padB = ShowXLabels ? Padding * 0.6f : 6f;
        float plotX = rx + padL;
        float plotY = ry + padT;
        float plotW = rw - padL - padR;
        float plotH = rh - padT - padB;
        if (plotW <= 1f || plotH <= 1f)
        {
            if (Border.HasValue)
                Draw.RectOutline(w.X, w.Y, rw * ws, rh * ws, GuiHelpers.Apply(Border.Value, this));
            base.Render(ctx);
            return;
        }

        // Resolve data ranges. Auto = scan all series; manual override on
        // any side wins. Pad the auto Y range by 5% so the highest value
        // doesn't kiss the top edge.
        var (xMin, xMax, yMin, yMax) = ResolveRange();
        float xSpan = MathF.Max(1e-6f, xMax - xMin);
        float ySpan = MathF.Max(1e-6f, yMax - yMin);

        // Gridlines under everything else.
        if (GridX > 0 || GridY > 0) DrawGrid(ctx, plotX, plotY, plotW, plotH);

        // Axes — bottom + left lines.
        if (ShowAxes) DrawAxes(ctx, plotX, plotY, plotW, plotH);

        // Tick labels.
        if (ShowXLabels || ShowYLabels)
            DrawLabels(ctx, plotX, plotY, plotW, plotH, xMin, xMax, yMin, yMax);

        // Series — line strips. Each adjacent pair becomes one Draw.Line
        // call. Out-of-range points still draw (clamping happens in the
        // transform itself), which lets the line gracefully exit the plot
        // area if data spikes past the override range.
        foreach (var s in Series)
        {
            if (s.Kind != PlotSeriesKind.Line) continue;
            var pts = s.PointsGetter?.Invoke();
            if (pts == null || pts.Count < 2) continue;
            Color seriesColor = GuiHelpers.Apply(s.Color, this);

            (float sx0, float sy0) = ToScreen(pts[0], plotX, plotY, plotW, plotH, xMin, ySpan, xSpan, yMin);
            for (int i = 1; i < pts.Count; i++)
            {
                (float sx1, float sy1) = ToScreen(pts[i], plotX, plotY, plotW, plotH, xMin, ySpan, xSpan, yMin);
                var w0 = ctx.ToWorld(sx0, sy0);
                var w1 = ctx.ToWorld(sx1, sy1);
                Draw.Line(w0.X, w0.Y, w1.X, w1.Y, seriesColor);
                sx0 = sx1; sy0 = sy1;
            }
        }

        if (Border.HasValue)
            Draw.RectOutline(w.X, w.Y, rw * ws, rh * ws, GuiHelpers.Apply(Border.Value, this));

        base.Render(ctx);
    }

    /// <summary>Map a single data point into screen pixels inside the plot area.</summary>
    private static (float, float) ToScreen((float X, float Y) p,
        float plotX, float plotY, float plotW, float plotH,
        float xMin, float ySpan, float xSpan, float yMin)
    {
        float fx = (p.X - xMin) / xSpan;
        float fy = (p.Y - yMin) / ySpan;
        // Y is flipped — data Y grows up, screen Y grows down.
        return (plotX + fx * plotW, plotY + (1f - fy) * plotH);
    }

    private (float xMin, float xMax, float yMin, float yMax) ResolveRange()
    {
        bool haveAny = false;
        float xMin = float.MaxValue, xMax = float.MinValue;
        float yMin = float.MaxValue, yMax = float.MinValue;
        foreach (var s in Series)
        {
            var pts = s.PointsGetter?.Invoke();
            if (pts == null) continue;
            foreach (var p in pts)
            {
                haveAny = true;
                if (p.X < xMin) xMin = p.X;
                if (p.X > xMax) xMax = p.X;
                if (p.Y < yMin) yMin = p.Y;
                if (p.Y > yMax) yMax = p.Y;
            }
        }
        if (!haveAny) { xMin = 0; xMax = 1; yMin = 0; yMax = 1; }
        // Pad Y range by 5% so peaks don't touch the top of the plot area.
        float ySpan = yMax - yMin;
        if (ySpan == 0f) { yMax = yMin + 1f; }
        else             { yMax += ySpan * 0.05f; }

        return (
            XMin ?? xMin,
            XMax ?? xMax,
            YMin ?? yMin,
            YMax ?? yMax);
    }

    private void DrawGrid(Context ctx, float plotX, float plotY, float plotW, float plotH)
    {
        Color c = GuiHelpers.Apply(GridColor, this);
        // Vertical gridlines.
        for (int i = 1; i < GridX; i++)
        {
            float fx = i / (float)GridX;
            float sx = plotX + fx * plotW;
            var w0 = ctx.ToWorld(sx, plotY);
            var w1 = ctx.ToWorld(sx, plotY + plotH);
            Draw.Line(w0.X, w0.Y, w1.X, w1.Y, c);
        }
        // Horizontal gridlines.
        for (int i = 1; i < GridY; i++)
        {
            float fy = i / (float)GridY;
            float sy = plotY + fy * plotH;
            var w0 = ctx.ToWorld(plotX, sy);
            var w1 = ctx.ToWorld(plotX + plotW, sy);
            Draw.Line(w0.X, w0.Y, w1.X, w1.Y, c);
        }
    }

    private void DrawAxes(Context ctx, float plotX, float plotY, float plotW, float plotH)
    {
        Color c = GuiHelpers.Apply(AxisColor, this);
        // Y axis (left edge of plot area).
        var yA = ctx.ToWorld(plotX, plotY);
        var yB = ctx.ToWorld(plotX, plotY + plotH);
        Draw.Line(yA.X, yA.Y, yB.X, yB.Y, c);
        // X axis (bottom edge of plot area).
        var xA = ctx.ToWorld(plotX, plotY + plotH);
        var xB = ctx.ToWorld(plotX + plotW, plotY + plotH);
        Draw.Line(xA.X, xA.Y, xB.X, xB.Y, c);
    }

    private void DrawLabels(Context ctx, float plotX, float plotY, float plotW, float plotH,
        float xMin, float xMax, float yMin, float yMax)
    {
        var font = Font ?? ctx.DefaultFont;
        if (font == null) return;
        Color c = GuiHelpers.Apply(LabelColor, this);
        string fmt = LabelFormat ?? AutoFormat(yMax - yMin);

        // Y labels — print at every horizontal gridline (incl. top + bottom).
        if (ShowYLabels)
        {
            int ticks = MathF.Max(1, GridY) > 0 ? GridY : 4;
            for (int i = 0; i <= ticks; i++)
            {
                float fy = i / (float)ticks;
                float v = yMin + (yMax - yMin) * fy;
                float sy = plotY + (1f - fy) * plotH;
                string txt = v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
                TextRenderer.DrawScreen(font, txt,
                    plotX - 4f, sy + font.Ascent * LabelScale * 0.4f,
                    LabelScale, c, ctx.Camera, ctx.ViewportW, ctx.ViewportH,
                    default, TextAlign.Right);
            }
        }
        // X labels — at every vertical gridline (incl. left + right).
        if (ShowXLabels)
        {
            int ticks = MathF.Max(1, GridX) > 0 ? GridX : 4;
            for (int i = 0; i <= ticks; i++)
            {
                float fx = i / (float)ticks;
                float v = xMin + (xMax - xMin) * fx;
                float sx = plotX + fx * plotW;
                string txt = v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
                TextRenderer.DrawScreen(font, txt,
                    sx, plotY + plotH + font.Ascent * LabelScale + 2f,
                    LabelScale, c, ctx.Camera, ctx.ViewportW, ctx.ViewportH,
                    default, TextAlign.Center);
            }
        }
    }

    /// <summary>Pick a sensible numeric format based on the axis span: integers
    /// for ranges ≥ 10, one decimal in [1..10), two decimals below 1.</summary>
    private static string AutoFormat(float span)
    {
        float a = MathF.Abs(span);
        if (a >= 100f) return "F0";
        if (a >= 10f)  return "F0";
        if (a >= 1f)   return "F1";
        return "F2";
    }
}

public enum PlotSeriesKind
{
    Line,
    // Reserved for stage B-Bar / B-Area / B-Function — same Series list,
    // renderer adds another switch arm per kind.
    Bar,
    Area,
}

/// <summary>One named data series inside a <see cref="Plot"/>. The plot
/// re-invokes <see cref="PointsGetter"/> every frame so the series can track
/// live data without rebuilding the widget.</summary>
public sealed class PlotSeries
{
    public string Name = "";
    public Color Color = Color.White;
    public float LineWidth = 1f;
    public PlotSeriesKind Kind = PlotSeriesKind.Line;
    public Func<IList<(float X, float Y)>>? PointsGetter;
}
