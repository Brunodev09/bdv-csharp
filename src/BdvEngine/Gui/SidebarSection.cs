namespace BdvEngine.Gui;

/// <summary>
/// One titled section inside a <see cref="Sidebar"/>. Has a header strip with
/// the section title and a vertical-layout body where icons / labels / buttons /
/// scroll views go. Add children via <see cref="Add"/> — they're inserted into
/// <see cref="Body"/> automatically.
///
/// Sections are separate Panels so each gets its own background + border, and
/// so they can be hidden / re-ordered independently inside the parent sidebar.
/// </summary>
public sealed class SidebarSection : Panel
{
    public const float TITLE_HEIGHT = 28f;

    public string Title { get; }
    public VerticalLayout Body { get; }

    public SidebarSection(string title, float width, float bodyHeight = 220f)
        : base(0, 0, width, TITLE_HEIGHT + bodyHeight)
    {
        Title = title;
        WithBackground(new Color(28, 34, 50, 255))
            .WithBorder(new Color(70, 85, 120, 200), 1f);

        // Header strip — bold accent color, left-aligned.
        var titleLabel = new Label(10, 6, title)
            .WithScale(0.32f)
            .WithColor(new Color(255, 220, 130, 255));
        base.Add(titleLabel);

        // Body area — vertical-layout for the section's content.
        Body = new VerticalLayout(0, TITLE_HEIGHT, width, bodyHeight)
            .WithSpacing(4f)
            .WithPadding(new Padding(8, 6, 8, 6));
        base.Add(Body);
    }

    /// <summary>Add a child to the section's body.</summary>
    public new T Add<T>(T child) where T : Element => Body.Add(child);

    /// <summary>Resize the body — useful after rebuilding contents at runtime.</summary>
    public SidebarSection WithBodyHeight(float h)
    {
        Body.Height = h;
        Height = TITLE_HEIGHT + h;
        return this;
    }
}
