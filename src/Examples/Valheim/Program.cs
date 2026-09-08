using BdvEngine;
using ValheimLike;

// Migrated to the unified engine. Fully qualified only during the transition, while the old
// BdvEngine.Engine still exists.
var engine = new Engine(new ValheimGame(), new EngineConfig
{
    Title = "BdvEngine — Valheim-like (vertical slice)",
    Size = new Silk.NET.Maths.Vector2D<int>(1280, 720),
    ShowStats = true,
});
engine.Run();
