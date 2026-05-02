using BdvEngine;
using TerrainGameApp;

var engine = new Engine(new TerrainGame(), new EngineConfig { Title = "Bdv World", ShowStats = true });
engine.Run();
