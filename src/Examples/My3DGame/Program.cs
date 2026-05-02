using BdvEngine;
using My3DGameApp;

var engine = new Engine3D(new My3DGame(), new EngineConfig { Title = "BdvEngine 3D", ShowStats = true });
engine.Run();
