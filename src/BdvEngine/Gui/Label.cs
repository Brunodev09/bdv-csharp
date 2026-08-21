using System;
using System.Collections.Generic;

namespace BdvEngine.Gui;

/// <summary>
/// Static or animated text. Reads the default font from the Context unless one is
/// set explicitly. Width/Height aren't required — text spans whatever the font
/// metrics produce — but if you set them they participate in hit testing (off by
/// default since labels usually shouldn't intercept clicks).
///
/// Rich text supports <c>&lt;link=id&gt;…&lt;/link&gt;</c> spans for
/// inline clickable mentions. Register handlers per id via
/// <see cref="OnLink"/>; clicking the span fires the handler.
/// Auto-flips <see cref="Element.Pickable"/> to true when any link
/// handlers are registered.
/// </summary>
public class Label : Element
{
    public string Text;
    public Color TextColor = Color.White;
    public float Scale = 0.4f;
    public TextAlign Align = TextAlign.Left;
    public TextAnim Anim;
    public Font? Font;
    /// <summary>If true, wrap to the element's Width by inserting line breaks.</summary>
    public bool WordWrap;
    /// <summary>If true, parse simple inline tags: &lt;color=#rrggbb&gt;…&lt;/color&gt;.</summary>
    public bool RichText;
    /// <summary>If true AND Width &gt; 0, automatically shrink
    /// <see cref="Scale"/> at render time so the text never extends
    /// past the element's right edge. Caller's Scale is treated as
    /// the maximum — the renderer scales down (clamped to
    /// <see cref="MinAutoFitScale"/>) but never up. Pairs naturally
    /// with a fixed-width parent (sidebar row, modal section line).
    /// </summary>
    public bool AutoFit = true;
    /// <summary>Floor on the auto-fit scale so tiny rects don't
    /// collapse text to invisibility. 0.08 ≈ a microscopic but
    /// still-legible 6-7 px line height on a 64-px atlas font.</summary>
    public float MinAutoFitScale = 0.08f;

    /// <summary>Per-link-id click handlers. When a click lands inside
    /// the screen rect of a <c>&lt;link=id&gt;</c> span, the matching
    /// handler fires. Use <see cref="OnLink"/> to register.</summary>
    private Dictionary<string, Action>? _linkHandlers;
    /// <summary>Screen-space rects collected from the last RichText
    /// render — populated by the rich-text renderer when link
    /// handlers are present. Hit-tested on pointer-down.</summary>
    private List<TextRenderer.RichLinkSpan>? _linkSpans;
    /// <summary>Throttle stamp for the link-render diagnostic so it
    /// doesn't spam the console every frame.</summary>
    private int _lastDiagSec = -1;

    public Label(float x, float y, string text)
    {
        X = x; Y = y; Text = text; Pickable = false;
    }

    public Label WithFont(Font font) { Font = font; return this; }
    public Label WithScale(float scale) { Scale = scale; return this; }
    public Label WithColor(Color color) { TextColor = color; return this; }
    public Label WithAlign(TextAlign align) { Align = align; return this; }
    public Label WithAnim(TextAnim anim) { Anim = anim; return this; }
    public Label Wrap(bool wrap = true) { WordWrap = wrap; return this; }
    public Label Rich(bool rich = true) { RichText = rich; return this; }
    /// <summary>Disable the default auto-shrink behaviour. Use only
    /// when the caller WANTS overflow (e.g. a marquee, or text the
    /// caller is sizing itself).</summary>
    public Label NoAutoFit() { AutoFit = false; return this; }

    /// <summary>Register a click handler for <c>&lt;link=id&gt;</c>
    /// spans inside the label's rich text. First call also flips the
    /// label to <c>Pickable = true</c> + collects span rects on
    /// every render so the pointer-down hit-test has data.</summary>
    public Label OnLink(string id, Action handler)
    {
        _linkHandlers ??= new Dictionary<string, Action>();
        _linkSpans    ??= new List<TextRenderer.RichLinkSpan>();
        _linkHandlers[id] = handler;
        Pickable = true;
        return this;
    }

    /// <summary>Labels with registered link handlers are pickable
    /// ONLY where their link spans cover — not across the full
    /// (often-large) bounding rect. Without this override, a Label
    /// sized for layout convenience (e.g. Height=4000 inside a
    /// scrolled body) would intercept every click in its bounds
    /// and starve everything underneath.</summary>
    public override bool ContainsScreenPoint(float sx, float sy)
    {
        if (_linkHandlers == null || _linkSpans == null) return base.ContainsScreenPoint(sx, sy);
        foreach (var span in _linkSpans)
            if (span.Contains(sx, sy)) return true;
        return false;
    }

