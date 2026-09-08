using System.Text.Json;

namespace BdvEngine;

public sealed class AnimatedSpriteComponentData : IComponentData
{
    public string Name { get; set; } = string.Empty;
    public string MaterialName = string.Empty;
    public int FrameWidth;
    public int FrameHeight;
    public int FrameCount = 1;
    public int[] FrameSequence = Array.Empty<int>();
    public float Width = 100, Height = 100;

    public void SetFromJson(JsonElement json)
    {
        if (json.TryGetProperty("name", out var n)) Name = n.GetString() ?? "";
        if (json.TryGetProperty("materialName", out var m)) MaterialName = m.GetString() ?? "";
        if (json.TryGetProperty("frameWidth", out var fw)) FrameWidth = fw.GetInt32();
        if (json.TryGetProperty("frameHeight", out var fh)) FrameHeight = fh.GetInt32();
        if (json.TryGetProperty("frameCount", out var fc)) FrameCount = fc.GetInt32();
        if (json.TryGetProperty("frameSequence", out var fs))
        {
            var list = new List<int>();
            foreach (var x in fs.EnumerateArray()) list.Add(x.GetInt32());
            FrameSequence = list.ToArray();
        }
        if (json.TryGetProperty("width", out var w)) Width = w.GetSingle();
        if (json.TryGetProperty("height", out var h)) Height = h.GetSingle();
    }
}

public sealed class AnimatedSpriteComponentBuilder : IComponentBuilder
{
    public System.Type ComponentType => typeof(AnimatedSpriteComponent);

    public string Type => "animatedSprite";

    public IComponent BuildFromJson(JsonElement json)
    {
        var data = new AnimatedSpriteComponentData();
        data.SetFromJson(json);
        return new AnimatedSpriteComponent(data);
    }
}

public sealed class AnimatedSpriteComponent : BaseComponent
{
    public AnimatedSprite Sprite { get; }

    public AnimatedSpriteComponent(AnimatedSpriteComponentData data) : base(data)
    {
        // Match TS: when width/height aren't explicitly set, use frame dimensions.
        float w = data.Width  != 100 ? data.Width  : data.FrameWidth;
        float h = data.Height != 100 ? data.Height : data.FrameHeight;
        Sprite = new AnimatedSprite(
            data.Name, data.MaterialName, w, h,
            data.FrameWidth, data.FrameHeight,
            data.FrameCount, data.FrameSequence);
    }

    public override void Load() => Sprite.Load();
    public override void Update(double deltaTime) => Sprite.Update(deltaTime);
    public override void Render(Shader shader)
    {
        Sprite.Render(shader, _owner.WorldMatrix);
        base.Render(shader);
    }
}
