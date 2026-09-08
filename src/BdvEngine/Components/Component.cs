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

    /// <summary>The concrete component class this builder produces, so <see cref="SceneSerializer"/>
    /// can map an instance back to its <see cref="Type"/> discriminator when saving a scene.
    /// Defaults to null (not serialisable) so third-party builders keep compiling; every built-in
    /// builder overrides it.</summary>
    System.Type? ComponentType => null;
}

public abstract class BaseComponent : IComponent
{
    protected readonly IComponentData _data;
    protected SimObject _owner = null!;

    public string Name { get; }
    public SimObject Owner => _owner;

    /// <summary>The construction-parameter bag this component was built from. Some components copy
    /// values out of it into live fields (and the inspector edits those); others keep state only
    /// here. <see cref="SceneSerializer"/> writes both, live fields winning, so either style
    /// round-trips.</summary>
    public IComponentData Data => _data;

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
    private static readonly Dictionary<System.Type, string> _typeNames = new();

    public static void RegisterBuilder(IComponentBuilder builder)
    {
        _builders[builder.Type] = builder;
        if (builder.ComponentType != null) _typeNames[builder.ComponentType] = builder.Type;
    }

    /// <summary>Reverse of <see cref="ExtractComponent"/>'s <c>"type"</c> lookup: given a live
    /// component, the discriminator to write it back out under. False for components with no
    /// registered builder (e.g. MeshComponent, which the scene serialiser handles natively).</summary>
    public static bool TryGetTypeName(IComponent component, out string type)
        => _typeNames.TryGetValue(component.GetType(), out type!);

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