    public override void OnPointerDown(PointerEvent e)
    {
        // Only intercept when there are registered handlers AND the
        // click lands inside one of the collected link rects. Other
        // clicks fall through to the base behaviour so a plain Label
        // doesn't accidentally swallow input on its (often-zero)
        // bounding rect.
        if (_linkHandlers != null && _linkSpans != null)
        {
            // Temporary diagnostic — surfaces in stdout when a click
            // lands on a rich-text label with link handlers but no
            // span matches. Lets us see whether the issue is "no
            // spans collected" vs "spans don't cover click point".
            bool any = false;
            foreach (var span in _linkSpans)
            {
                if (!span.Contains(e.X, e.Y)) continue;
                any = true;
                if (_linkHandlers.TryGetValue(span.Id, out var handler))
                {
                    System.Console.WriteLine($"[link-click] hit id={span.Id} at ({e.X:F0},{e.Y:F0})");
                    handler();
                    return;
                }
            }
            if (!any)
            {
                System.Console.WriteLine($"[link-click] click at ({e.X:F0},{e.Y:F0}) — no span match (spans={_linkSpans.Count})");
                if (_linkSpans.Count > 0)
                {
                    var s0 = _linkSpans[0];
                    System.Console.WriteLine($"[link-click]   first span: id={s0.Id} rect=({s0.X:F0},{s0.Y:F0} {s0.W:F0}x{s0.H:F0})");
                }
            }
        }
        base.OnPointerDown(e);
    }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        var font = Font ?? ctx.DefaultFont;
        if (font == null) return;
        var (rx, ry, rw, _) = AbsoluteRect();
        Color baseColor = GuiHelpers.Apply(TextColor, this);

        // Auto-fit: when caller set a Width and the text would spill
        // past it, shrink Scale just enough to fit. Done before any
        // draw / span collection so link rects, glyph quads, and
        // alignment all use the same effective scale.
        float drawScale = ComputeRenderScale(font, rw);

        if (RichText)
        {
            // Single-line rich (color tags + link tags). When link
            // handlers are registered, collect span rects so the
            // pointer-down hit-test can dispatch.
            TextRenderer.DrawScreenRich(font, Text, rx, ry + font.Ascent * drawScale, drawScale,
                baseColor, ctx.Camera, ctx.ViewportW, ctx.ViewportH,
                _linkHandlers != null ? _linkSpans : null);
            // Diagnostic — every ~2s, print first span vs the label's
            // origin so we can compare against the cursor position.
            if (_linkHandlers != null && _linkSpans != null && _linkSpans.Count > 0
                && (int)(Time.TotalF * 0.5f) != _lastDiagSec)
            {
                _lastDiagSec = (int)(Time.TotalF * 0.5f);
                var s = _linkSpans[0];
                System.Console.WriteLine(
                    $"[link-render] label origin=({rx:F0},{ry:F0}) " +
                    $"first-span id={s.Id} rect=({s.X:F0},{s.Y:F0} {s.W:F0}x{s.H:F0}) total={_linkSpans.Count}");
            }
            base.Render(ctx);
            return;
        }

        if (WordWrap && rw > 0)
        {
            float y = ry + font.Ascent * drawScale;
            foreach (var line in TextRenderer.Wrap(font, Text, rw, drawScale))
            {
                TextRenderer.DrawScreen(font, line, rx, y, drawScale, baseColor,
                    ctx.Camera, ctx.ViewportW, ctx.ViewportH, Anim, Align);
                y += font.LineAdvance * drawScale;
            }
        }
        else
        {
            TextRenderer.DrawScreen(font, Text, rx, ry + font.Ascent * drawScale, drawScale, baseColor,
                ctx.Camera, ctx.ViewportW, ctx.ViewportH, Anim, Align);
        }
        base.Render(ctx);
    }

    /// <summary>Effective scale for this render — either <see cref="Scale"/>
    /// verbatim or a shrunk-down value just large enough to fit the
    /// rect's width. SINGLE-LINE only: multi-line content lets one
    /// long row dictate the scale for every row, which crushes the
    /// readable rows down to nothing (regressed the event log + the
    /// tech list to illegible 6-px text in real play). Multi-line
    /// callers should size their rect to fit their widest line or
    /// opt in to <see cref="WordWrap"/> explicitly.</summary>
    private float ComputeRenderScale(Font font, float rw)
    {
        if (!AutoFit || WordWrap || rw <= 0f || string.IsNullOrEmpty(Text)) return Scale;
        string raw = RichText ? StripTags(Text) : Text;
        if (string.IsNullOrEmpty(raw)) return Scale;
        // Bail on multi-line text — each line is independent; shrinking
        // them all to fit the worst case is the wrong shape for log /
        // list UIs. Single-line is where the spill-past-the-rect bug
        // actually showed up and where AutoFit unambiguously helps.
        if (raw.IndexOf('\n') >= 0) return Scale;
        float width = font.Measure(raw);
        if (width <= 0f) return Scale;
        float fits = rw / width;
        if (fits >= Scale) return Scale; // already fits
        return Math.Max(MinAutoFitScale, fits);
    }

    /// <summary>Strip <c>&lt;...&gt;</c> tags so AutoFit measures the
    /// visible characters only (matches what the rich renderer would
    /// actually draw — color/link tags don't consume glyph cells).</summary>
    private static string StripTags(string s)
    {
        if (s.IndexOf('<') < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '<')
            {
                int end = s.IndexOf('>', i + 1);
                if (end > i) { i = end + 1; continue; }
            }
            sb.Append(s[i]); i++;
        }
        return sb.ToString();
    }
}
