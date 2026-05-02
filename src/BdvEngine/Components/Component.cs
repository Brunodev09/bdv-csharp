using System.Text.Json;

namespace BdvEngine;

public interface IComponentData
{
    string Name { get; set; }
    void SetFromJson(JsonElement json);
}

public interface IComponent
{
    string Name { get; }
    SimObject Owner { get; }
    void SetOwner(SimObject owner);
    void Load();
    void Unload();
    void Update(double deltaTime);
    void Render(Shader shader);
}

public interface IComponentBuilder
{
    string Type { get; }
    IComponent BuildFromJson(JsonElement json);
}

public abstract class BaseComponent : IComponent
{
    protected readonly IComponentData _data;
    protected SimObject _owner = null!;

    public string Name { get; }
    public SimObject Owner => _owner;

    protected BaseComponent(IComponentData data)
    {
        _data = data;
        Name = data.Name;
    }

    public virtual void SetOwner(SimObject owner) => _owner = owner;
    public virtual void Load() { }
    public virtual void Unload() { }
    public virtual void Update(double deltaTime) { }
    public virtual void Render(Shader shader) { }
}

public static class ComponentManager
{
    private static readonly Dictionary<string, IComponentBuilder> _builders = new();

    public static void RegisterBuilder(IComponentBuilder builder)
        => _builders[builder.Type] = builder;

    public static IComponent ExtractComponent(JsonElement json)
    {
        if (!json.TryGetProperty("type", out var typeProp))
            throw new InvalidOperationException("ComponentManager: component is missing 'type'.");
        string type = typeProp.GetString()!;
        if (!_builders.TryGetValue(type, out var builder))
            throw new InvalidOperationException($"ComponentManager: no builder registered for type '{type}'.");
        return builder.BuildFromJson(json);
    }
}
