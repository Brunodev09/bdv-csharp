using BdvEngine;
using HexStrategyGameApp;

var engine = new Engine(new HexStrategyGame(), new EngineConfig { Title = "Bdv Hex Strategy", ShowStats = true });
engine.Run();
