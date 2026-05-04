using BdvEngine;
using SpritesheetEditorApp;

var engine = new Engine(new SpritesheetEditor(), new EngineConfig
{
    Title = "BDV Spritesheet Editor",
    ShowStats = false,
});
engine.Run();
