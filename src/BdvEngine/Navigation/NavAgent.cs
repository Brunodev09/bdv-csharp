using System.Numerics;
using System.Text.Json;

namespace BdvEngine;

/// <summary>
/// Walks a <see cref="SimObject"/> along a <see cref="NavMesh"/> path.
///
/// <para>Steering only — it produces a desired horizontal velocity and hands it to a
/// <see cref="CharacterController"/> when there is one, so gravity, slopes, steps and collision
/// stay the controller's job. Without a controller it moves the transform directly, which is right
/// for something flying or for a test.</para>
///
/// <code>
/// var agent = new NavAgent(nav) { Speed = 3.5f };
/// npc.AddComponent(agent);
/// agent.SetDestination(target);
///
/// if (agent.Arrived) { /* ... */ }
/// </code>
/// </summary>
public sealed class NavAgent : BaseComponent, IAvoidanceAgent
{
    private readonly NavPath _path = new();
    private int _index;
    private NavMesh? _mesh;

    // ── link traversal state ──
    private NavLink? _link;
    private Vector3 _linkFrom, _linkTo;
    private float _linkT;

    // ── avoidance ──
    private readonly List<OrcaNeighbour> _neighbours = new();
    private readonly List<OrcaLine> _lines = new();
    private Vector3 _velocity;

    /// <summary>Steer around other agents instead of walking through them. Costs one distance check
    /// per registered agent per frame plus a small solve; turn it off for agents that are alone or
    /// scripted.</summary>
    public bool Avoidance = true;

    /// <summary>Body radius used by the avoidance solver. Should match the character's collider —
    /// too small and agents clip, too large and a crowd deadlocks in a doorway that would fit.</summary>
    [Range(0.05f, 5f)] public float Radius = 0.35f;

    /// <summary>False makes other agents take the full correction rather than assuming this one
    /// yields half. Right for a player or a scripted mover that ignores the crowd.</summary>
    public bool YieldsToOthers = true;

    [Range(0.1f, 30f)] public float Speed = 3f;

    /// <summary>How close counts as reaching a waypoint. Too small and an agent circles a corner it
    /// can never quite touch; too large and it cuts corners visibly.</summary>
    [Range(0.05f, 5f)] public float ArriveRadius = 0.35f;

    /// <summary>Degrees per second the agent may turn. 0 snaps instantly.</summary>
    [Range(0f, 1440f)] public float TurnSpeed = 540f;

    /// <summary>Stop this far from the final destination.</summary>
    [Range(0f, 10f)] public float StoppingDistance = 0.1f;

    /// <summary>True once the last waypoint is reached, or when there was nowhere to go.</summary>
    public bool Arrived { get; private set; } = true;

    /// <summary>True when the last <see cref="SetDestination"/> found no route. Distinct from
    /// <see cref="Arrived"/>: one means "nothing to do", the other means "asked, and it's
    /// impossible" — worth telling apart before an NPC stands still looking broken.</summary>
    public bool PathFailed { get; private set; }

    /// <summary>Current path, for debug drawing. Waypoints know which transitions are links.</summary>
    public NavPath Path => _path;

    /// <summary>The link being traversed right now, or null while walking. Read it to drive an
    /// animation — the <see cref="NavLink.Kind"/> says whether this is a jump, a drop or a climb.</summary>
    public NavLink? TraversingLink => _link;

    /// <summary>Horizontal speed used while crossing a link. Separate from <see cref="Speed"/>
    /// because a jump has its own pace.</summary>
    [Range(0.1f, 30f)] public float LinkSpeed = 4f;

    /// <summary>Peak height of the arc on a <see cref="NavLinkKind.Jump"/>, above the straight line
    /// between its ends. Purely cosmetic — the destination is the same either way.</summary>
    [Range(0f, 5f)] public float JumpArcHeight = 0.9f;

    public int WaypointIndex => _index;

    public NavMesh? Mesh
    {
        get => _mesh;
        set => _mesh = value;
    }

    public NavAgent(NavMesh? mesh = null) : base(new NavAgentData()) => _mesh = mesh;

    // ── IAvoidanceAgent ─────────────────────────────────────────────────────
    public Vector3 AvoidancePosition => _owner?.WorldMatrix.Translation ?? Vector3.Zero;
    public Vector3 AvoidanceVelocity => _velocity;
    public float AvoidanceRadius => Radius;
    public bool AvoidanceReciprocal => YieldsToOthers;

    /// <summary>Mid-link agents drop out of avoidance: they are airborne on a fixed trajectory, and
    /// steering them would either break the jump or make everyone else dodge a body that is not
    /// going to deviate anyway.</summary>
    public bool AvoidanceActive => Avoidance && _link == null && !Arrived;

    public override void Load() => AvoidanceWorld.Register(this);

    public override void Unload() => AvoidanceWorld.Unregister(this);

    /// <summary>Path to a world position. Returns false (and sets <see cref="PathFailed"/>) when
    /// there is no route, leaving the agent where it is rather than drifting toward an
    /// unreachable point.</summary>
    public bool SetDestination(Vector3 destination)
    {
        _path.Clear();
        _index = 0;
        Arrived = false;
        PathFailed = false;

        _link = null;
        if (_mesh == null || _owner == null) { PathFailed = true; Arrived = true; return false; }

        if (!_mesh.FindPath(_owner.WorldMatrix.Translation, destination, _path))
        {
            PathFailed = true;
            Arrived = true;
            return false;
        }

        // The first waypoint is where we already are; skipping it avoids a tiny backward step when
        // the agent is standing slightly off the snapped position.
        if (_path.Count > 1) _index = 1;
        return true;
    }

