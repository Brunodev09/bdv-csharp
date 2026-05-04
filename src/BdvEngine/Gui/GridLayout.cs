namespace BdvEngine.Gui;

/// <summary>
/// Container that lays its children out in a grid each frame. Cell size is fixed
/// (every child is forced to <see cref="CellWidth"/>×<see cref="CellHeight"/>);
/// row count is auto-computed from <see cref="Cols"/> and the visible child count.
///
/// Use case: tile palette in the spritesheet editor, inventory grids, hex preview
/// sheets — anywhere you'd otherwise hand-position N identically-sized cells.
/// </summary>
public sealed class GridLayout : Panel
{
    public int Cols = 1;
    public float CellWidth  = 64f;
    public float CellHeight = 64f;
    public float SpacingX = 4f;
    public float SpacingY = 4f;
    public Padding Padding = new(8f);

    public GridLayout(float x, float y, float w, float h) : base(x, y, w, h) { }

    public GridLayout WithCols(int cols)                   { Cols = Math.Max(1, cols); return this; }
    public GridLayout WithCellSize(float cw, float ch)     { CellWidth = cw; CellHeight = ch; return this; }
    public GridLayout WithSpacing(float sx, float sy)      { SpacingX = sx; SpacingY = sy; return this; }
    public GridLayout WithSpacing(float uniform)           { SpacingX = SpacingY = uniform; return this; }
    public GridLayout WithPadding(Padding pad)             { Padding = pad; return this; }
    public GridLayout WithPadding(float all)               { Padding = new(all); return this; }

    public override void Render(Context ctx)
    {
        if (!Visible) return;
        DoLayout();
        base.Render(ctx);
    }

    private void DoLayout()
    {
        int visibleIdx = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!c.Visible) continue;
            int row = visibleIdx / Cols;
            int col = visibleIdx % Cols;
            c.AnchorMin = c.AnchorMax = c.Pivot = System.Numerics.Vector2.Zero;
            c.X = Padding.Left + col * (CellWidth  + SpacingX);
            c.Y = Padding.Top  + row * (CellHeight + SpacingY);
            c.Width  = CellWidth;
            c.Height = CellHeight;
            visibleIdx++;
        }
    }
}
