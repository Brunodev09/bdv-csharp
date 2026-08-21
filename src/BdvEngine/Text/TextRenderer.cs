namespace BdvEngine;

public enum TextAlign { Left, Center, Right }

/// <summary>
/// Per-glyph quad emitter for baked fonts. Each glyph becomes a SpriteBatcher quad,
/// with optional per-glyph time-driven transforms (wave, pop, shake) and tints
/// (rainbow). Cheap enough for hundreds of strings per frame at typical lengths.
/// </summary>
public static class TextRenderer
{
    /// <summary>Draw text at world position (x, y is the baseline left).</summary>
    public static void Draw(Font font, string text, float x, float y, float scale, Color color,
        SpriteLayer layer = SpriteLayer.UI, float sortY = 0f)
        => Draw(font, text, x, y, scale, color, TextAnim.None, TextAlign.Left, layer, sortY);

    /// <summary>
    /// Draw text in screen pixels, ignoring camera pan/zoom — text stays glued to the
    /// viewport (HUD overlays, score popups, debug readouts). pixelScale = 1 means
    /// "1 source font pixel = 1 screen pixel". Animation pixel amplitudes (Wave, Shake)
    /// are also in screen pixels.
    /// </summary>
    public static void DrawScreen(Font font, string text, float screenX, float screenY,
        float pixelScale, Color color,
        Camera camera, int viewportW, int viewportH,
        TextAnim anim = default, TextAlign align = TextAlign.Left,
        SpriteLayer layer = SpriteLayer.UI, float sortY = 0f)
    {
        float invZoom = 1f / camera.Zoom;
        var world = camera.ScreenToWorld(screenX, screenY, viewportW, viewportH);
        // Pixel-amplitude anim params live in screen units; convert to world so they
        // visually match `pixelScale` regardless of zoom.
        var a = anim;
        a.WaveAmplitude *= invZoom;
        a.Shake *= invZoom;
        Draw(font, text, world.X, world.Y, pixelScale * invZoom, color, a, align, layer, sortY);
    }

    /// <summary>Animated draw with alignment.</summary>
    public static void Draw(Font font, string text, float x, float y, float scale, Color color,
        TextAnim anim, TextAlign align = TextAlign.Left,
        SpriteLayer layer = SpriteLayer.UI, float sortY = 0f)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Alignment: shift origin by measured width.
        if (align != TextAlign.Left)
        {
            float w = font.Measure(text) * scale;
            if (align == TextAlign.Center) x -= w * 0.5f;
            else x -= w;
        }

