namespace BdvEngine.Gui;

/// <summary>Which edge of the viewport the sidebar pins to.</summary>
public enum SidebarSide { Left, Right }

/// <summary>
/// Full-viewport-height anchored panel that slides in from the left or right
/// edge. Add <see cref="SidebarSection"/> instances (or any other Element) via
/// <see cref="Add"/> — they stack vertically in the content area. Use
/// <see cref="Open"/>/<see cref="Close"/>/<see cref="Toggle"/> to drive the
/// slide animation.
///
/// Built on top of <see cref="Anchor.StretchLeft"/> / <see cref="Anchor.StretchRight"/>:
/// the panel keeps its fixed <see cref="SidebarWidth"/> on the X axis and
/// auto-stretches to fill the viewport on the Y axis. The sliding happens by
/// animating X off-screen (toward -Width on the left, +Width on the right).
/// </summary>
public sealed class Sidebar : Panel
{
    public SidebarSide Side { get; }
    public float SidebarWidth { get; }
    public bool IsOpen { get; private set; }
    /// <summary>Lerp factor per frame (0..1). 0.18 ≈ ~10 frames to glide in.</summary>
    public float SlideSpeed { get; set; } = 0.20f;

    private float _slideX;
    private float _slideTarget;

    /// <summary>The vertical layout that holds the sidebar's children. Don't
    /// add to the Sidebar directly — call <see cref="Add"/> to put items here.</summary>
    public VerticalLayout Content { get; }

    /// <summary>The tab button on the sidebar's outer edge that toggles open/
    /// closed. Always reachable since it slides with the sidebar. Customize
    /// via the public Button API (label, colors, font).</summary>
    public Button ToggleTab { get; }

    /// <summary>The explicit ✕ close button in the sidebar's top-right.
    /// Always visible while the sidebar is open; calls <see cref="Close"/>.</summary>
    public Button CloseButton { get; }

    public Sidebar(SidebarSide side, float width = 320f) : base(0, 0, width, 0)
    {
        Side = side;
        SidebarWidth = width;
        AnchorTo(side == SidebarSide.Left ? Anchor.StretchLeft : Anchor.StretchRight);
        Width = width;

        WithBackground(new Color(18, 22, 32, 255))
            .WithBorder(new Color(95, 115, 160, 255), 2f);

        // ── Toggle tab ─────────────────────────────────────────────────────
        // A small button that sticks out beyond the sidebar's outer edge so
        // it's always reachable: visible at the screen edge when the sidebar
        // is closed, attached to the sidebar's open-edge when it's open.
        // Solves the overlap problem where the original toggle button —
        // outside the sidebar in the HUD — gets covered as the sidebar
        // slides in. As a child of the sidebar, the tab slides with it.
        const float TAB_W = 24f, TAB_H = 90f;
        // Tab's local X within the sidebar:
        //  Left sidebar  → tab at the right edge (X = width). When sidebar
        //                  is at X=-width (closed), tab's screen X = 0,
        //                  poking out at the left screen edge.
        //  Right sidebar → tab at the left edge (X = -TAB_W).
        float tabLocalX = side == SidebarSide.Left ? width : -TAB_W;
        ToggleTab = new Button(tabLocalX, 0, TAB_W, TAB_H, side == SidebarSide.Left ? "▶" : "◀")
            .WithColors(
                new Color(40,  48,  64, 235),
                new Color(80,  95, 130, 245),
                new Color(110, 130, 180, 250))
            .WithTextColor(new Color(220, 225, 240, 255))
            .OnClick(Toggle);
        // Anchor the tab vertically centered on the sidebar so it lands at
        // a comfortable resting height regardless of viewport size.
        ToggleTab.AnchorMin = new System.Numerics.Vector2(0, 0.5f);
        ToggleTab.AnchorMax = new System.Numerics.Vector2(0, 0.5f);
        ToggleTab.Pivot     = new System.Numerics.Vector2(0, 0.5f);
        base.Add(ToggleTab);

        // Content area — stacks children top-to-bottom. Top padding leaves
        // room for the ✕ so the first section doesn't sit under it.
        const float X_SZ = 30f, X_MARGIN = 8f;
        Content = new VerticalLayout(0, 0, width, 800)
            .WithSpacing(4f)
            .WithPadding(new Padding(8, X_SZ + X_MARGIN + 6, 8, 8));
        base.Add(Content);

        // Explicit ✕ close button, top-right. Added AFTER Content so it's the
        // topmost child — hit-testing walks children in reverse, so Content
        // (full-width) must NOT sit on top of the X or clicks never reach it
        // (that was the "X doesn't close" bug).
        CloseButton = new Button(width - X_SZ - X_MARGIN, X_MARGIN, X_SZ, X_SZ, "X")
            .WithColors(
                new Color(120, 40, 48, 240),
                new Color(170, 60, 68, 250),
                new Color(210, 80, 88, 255))
            .WithTextColor(new Color(245, 230, 232, 255))
            .OnClick(Close);
        base.Add(CloseButton);

        // Start closed (off-screen) — caller invokes Open() to reveal.
        Close();
        _slideX = _slideTarget;
        X = _slideX;
    }

    /// <summary>Add a child element (typically a SidebarSection) to the
    /// stacked content area. Hides the base Panel.Add — calling Sidebar.Add(..)
    /// always routes into Content.</summary>
    public new T Add<T>(T child) where T : Element => Content.Add(child);

    public void Open()
    {
        IsOpen = true;
        _slideTarget = 0f;
        // Tab arrow flips: when open, points "back into" the sidebar's
        // outer edge to read as "click me to close".
        if (ToggleTab != null) ToggleTab.Label = Side == SidebarSide.Left ? "◀" : "▶";
    }

    public void Close()
    {
        IsOpen = false;
        _slideTarget = Side == SidebarSide.Left ? -SidebarWidth : SidebarWidth;
        if (ToggleTab != null) ToggleTab.Label = Side == SidebarSide.Left ? "▶" : "◀";
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public override void Update(Context ctx)
    {
        // Animate slide. Snap when within half a pixel of the target so the
        // fully-open/closed state is exact (no perpetual sub-pixel jitter).
        _slideX += (_slideTarget - _slideX) * SlideSpeed;
        if (MathF.Abs(_slideX - _slideTarget) < 0.5f) _slideX = _slideTarget;
        X = _slideX;
        // Match content height to the viewport so multi-section sidebars
        // render correctly even if the window resizes.
        Content.Height = ctx.ViewportH / MathF.Max(0.01f, ctx.UIScale);
        base.Update(ctx);
    }
}
