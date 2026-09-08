using System.Numerics;

namespace BdvEngine;

/// <summary>
/// Swaps a mesh for a cheaper one as it gets further away, and drops it entirely past a cull
/// distance. Use in place of <see cref="MeshComponent"/> on anything you place a lot of.
///
/// <code>
/// var lod = new LodComponent();
/// lod.Add(Primitives.Sphere(20, 14).Mesh, "leaves", within: 35f);   // near
/// lod.Add(Primitives.Sphere(10,  7).Mesh, "leaves", within: 90f);   // mid
/// lod.Add(Primitives.Sphere( 5,  4).Mesh, "leaves", within: 200f);  // far
/// lod.CullDistance = 260f;
/// tree.AddComponent(lod);
/// </code>
///
/// <para><b>Where this sits in the pipeline.</b> The renderer resolves the level during its scene
/// walk and pushes the chosen (mesh, material) into the same queue a <see cref="MeshComponent"/>
/// would. Everything downstream is therefore unchanged: frustum culling, instancing, transparency
/// sorting and shadows all work on the result, and every instance that picked the same level
/// batches into one draw call.</para>
///
/// <para><b>Thresholds are PER UNIT OF SCALE.</b> Every distance here is multiplied by the object's
/// world scale, so <c>within: 70</c> means 70 units for a scale-1 object and 182 for one scaled
/// 2.6x. That is what lets one setting serve a forest of varied sizes — a bigger tree should hold
/// its detail further out — but it does mean the numbers depend on how your meshes are scaled. In
/// a scene where everything is scale 1 they are plain distances.</para>
///
/// <para><b>Switching pops.</b> That is inherent to discrete LOD; the mitigation here is
/// hysteresis, not blending — see <see cref="Hysteresis"/>. Dithered cross-fade would need a second
/// draw of each transitioning object and is not worth it at this scale.</para>
/// </summary>
public sealed class LodComponent : BaseComponent
{
    /// <summary>One detail level. <see cref="Within"/> is the distance out to which it is used,
    /// <b>expressed for an object at scale 1</b> — see the class remarks on scaling.</summary>
    public readonly struct Level
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        public readonly float Within;

        public Level(Mesh mesh, Material material, float within)
        {
            Mesh = mesh;
            Material = material;
            Within = within;
        }
    }

    private readonly List<Level> _levels = new();

    public IReadOnlyList<Level> Levels => _levels;

    /// <summary>Beyond this the object is not drawn at all. 0 means never cull by distance.
    /// Per unit of scale, exactly like the level thresholds.</summary>
    public float CullDistance;

    /// <summary>Fraction a threshold is stretched once a level is active, so an object hovering
    /// exactly on a boundary doesn't strobe between two levels as the camera jitters. 0.1 means a
    /// level holds until 10% past its distance before the next one takes over.</summary>
    public float Hysteresis = 0.1f;

    /// <summary>Level chosen last frame, or -1 for culled. Read it to see what LOD an object is
    /// actually using — the first question when a scene looks wrong at distance.</summary>
    public int CurrentLevel { get; private set; } = -1;

    public LodComponent() : base(new LodData()) { }

    /// <summary>Add a level. Order matters: nearest (most detailed) first. <paramref name="within"/>
    /// is per unit of the object's scale.</summary>
    public LodComponent Add(Mesh mesh, string materialName, float within)
    {
        _levels.Add(new Level(mesh, MaterialManager.Get(materialName), within));
        return this;
    }

    /// <summary>
    /// Pick a level for <paramref name="distance"/>, or false when the object should be dropped.
    ///
    /// <para><paramref name="scale"/> is the object's world scale; thresholds are multiplied by it
    /// so a bigger object holds its detail further out.</para>
    /// </summary>
    public bool Select(float distance, float scale, out Mesh mesh, out Material material)
    {
        mesh = null!;
        material = null!;
        if (_levels.Count == 0) return false;

        if (CullDistance > 0f && distance > CullDistance * scale)
        {
            CurrentLevel = -1;
            return false;
        }

        for (int i = 0; i < _levels.Count; i++)
        {
            float limit = _levels[i].Within * scale;
            // Stretch the boundary for the level we are already on, so crossing back and forth
            // needs real movement rather than floating-point noise.
            if (i == CurrentLevel) limit *= 1f + Hysteresis;
            if (distance <= limit)
            {
                CurrentLevel = i;
                mesh = _levels[i].Mesh;
                material = _levels[i].Material;
                return true;
            }
        }

        // Past every threshold but inside the cull distance: keep the coarsest level rather than
        // vanishing, so a missing CullDistance can't silently delete the world.
        CurrentLevel = _levels.Count - 1;
        mesh = _levels[CurrentLevel].Mesh;
        material = _levels[CurrentLevel].Material;
        return true;
    }

    private sealed class LodData : IComponentData
    {
        public string Name { get; set; } = "lod";
        public void SetFromJson(System.Text.Json.JsonElement json) { }
    }
}