        float t = anim.Time != 0f ? anim.Time : Time.TotalF;
        float baseRand = MathF.Floor(t * 60f); // shake reseeds at ~60Hz so it doesn't strobe per pixel
        float cursor = 0f; // glyph layout cursor (font-local pixels, scaled per-glyph)

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                cursor = 0f;
                y += font.LineAdvance * scale;
                continue;
            }

            float glyphTime = t + i * anim.Stagger;

            // Lay out in font-local pixels (TryGetQuad advances `cursor`), then scale into
            // world space below. Whitespace returns true with a zero-area quad — skip the
            // push but the cursor advance already happened.
            if (!font.TryGetQuad(c, ref cursor, 0f,
                out float lx0, out float ly0, out float lx1, out float ly1,
                out float u0, out float v0, out float u1, out float v1))
                continue;
            if (lx1 <= lx0 || ly1 <= ly0) continue;

            float gw = (lx1 - lx0) * scale;
            float gh = (ly1 - ly0) * scale;
            float cx = x + (lx0 + (lx1 - lx0) * 0.5f) * scale;
            float cy = y + (ly0 + (ly1 - ly0) * 0.5f) * scale;

            // -- Animation effects --

            // Vertical sine wave (jump).
            if (anim.WaveAmplitude != 0f)
            {
                float speed = anim.WaveSpeed != 0f ? anim.WaveSpeed : 6f;
                cy += MathF.Sin(glyphTime * speed) * anim.WaveAmplitude;
            }

            // Per-frame jitter (shake). Pseudo-random from glyph index + frame slot.
            if (anim.Shake != 0f)
            {
                float r1 = Hash01(i * 7919 + (int)baseRand);
                float r2 = Hash01(i * 6271 + (int)baseRand * 31);
                cx += (r1 - 0.5f) * 2f * anim.Shake;
                cy += (r2 - 0.5f) * 2f * anim.Shake;
            }

            // Scale pulse (pop).
            float s = 1f;
            if (anim.PopAmount != 0f)
            {
                float speed = anim.PopSpeed != 0f ? anim.PopSpeed : 8f;
                s = 1f + MathF.Sin(glyphTime * speed) * anim.PopAmount;
            }
            float halfW = gw * 0.5f * s;
            float halfH = gh * 0.5f * s;

            // Color: optional rainbow multiplier on top of the user's tint.
            Color tint = color;
            if (anim.Rainbow)
            {
                float speed = anim.RainbowSpeed != 0f ? anim.RainbowSpeed : 3f;
                float hue = (glyphTime * speed * 0.159154943f) % 1f; // /(2π)
                if (hue < 0) hue += 1f;
                var rainbow = HsvToRgb(hue, 1f, 1f);
                tint = new Color(
                    (byte)(color.R * rainbow.R / 255),
                    (byte)(color.G * rainbow.G / 255),
                    (byte)(color.B * rainbow.B / 255),
                    color.A);
            }

            SpriteBatcher.DrawTextureUV(font.Material, u0, v0, u1, v1,
                cx - halfW, cy - halfH, halfW * 2f, halfH * 2f, tint, layer, sortY);
        }
    }

    /// <summary>Greedy word-wrap to a max pixel width (unscaled font units * scale).
    /// Splits on spaces, preserves explicit \n. Returns the lines in order.</summary>
    public static IEnumerable<string> Wrap(Font font, string text, float maxWidth, float scale)
    {
        foreach (var paragraph in (text ?? "").Split('\n'))
        {
            var words = paragraph.Split(' ');
            string current = "";
            foreach (var word in words)
            {
                string test = current.Length == 0 ? word : current + " " + word;
                if (font.Measure(test) * scale > maxWidth && current.Length > 0)
                {
                    yield return current;
                    current = word;
                }
                else current = test;
            }
            yield return current;
        }
    }

    /// <summary>Draw text in screen pixels with simple inline tags:
    ///   <c>&lt;color=#rrggbb&gt;…&lt;/color&gt;</c>
    ///   <c>&lt;link=id&gt;…&lt;/link&gt;</c>
    /// Nesting is supported via a color stack. Link tags don't render
    /// any decoration on their own — pair with a color tag for visual
    /// affordance. Other tags are passed through verbatim.</summary>
    public static void DrawScreenRich(Font font, string text, float screenX, float screenY,
        float pixelScale, Color baseColor,
        Camera camera, int viewportW, int viewportH)
    {
        DrawScreenRich(font, text, screenX, screenY, pixelScale, baseColor, camera,
            viewportW, viewportH, links: null);
    }

    /// <summary>Same as <see cref="DrawScreenRich"/> but also collects
    /// per-link bounding rects in SCREEN space into
    /// <paramref name="links"/>. A link spanning multiple lines (after
    /// a hard line break) produces one rect per line. Caller passes
    /// an empty list; the list is cleared before population. Used by
    /// <see cref="BdvEngine.Gui.Label"/>'s hit-test to dispatch
    /// click handlers to <c>&lt;link=id&gt;</c> spans.</summary>
    public static void DrawScreenRich(Font font, string text, float screenX, float screenY,
        float pixelScale, Color baseColor,
        Camera camera, int viewportW, int viewportH,
        List<RichLinkSpan>? links)
    {
        if (string.IsNullOrEmpty(text)) return;
        links?.Clear();
        float invZoom = 1f / camera.Zoom;
        var world = camera.ScreenToWorld(screenX, screenY, viewportW, viewportH);
        DrawWorldRich(font, text, world.X, world.Y, pixelScale * invZoom, baseColor,
            // Inline screen-space tracker — converts the world-space
            // cursor advances inside DrawWorldRich back to screen
            // pixels so the collected rects match the GUI hit-test
            // coord system without needing camera conversion later.
            links == null ? null : new ScreenLinkTracker(screenX, screenY, pixelScale, font, links));
    }

    /// <summary>One contiguous on-screen rectangle for a
    /// <c>&lt;link=...&gt;</c> span. ID matches the tag's id; X/Y/W/H
    /// are screen-space pixels. A multi-line link produces one
    /// RichLinkSpan per line.</summary>
    public readonly struct RichLinkSpan
    {
        public readonly string Id;
        public readonly float X, Y, W, H;
        public RichLinkSpan(string id, float x, float y, float w, float h)
        { Id = id; X = x; Y = y; W = w; H = h; }
        public bool Contains(float sx, float sy) => sx >= X && sx <= X + W && sy >= Y && sy <= Y + H;
    }

    /// <summary>Helper passed into the world-space renderer that
    /// translates per-glyph cursor advances into screen-space rects
    /// for any active link span. Tracks lineY + cursor in screen
    /// units (= pixelScale * world-unit) so the result is directly
    /// usable for GUI hit-test.</summary>
    internal sealed class ScreenLinkTracker
    {
        public readonly float OriginX, OriginY;
        public readonly float Scale;          // pixels per font-unit
        public readonly Font Font;
        public readonly List<RichLinkSpan> Out;
        public string? CurrentLinkId;
        public float SpanStartCursor;         // cursor (font-units) where current span began on this line
        public ScreenLinkTracker(float ox, float oy, float scale, Font font, List<RichLinkSpan> outList)
        { OriginX = ox; OriginY = oy; Scale = scale; Font = font; Out = outList; }

        public void OpenLink(string id, float cursorFontUnits)
        {
            CurrentLinkId = id;
            SpanStartCursor = cursorFontUnits;
        }
        public void CloseLink(float lineYFontUnits, float cursorFontUnits)
        {
            if (CurrentLinkId == null) return;
            EmitRect(lineYFontUnits, cursorFontUnits);
            CurrentLinkId = null;
        }
        public void OnNewline(float lineYFontUnits, float cursorFontUnits)
        {
            if (CurrentLinkId == null) return;
            EmitRect(lineYFontUnits, cursorFontUnits);
            // Span continues on next line — restart at the line's
            // leading edge (cursor reset to 0 happens in the renderer
            // after this call).
            SpanStartCursor = 0f;
        }
        private void EmitRect(float lineYFontUnits, float cursorFontUnits)
        {
            float x0 = OriginX + SpanStartCursor * Scale;
            float y0 = OriginY + lineYFontUnits * Scale;
            float w  = (cursorFontUnits - SpanStartCursor) * Scale;
            float h  = Font.LineAdvance * Scale;
            // OriginY in DrawScreenRich is the baseline; shift up so
            // the rect sits over the visible glyphs, not below.
            y0 -= Font.Ascent * Scale;
            if (w > 0 && h > 0)
                Out.Add(new RichLinkSpan(CurrentLinkId!, x0, y0, w, h));
        }
    }

    private static void DrawWorldRich(Font font, string text, float x, float y, float scale, Color baseColor)
        => DrawWorldRich(font, text, x, y, scale, baseColor, links: null);

    private static void DrawWorldRich(Font font, string text, float x, float y, float scale, Color baseColor,
        ScreenLinkTracker? links)
    {
        var stack = new Stack<Color>();
        stack.Push(baseColor);
        float cursor = 0f;
        float lineY = y; // current baseline; advances on '\n'
        // lineYFontUnits / cursorFontUnits are in FONT units (cursor
        // is already font-units, and we use (lineY - y)/scale to
        // recover font units relative to baseline). The tracker uses
        // these to produce screen-space rects.
        float baselineFontY = 0f; // 0 at first line; +LineAdvance per newline
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                // Hard line break — finalize any open link span on
                // this line + reset horizontal cursor + drop baseline
                // by one font line.
                links?.OnNewline(baselineFontY, cursor);
                cursor = 0f;
                lineY += font.LineAdvance * scale;
                baselineFontY += font.LineAdvance;
                i++;
                continue;
            }
            if (c == '<')
            {
                int end = text.IndexOf('>', i + 1);
                if (end > i)
                {
                    string tag = text.Substring(i + 1, end - i - 1);
                    if (tag.StartsWith("color=#") && tag.Length >= 13)
                    {
                        if (TryParseHex(tag.AsSpan(7), out var col))
                        {
                            stack.Push(new Color(col.R, col.G, col.B, baseColor.A));
                            i = end + 1; continue;
                        }
                    }
                    else if (tag == "/color")
                    {
                        if (stack.Count > 1) stack.Pop();
                        i = end + 1; continue;
                    }
                    else if (tag.StartsWith("link="))
                    {
                        string id = tag.Substring(5);
                        links?.OpenLink(id, cursor);
                        i = end + 1; continue;
                    }
                    else if (tag == "/link")
                    {
                        links?.CloseLink(baselineFontY, cursor);
                        i = end + 1; continue;
                    }
                }
            }
            if (!font.TryGetQuad(c, ref cursor, 0f,
                out float lx0, out float ly0, out float lx1, out float ly1,
                out float u0, out float v0, out float u1, out float v1))
            { i++; continue; }
            if (lx1 > lx0 && ly1 > ly0)
            {
                float gw = (lx1 - lx0) * scale, gh = (ly1 - ly0) * scale;
                float cx = x + (lx0 + (lx1 - lx0) * 0.5f) * scale;
                float cy = lineY + (ly0 + (ly1 - ly0) * 0.5f) * scale;
                SpriteBatcher.DrawTextureUV(font.Material, u0, v0, u1, v1,
                    cx - gw * 0.5f, cy - gh * 0.5f, gw, gh, stack.Peek(), SpriteLayer.UI);
            }
            i++;
        }
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, out (byte R, byte G, byte B) c)
    {
        c = default;
        if (hex.Length < 6) return false;
        if (!byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)) return false;
        if (!byte.TryParse(hex.Slice(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)) return false;
        if (!byte.TryParse(hex.Slice(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;
        c = (r, g, b); return true;
    }

    private static float Hash01(int x)
    {
        // xorshift on a per-call basis; deterministic for same x.
        uint v = (uint)x * 2654435761u;
        v ^= v >> 16;
        v *= 0x7feb352dU;
        v ^= v >> 15;
        v *= 0x846ca68bU;
        v ^= v >> 16;
        return (v & 0xFFFFFF) / 16777216f;
    }

    private static (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
    {
        float r, g, b;
        int i = (int)MathF.Floor(h * 6f);
        float f = h * 6f - i;
        float p = v * (1f - s);
        float q = v * (1f - f * s);
        float t = v * (1f - (1f - f) * s);
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
