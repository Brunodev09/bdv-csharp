namespace BdvEngine.Gui;

/// <summary>
/// Per-element attachable behavior — the Gui counterpart to <see cref="IBehavior"/>
/// for SimObjects. Lives in <see cref="Element.Behaviors"/>; called from
/// <see cref="Element.Update"/> / <see cref="Element.Render"/> with the owning
/// element so a single behavior instance can read/write multiple properties
/// (Width/Height, RenderScale, color tints on subclasses, etc.).
///
/// Behaviors run *before* the element's own Update logic and *before* its Render,
/// so they can mutate state (e.g., RenderScale) that the render pass then reads.
/// </summary>
public interface IElementBehavior
{
    void Update(Context ctx, Element owner) { }
    void Render(Context ctx, Element owner) { }
}
