using System.Text.Json;

namespace BdvEngine;

public interface IBehaviorData
{
    string Name { get; set; }
    void SetFromJson(JsonElement json);
}

public interface IBehavior
{
    string Name { get; }
    void SetOwner(SimObject owner);
    void Update(double deltaTime);
    void Render(Shader shader);
    void Apply(object? userData);
}

public interface IBehaviorBuilder
{
    string Type { get; }
    IBehavior BuildFromJson(JsonElement json);
}

public abstract class BaseBehavior : IBehavior
{
    protected readonly IBehaviorData _data;
    protected SimObject _owner = null!;

    public string Name { get; }

    protected BaseBehavior(IBehaviorData data)
    {
        _data = data;
        Name = data.Name;
    }

    public virtual void SetOwner(SimObject owner) => _owner = owner;
    public virtual void Update(double deltaTime) { }
    public virtual void Render(Shader shader) { }
    public virtual void Apply(object? userData) { }
}

public static class BehaviorManager
{
    private static readonly Dictionary<string, IBehaviorBuilder> _builders = new();

    public static void RegisterBuilder(IBehaviorBuilder builder)
        => _builders[builder.Type] = builder;

    public static IBehavior ExtractBehavior(JsonElement json)
    {
        if (!json.TryGetProperty("type", out var typeProp))
            throw new InvalidOperationException("BehaviorManager: behavior is missing 'type'.");
        string type = typeProp.GetString()!;
        if (!_builders.TryGetValue(type, out var builder))
            throw new InvalidOperationException($"BehaviorManager: no builder registered for type '{type}'.");
        return builder.BuildFromJson(json);
    }
}
