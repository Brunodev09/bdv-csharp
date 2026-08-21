namespace BdvEngine.Prefabs;

/// <summary>Placement callback the game supplies to
/// <see cref="PrefabSpawner"/>. Called once per non-empty cell with
/// the world tile the cell lands on. Return true if the placement
/// succeeded (tile written, prop spawned); false to record a skip
/// in the <see cref="SpawnReport"/> — the spawner does NOT abort on
/// a skip so partial spawns still finish (e.g. a house whose bed
/// tile is blocked still places its floor + walls).</summary>
public delegate bool PrefabCellPlacer(int worldCol, int worldRow, PrefabCell cell);

/// <summary>What the spawner did. The game uses this to log, refund
/// costs on partial failure, undo, etc.</summary>
public readonly struct SpawnReport
{
    public readonly int Placed;
    public readonly int Skipped;
    public SpawnReport(int placed, int skipped) { Placed = placed; Skipped = skipped; }
}

/// <summary>Rotation applied to the grid at spawn time (in 90°
/// increments). Simpler than authoring four PNGs per prefab.</summary>
public enum PrefabRotation { R0, R90, R180, R270 }

/// <summary>
/// Places a <see cref="Prefab"/> in world coordinates via a
/// per-project placement callback. Zero opinion on what a
/// "wall" is — the callback owns the actual tile / prop write,
/// which lets ColonySim place its own <c>BuiltStructure</c> objects
/// while WorldSim / a different game supplies its own translation.
///
/// Does NOT deduct materials, spawn built designations, or check
/// permissions — those are game-side policies. The spawner's job is
/// coordinate arithmetic (anchor offset, rotation) and iterating
/// cells in a deterministic order.
/// </summary>
public static class PrefabSpawner
{
    /// <summary>Order: floors → walls → props. Ensures a prop cell
    /// that expects a floor beneath sees the floor already placed
    /// even when the callback tracks layering.</summary>
    private static readonly PrefabKind[] Order = { PrefabKind.Floor, PrefabKind.Wall, PrefabKind.Prop };

    public static SpawnReport Spawn(Prefab p, int col, int row, PrefabCellPlacer placer,
                                    PrefabRotation rotation = PrefabRotation.R0)
    {
        int placed = 0, skipped = 0;
        int w = p.Width, h = p.Height;
        int ax = p.Anchor.Col, ay = p.Anchor.Row;

        foreach (var pass in Order)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var cell = p.Grid[x, y];
                if (cell.Kind != pass) continue;

                // Rotate the (x,y) around the anchor before offsetting
                // into world space.
                var (rx, ry) = Rotate(x - ax, y - ay, rotation);
                int wCol = col + rx;
                int wRow = row + ry;
                if (placer(wCol, wRow, cell)) placed++;
                else                          skipped++;
            }
        }
        return new SpawnReport(placed, skipped);
    }

    private static (int, int) Rotate(int dx, int dy, PrefabRotation rot)
    {
        return rot switch
        {
            PrefabRotation.R90  => (-dy,  dx),
            PrefabRotation.R180 => (-dx, -dy),
            PrefabRotation.R270 => ( dy, -dx),
            _                    => ( dx,  dy),
        };
    }
}
