namespace BdvEngine;

public abstract class Game
{
    public Camera2D Camera { get; internal set; } = null!;
    public int ViewportWidth { get; internal set; }
    public int ViewportHeight { get; internal set; }

    public abstract void Init();
    public abstract void Update(double deltaTime);
    public abstract void Render(Shader shader);
}
