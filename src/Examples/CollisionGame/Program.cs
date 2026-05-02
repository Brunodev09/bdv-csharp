using BdvEngine;
using CollisionGameApp;

var engine = new Engine(new CollisionGame(), new EngineConfig { Title = "BdvEngine — Collision", ShowStats = true });
engine.Run();
