using System.Numerics;

namespace BdvEngine;

public sealed class SimObject
{
    private readonly List<SimObject> _children = new();
    private readonly List<IComponent> _components = new();
    private readonly List<IBehavior> _behaviors = new();
    private SimObject? _parent;
    private Scene? _scene;

    private Matrix4x4 _localMatrix = Matrix4x4.Identity;
    private Matrix4x4 _worldMatrix = Matrix4x4.Identity;

    public int Id { get; }
    public string Name { get; set; }

    /// <summary>Asset this object's subtree was imported from (e.g. <c>"assets/hero.glb"</c>), set
    /// by <see cref="World.Load"/>. <see cref="SceneSerializer"/> writes this out as the node's
    /// <c>"model"</c> and re-imports on load instead of serialising the generated children — so a
    /// scene file references models rather than inlining their geometry.</summary>
    public string? Source { get; set; }

    public Transform Transform { get; } = new();
    public bool IsLoaded { get; private set; }
    public SimObject? Parent => _parent;
    public IReadOnlyList<SimObject> Children => _children;
    /// <summary>This object's components (read by the unified renderer to collect mesh draws).</summary>
    public IReadOnlyList<IComponent> Components => _components;
    /// <summary>This object's behaviors — read by <see cref="SceneSerializer"/> and the inspector.</summary>
    public IReadOnlyList<IBehavior> Behaviors => _behaviors;
    public Matrix4x4 LocalMatrix => _localMatrix;
    public Matrix4x4 WorldMatrix => _worldMatrix;

    public SimObject(int id, string name, Scene? scene = null)
    {
        Id = id;
        Name = name;
        _scene = scene;
    }

    internal void OnAdded(Scene? scene) => _scene = scene;

    public void AddChild(SimObject child)
    {
        child._parent = this;
        _children.Add(child);
        child.OnAdded(_scene);
    }

    public void RemoveChild(SimObject child)
    {
        if (_children.Remove(child)) child._parent = null;
    }

    public SimObject? GetObjectByName(string name)
    {
        if (Name == name) return this;
        foreach (var c in _children)
        {
            var r = c.GetObjectByName(name);
            if (r != null) return r;
        }
        return null;
    }

    public IComponent? GetComponent(string name) => _components.Find(c => c.Name == name);
    public IBehavior? GetBehavior(string name) => _behaviors.Find(b => b.Name == name);
    public T? GetComponent<T>() where T : class, IComponent => _components.OfType<T>().FirstOrDefault();
    public T? GetBehavior<T>() where T : class, IBehavior => _behaviors.OfType<T>().FirstOrDefault();

    public void AddComponent(IComponent component)
    {
        _components.Add(component);
        component.SetOwner(this);
    }

    public void AddBehavior(IBehavior behavior)
    {
        _behaviors.Add(behavior);
        behavior.SetOwner(this);
    }

    public void Load()
    {
        IsLoaded = true;
        foreach (var c in _components) c.Load();
        foreach (var ch in _children) ch.Load();
    }

    public void Update(double deltaTime)
    {
        _localMatrix = Transform.GetMatrix();
        _worldMatrix = _parent != null ? _localMatrix * _parent._worldMatrix : _localMatrix;

        foreach (var c in _components) c.Update(deltaTime);
        foreach (var b in _behaviors) b.Update(deltaTime);
        foreach (var ch in _children) ch.Update(deltaTime);
    }

    public void Render(Shader shader)
    {
        foreach (var c in _components) c.Render(shader);
        foreach (var b in _behaviors) b.Render(shader);
        foreach (var ch in _children) ch.Render(shader);
    }

    /// <summary>Recompute local + world matrices for this whole subtree WITHOUT running
    /// component/behavior updates. The unified engine calls this once right before rendering, so
    /// a transform mutated during Update (game logic OR a behavior) shows up the SAME frame —
    /// fixes the one-frame-lag the manual 3D path had (matrices were only baked at the top of
    /// <see cref="Update"/>, before behaviors ran).</summary>
    public void RebakeMatrices()
    {
        _localMatrix = Transform.GetMatrix();
        _worldMatrix = _parent != null ? _localMatrix * _parent._worldMatrix : _localMatrix;
        foreach (var ch in _children) ch.RebakeMatrices();
    }
}