    public void Stop()
    {
        _path.Clear();
        _index = 0;
        _link = null;
        _velocity = Vector3.Zero;
        Arrived = true;
    }

    public override void Update(double deltaTime)
    {
        if (Arrived || _owner == null || _index >= _path.Count) return;

        float dt = (float)deltaTime;

        if (_link != null) { TraverseLink(dt); return; }

        var position = _owner.WorldMatrix.Translation;
        var target = _path[_index].Position;

        // Horizontal only: the controller owns vertical motion, and steering toward a waypoint's
        // height would fight gravity on every slope.
        var toTarget = new Vector3(target.X - position.X, 0f, target.Z - position.Z);
        float distance = toTarget.Length();

        bool last = _index == _path.Count - 1;
        float threshold = last ? MathF.Max(StoppingDistance, 0.01f) : ArriveRadius;

        if (distance <= threshold)
        {
            // Reaching a link's near end starts the traversal instead of advancing to the next
            // waypoint on foot: the far end is not somewhere you can walk to from here.
            var reached = _path[_index];
            if (reached.IsLinkStart && _index + 1 < _path.Count)
            {
                BeginLink(reached.Link!, reached.Position, _path[_index + 1].Position);
                return;
            }

            _index++;
            if (_index >= _path.Count) { Arrived = true; return; }
            target = _path[_index].Position;
            toTarget = new Vector3(target.X - position.X, 0f, target.Z - position.Z);
            distance = toTarget.Length();
            if (distance < 1e-5f) return;
        }

        var direction = toTarget / MathF.Max(distance, 1e-5f);

        // Slow into the final waypoint so the agent settles instead of overshooting and jittering
        // back. Intermediate waypoints keep full speed — they are corners, not destinations.
        float speed = Speed;
        if (last) speed = MathF.Min(speed, MathF.Max(distance, 0.01f) / MathF.Max(dt, 1e-4f));

        var desired = direction * speed;

        // Avoidance adjusts the velocity we WANT into one that is also safe. Doing it here, on the
        // velocity rather than on the position, is what keeps it compatible with the character
        // controller: the controller still resolves walls and slopes afterwards.
        if (Avoidance)
            desired = AvoidanceWorld.Steer(this, desired, Speed, MathF.Max(dt, 1e-4f),
                                           _neighbours, _lines);

        _velocity = desired;

        var controller = _owner.GetComponent<CharacterController>();
        if (controller != null) controller.Move(desired, deltaTime);
        else _owner.Transform.Position += desired * dt;

        // Face where we are actually going, not where we wanted to: an agent that stares at its
        // goal while side-stepping around someone reads as broken.
        var facing = desired.LengthSquared() > 1e-6f ? Vector3.Normalize(desired) : direction;
        if (TurnSpeed > 0f) FaceAlong(facing, dt);
    }

    private void BeginLink(NavLink link, Vector3 from, Vector3 to)
    {
        _link = link;
        _linkFrom = from;
        _linkTo = to;
        _linkT = 0f;

        // Zero any accumulated fall speed, or the controller resumes a drop mid-jump the instant
        // the traversal hands control back.
        _owner.GetComponent<CharacterController>()?.SetVerticalVelocity(0f);
    }

    /// <summary>
    /// Move along the active link, bypassing the character controller.
    ///
    /// <para>The controller is deliberately not used here: it exists to keep a body on the ground
    /// and out of walls, and a jump is precisely the moment both of those are wrong. It resumes at
    /// the far end.</para>
    /// </summary>
    private void TraverseLink(float dt)
    {
        var link = _link!;
        float span = MathF.Max(Vector3.Distance(_linkFrom, _linkTo), 1e-4f);
        _linkT += MathF.Max(LinkSpeed, 0.01f) * dt / span;

        _velocity = (_linkTo - _linkFrom) * (MathF.Max(LinkSpeed, 0.01f) / span);

        if (_linkT >= 1f)
        {
            _owner.Transform.Position = _linkTo;
            _velocity = Vector3.Zero;
            _link = null;
            _index++;
            if (_index >= _path.Count) Arrived = true;
            return;
        }

        var p = Vector3.Lerp(_linkFrom, _linkTo, _linkT);

        // An arc on jumps only. A drop should fall along the straight line rather than launch
        // upward first, and a climb is vertical already.
        if (link.Kind == NavLinkKind.Jump && JumpArcHeight > 0f)
            p.Y += MathF.Sin(_linkT * MathF.PI) * JumpArcHeight;

        _owner.Transform.Position = p;

        var flat = new Vector3(_linkTo.X - _linkFrom.X, 0f, _linkTo.Z - _linkFrom.Z);
        if (TurnSpeed > 0f && flat.LengthSquared() > 1e-6f) FaceAlong(Vector3.Normalize(flat), dt);
    }

    private void FaceAlong(Vector3 direction, float dt)
    {
        float desired = MathF.Atan2(direction.X, direction.Z);
        float current = _owner.Transform.Rotation.Y;

        // Wrap the difference into [-pi, pi] so an agent turning past due-south takes the short way
        // round instead of spinning almost all the way back.
        float delta = desired - current;
        while (delta > MathF.PI) delta -= MathF.Tau;
        while (delta < -MathF.PI) delta += MathF.Tau;

        float maxStep = TurnSpeed * MathF.PI / 180f * dt;
        var rotation = _owner.Transform.Rotation;
        rotation.Y = current + Math.Clamp(delta, -maxStep, maxStep);
        _owner.Transform.Rotation = rotation;
    }

    private sealed class NavAgentData : IComponentData
    {
        public string Name { get; set; } = "navAgent";
        public void SetFromJson(JsonElement json) { }
    }
}
