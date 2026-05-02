using System.Text.Json;

namespace BdvEngine;

public sealed class SpriteComponentData : IComponentData
{
    public string Name { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;

    public void SetFromJson(JsonElement json)
    {
        if (json.TryGetProperty("name", out var n)) Name = n.GetString() ?? "";
        if (json.TryGetProperty("materialName", out var m)) MaterialName = m.GetString() ?? "";
    }
}

public sealed class SpriteComponentBuilder : IComponentBuilder
{
    public string Type => "sprite";

    public IComponent BuildFromJson(JsonElement json)
    {
        var data = new SpriteComponentData();
        data.SetFromJson(json);
        return new SpriteComponent(data);
    }
}

public sealed class SpriteComponent : BaseComponent
{
    private readonly Sprite _sprite;

    public Sprite Sprite => _sprite;

    public SpriteComponent(SpriteComponentData data) : base(data)
    {
        _sprite = new Sprite(data.Name, data.MaterialName);
    }

    public override void Load() => _sprite.Load();

    public override void Render(Shader shader)
    {
        _sprite.Render(shader, _owner.WorldMatrix);
        base.Render(shader);
    }
}
