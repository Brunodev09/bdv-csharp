using BdvEngine;
using StressGameApp;

var engine = new Engine(new StressGame(), new EngineConfig { Title = "BdvEngine — Stress", ShowStats = true });
engine.Run();
