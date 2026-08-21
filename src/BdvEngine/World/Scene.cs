namespace BdvEngine;

public sealed class Scene
{
    public SimObject Root { get; }
    public bool IsLoaded => Root.IsLoaded;

    public Scene()
    {
        Root = new SimObject(0, "__root__", this);
    }

    public void AddObject(SimObject obj) => Root.AddChild(obj);
    public void RemoveObject(SimObject obj) => Root.RemoveChild(obj);
    public SimObject? GetObjectByName(string name) => Root.GetObjectByName(name);
    public void Load() => Root.Load();
    public void Update(double deltaTime) => Root.Update(deltaTime);
    public void Render(Shader shader) => Root.Render(shader);
    public void RebakeMatrices() => Root.RebakeMatrices();
}
