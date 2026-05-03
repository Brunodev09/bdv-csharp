namespace BdvEngine.Gui;

/// <summary>
/// Label whose text is recomputed every frame from a callback. Drop-in replacement
/// for the old ImGui-based "TextLive" widget when you have values that change each
/// tick (FPS, player position, current selection, etc.).
/// </summary>
public sealed class LiveLabel : Label
{
    public Func<string> Provider;

    public LiveLabel(float x, float y, Func<string> provider) : base(x, y, "")
    {
        Provider = provider;
    }

    public override void Update(Context ctx)
    {
        Text = Provider();
        base.Update(ctx);
    }
}
