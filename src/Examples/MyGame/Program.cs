using BdvEngine;
using MyGameApp;

var engine = new Engine(new MyGame(), new EngineConfig { Title = "BdvEngine — MyGame", ShowStats = true });
engine.Run();
