using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.View;

/// <summary>
/// Third-person camera: a pivot that follows the target at shoulder height, turned by mouse look
/// (yaw unbounded, pitch clamped), with its Camera3D child held up to <see cref="Distance"/> metres behind
/// the pivot along the pivot's +Z, so the camera looks along the pivot's -Z at the target.
/// CAMERA-FEEL (design/ART-PLAN.md section 8 lane 10), all rules in the pure, tested <see cref="CameraBoom"/>:
/// <list type="bullet">
/// <item>Start facing: once the run layout is known, the heading turns to <see cref="CameraBoom.StartHeading"/> (down the
/// first corridor), unless the launch replays an input log (<c>--replay-input</c>): SimDriver then puts the log's own
/// <c>startHeading</c> on the rig before the first step, and a log without one (every log written before HOST-HEADING,
/// the committed fixtures among them) keeps the legacy heading, so the E2eAssert digest and HudSceneTest run L replay
/// unchanged. A <c>--record-input</c> launch faces the route like any other and the log records that heading (lane
/// HOST-HEADING).</item>
/// <item>Boom clamp: each frame the boom is shortened (<see cref="CameraBoom.Clamp"/>) so the camera stays
/// <see cref="CameraBoom.MarginMm"/> inside the walls of the plan (<see cref="DungeonBuilder.OccluderBoxes"/>), the same
/// boxes in the GreyBox fallback.</item>
/// <item>Cut-away: any wall piece or pillar still crossing the sight line to the pivot or to a living party body
/// (<see cref="CameraBoom.Occluding"/>) is hidden for that frame by <see cref="DungeonBuilder.CutAway"/>.</item>
/// <item>Party framing (PARTY-FRAMING): each frame <see cref="CameraBoom.FitParty"/> sizes the boom (within
/// <see cref="MinBoom"/>..<see cref="MaxBoom"/>) so every living party body and the Warden's current target, from
/// <see cref="BodyBottomMm"/> to <see cref="BodyTopMm"/> above the feet, sit inside the frustum less <see cref="FramePad"/> and above the ability bar
/// (<see cref="HudBottomPermille"/>). The boom eases toward the fit (<see cref="BoomRate"/>) and the eased boom still
/// goes through the wall clamp, so the walls win. A pitch change the boom cannot cover (fitted at the clamped boom) is a
/// separate eased offset (<see cref="PitchRate"/>) on top of the player's <see cref="Pitch"/>, within its clamp and never flatter than
/// <see cref="FlattestFitPitch"/>; a mouse
/// pitch folds the offset into <see cref="Pitch"/> and holds it at zero for <see cref="PitchHoldSeconds"/>, so mouse
/// look stays authoritative. The fit never writes <see cref="Yaw"/>. A fit no boom or pitch solves logs
/// <c>CAMERA fit unsolvable</c> once per episode and keeps the longest boom.</item>
/// </list>
/// Render-only: it never writes sim state; <see cref="Yaw"/> is still the heading PlayerInput's WASD reads.
/// </summary>
public partial class CameraRig : Node3D
{
    private const float MinPitch = -1.2f;
    private const float MaxPitch = -0.05f;
    private const float MmToM = 0.001f;

    // Sight-line target on a party body: about chest height, mm.
    private const int BodyPointMm = 1200;

    /// <summary>Top of a framed body above its feet, mm (head plus the start of its name plate).</summary>
    public const int BodyTopMm = 2000;

    /// <summary>Bottom of a framed body above its feet, mm (the feet: the whole body reads above the ability bar).</summary>
    public const int BodyBottomMm = 0;

    private readonly HashSet<int> _occluded = new();
    private readonly List<(int X, int Y, int Z)> _points = new();
    private readonly List<int> _pointIds = new();
    private bool _fitted;
    private bool _unsolvable;
    private ulong _unsolvedEpisodes;
    private float _boomWant;
    private float _pitchOffset;
    private float _pitchHold;
    private int _fitMm;
    private bool _headed;
    private NavLayout? _layout;
    private IReadOnlyList<WallBox> _walls = new List<WallBox>();
    private List<PlanBox> _boxes = new();
    private ulong _frames;
    private ulong _clamped;
    private ulong _outside;
    private int _maxCut;

    /// <summary>The node to follow (the Warden's ActorView).</summary>
    [Export]
    public NodePath TargetPath { get; set; } = new("../Actors/Warden");

    /// <summary>The driver to read the run layout and party from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    /// <summary>The kit dungeon shell whose walls are cut away.</summary>
    [Export]
    public NodePath DungeonPath { get; set; } = new("../Dungeon");

    /// <summary>Camera distance behind the pivot before there is a party to frame, metres (walls may shorten it).</summary>
    [Export]
    public float Distance { get; set; } = 7.5f;

    /// <summary>Shortest boom the party fit may choose, metres.</summary>
    [Export]
    public float MinBoom { get; set; } = 4.5f;

    /// <summary>Longest boom the party fit may choose, metres (walls may still shorten it).</summary>
    [Export]
    public float MaxBoom { get; set; } = 12f;

    /// <summary>Margin kept between a framed point and the frustum edge, metres.</summary>
    [Export]
    public float FramePad { get; set; } = 0.5f;

    /// <summary>The flattest pitch the fit's offset may reach, radians (a flatter view drops the camera to the floor).</summary>
    [Export]
    public float FlattestFitPitch { get; set; } = -0.22f;

    /// <summary>How fast the boom eases toward the fit, per second (k = 1 - exp(-dt rate)).</summary>
    [Export]
    public float BoomRate { get; set; } = 6f;

    /// <summary>How fast the fit's pitch offset eases in or out, per second.</summary>
    [Export]
    public float PitchRate { get; set; } = 3f;

    /// <summary>Seconds after a mouse pitch during which the fit adds no pitch offset.</summary>
    [Export]
    public float PitchHoldSeconds { get; set; } = 2f;

    /// <summary>Part of the lower half-frame above the ability bar, permille (the bar's top edge is at 57 percent).</summary>
    [Export]
    public int HudBottomPermille { get; set; } = 550;

    /// <summary>Part of the upper half-frame below the top bar band, permille.</summary>
    [Export]
    public int HudTopPermille { get; set; } = 850;

    /// <summary>Pivot height above the target's feet, metres.</summary>
    [Export]
    public float PivotHeight { get; set; } = 1.4f;

    /// <summary>Heading, radians. Pi looks along world +z; -Pi/2 looks along world +x.</summary>
    public float Yaw { get; private set; } = Mathf.Pi;

    /// <summary>Pitch, radians (negative looks down).</summary>
    public float Pitch { get; private set; } = -0.42f;

    /// <summary>The boom length this frame after the wall clamp, metres.</summary>
    public float Boom { get; private set; }

    private Node3D? Target { get; set; }

    private Camera3D? Camera { get; set; }

    private SimDriver? Driver { get; set; }

    private DungeonBuilder? Dungeon { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Target = GetNodeOrNull<Node3D>(TargetPath);
        Camera = GetNodeOrNull<Camera3D>("Camera");
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        Dungeon = GetNodeOrNull<DungeonBuilder>(DungeonPath);
        Boom = Distance;
        _boomWant = Distance;
        Apply(0f);
    }

    /// <summary>
    /// Turns the rig by a mouse delta, radians. A pitch delta first folds the party fit's pitch offset into
    /// <see cref="Pitch"/> (the view does not jump) and holds the offset at zero for <see cref="PitchHoldSeconds"/>.
    /// </summary>
    public void Look(float yawDelta, float pitchDelta)
    {
        Yaw = Mathf.Wrap(Yaw + yawDelta, -Mathf.Pi, Mathf.Pi);
        if (pitchDelta != 0f)
        {
            Pitch = Mathf.Clamp(Pitch + _pitchOffset, MinPitch, MaxPitch);
            _pitchOffset = 0f;
            _pitchHold = PitchHoldSeconds;
        }
        Pitch = Mathf.Clamp(Pitch + pitchDelta, MinPitch, MaxPitch);
    }

    /// <summary>
    /// Puts the heading back to a value this rig reported as <see cref="Yaw"/> (lane 3E-c, <c>--replay-input</c>): the
    /// recorded yaw is restored bit for bit, with no second wrap, so WASD (PlayerInput reads <see cref="Yaw"/>) walks the
    /// recorded route. Pitch is untouched (movement never reads it).
    /// </summary>
    /// <param name="yaw">A heading in [-Pi, Pi], radians, as <see cref="Yaw"/> reported it.</param>
    public void RestoreYaw(float yaw) => Yaw = yaw;

    /// <summary>
    /// Turns to the start heading now if the run layout is known and it has not happened yet (lane HOST-HEADING):
    /// SimDriver calls it before it samples the recording baseline, so a recording's <c>startHeading</c> is the route
    /// heading whatever order the nodes' first frames run in. Idempotent; the same rule as the first <c>Apply</c>.
    /// </summary>
    public void EnsureStartHeading()
    {
        if (CurrentLayout() is { } layout)
        {
            HeadDownTheRoute(layout);
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta) => Apply((float)delta);

    private NavLayout? CurrentLayout() => Driver?.Run?.Content.Layout ?? Driver?.Setup?.Content.Layout;

    private void Apply(float dt)
    {
        var layout = CurrentLayout();
        if (layout is not null && !ReferenceEquals(layout, _layout))
        {
            _layout = layout;
            _walls = DungeonBuilder.OccluderBoxes(layout);
            _boxes = new List<PlanBox>(DungeonBuilder.PlanBoxesOf(layout));
            HeadDownTheRoute(layout);
        }

        if (Target is not null)
        {
            GlobalPosition = Target.GlobalPosition + new Vector3(0, PivotHeight, 0);
        }
        Rotation = new Vector3(Pitch, Yaw, 0);
        Boom = Distance;
        if (Camera is null)
        {
            return;
        }

        FrameParty(dt);
        Rotation = new Vector3(Mathf.Clamp(Pitch + _pitchOffset, MinPitch, MaxPitch), Yaw, 0);
        Boom = ClampToWalls(_boomWant, Transform.Basis, out var cut);
        if (cut)
        {
            _clamped++;
        }
        Camera.Position = new Vector3(0, 0, Boom);
        if (_layout is not null && _walls.Count > 0)
        {
            CutAwayOccluders(_layout);
            Audit();
        }
    }

    // PARTY-FRAMING: the boom eases toward the fit at the player's pitch (Rotation holds (Pitch, Yaw) here); the pitch
    // offset is fitted at the wall-clamped boom, so a wall that shortens the boom can still be answered by the pitch.
    private void FrameParty(float dt)
    {
        GatherPoints();
        if (_points.Count == 0)
        {
            _boomWant = Ease(_boomWant, Distance, BoomRate, dt);
            _pitchOffset = Ease(_pitchOffset, 0f, PitchRate, dt);
            return;
        }
        var basis = Transform.Basis;
        var pivot = ToMm3(GlobalPosition);
        var forward = ToPermille(-basis.Z);
        var up = ToPermille(basis.Y);
        var (tanH, tanUp, tanDown) = Tangents();
        var pad = Mathf.RoundToInt(FramePad / MmToM);
        var minMm = Mathf.RoundToInt(MinBoom / MmToM);
        var maxMm = Math.Max(minMm, Mathf.RoundToInt(MaxBoom / MmToM));
        var downRoom = Math.Max(0, Mathf.FloorToInt((Pitch - MinPitch) * 1000f));
        var upRoom = Math.Max(0, Mathf.FloorToInt((Mathf.Min(MaxPitch, FlattestFitPitch) - Pitch) * 1000f));

        var fit = CameraBoom.FitParty(pivot, forward, up, _points, tanH, tanUp, tanDown, pad, minMm, maxMm, downRoom, upRoom);
        _fitMm = fit.BoomMm;
        var want = fit.BoomMm * MmToM;
        _boomWant = _fitted ? Ease(_boomWant, want, BoomRate, dt) : want;
        _fitted = true;

        var clampedMm = Mathf.RoundToInt(ClampToWalls(_boomWant, basis, out _) / MmToM);
        var atWall = CameraBoom.FitParty(pivot, forward, up, _points, tanH, tanUp, tanDown, pad, clampedMm, clampedMm, downRoom, upRoom);
        _pitchHold = Math.Max(0f, _pitchHold - dt);
        var pitchWant = _pitchHold > 0f ? 0f : atWall.PitchMrad / 1000f;
        _pitchOffset = Ease(_pitchOffset, pitchWant, PitchRate, dt);

        var unsolvable = !fit.Solved && fit.BoomMm == maxMm;
        if (unsolvable && !_unsolvable)
        {
            _unsolvedEpisodes++;
            var p = _points[fit.Worst];
            GD.Print(FormattableString.Invariant(
                $"CAMERA fit unsolvable frame={_frames} id={_pointIds[fit.Worst]} at=({p.X},{p.Y},{p.Z}) pivot=({pivot.X},{pivot.Y},{pivot.Z}) boom={fit.BoomMm} pitch={fit.PitchMrad}"));
        }
        _unsolvable = unsolvable;
    }

    // Living party bodies and the Warden's current target, feet and top, lerped like the bodies the view draws.
    private void GatherPoints()
    {
        _points.Clear();
        _pointIds.Clear();
        if (Driver is null)
        {
            return;
        }
        var state = Driver.State;
        int? targetId = state.Actors.TryGetValue(SimDriver.WardenId, out var me) ? me.TargetId : null;
        foreach (var (id, actor) in state.Actors)
        {
            if (!actor.Alive || (actor.Kind != ActorKind.Player && id != targetId))
            {
                continue;
            }
            var before = Driver.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : actor;
            var feet = ToMm3(ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), Driver.Alpha));
            _points.Add((feet.X, feet.Y + BodyBottomMm, feet.Z));
            _pointIds.Add(id);
            _points.Add((feet.X, feet.Y + BodyTopMm, feet.Z));
            _pointIds.Add(id);
        }
    }

    // Frustum tangents, permille: Godot's fov is vertical (keep_aspect height), so tanH = tanV * aspect; the HUD bands
    // trim the vertical halves.
    private (int TanH, int TanUp, int TanDown) Tangents()
    {
        var size = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        var aspect = size.Y > 0f ? size.X / size.Y : 16f / 9f;
        var tanV = Mathf.Tan(Mathf.DegToRad(Camera!.Fov) / 2f);
        return (
            Math.Max(1, Mathf.RoundToInt(tanV * aspect * 1000f)),
            Math.Max(1, Mathf.RoundToInt(tanV * HudTopPermille)),
            Math.Max(1, Mathf.RoundToInt(tanV * HudBottomPermille)));
    }

    // The boom after the CAMERA-FEEL wall clamp along this basis's +Z, metres.
    private float ClampToWalls(float boom, Basis basis, out bool cut)
    {
        cut = false;
        if (_walls.Count == 0)
        {
            return boom;
        }
        var pivot = GlobalPosition;
        var desired = pivot + basis * new Vector3(0, 0, boom);
        var fit = CameraBoom.Clamp(ToMm(pivot.X, pivot.Z), ToMm(desired.X, desired.Z), _walls);
        cut = fit.Ppm < CameraBoom.Whole;
        return boom * fit.Ppm / CameraBoom.Whole;
    }

    private static float Ease(float from, float to, float rate, float dt) => from + (to - from) * (1f - Mathf.Exp(-dt * rate));

    private static (int X, int Y, int Z) ToPermille(Vector3 v) =>
        (Mathf.RoundToInt(v.X * 1000f), Mathf.RoundToInt(v.Y * 1000f), Mathf.RoundToInt(v.Z * 1000f));

    // Start facing: once, when the first layout is known, and never on a replay, whose heading is the log's (see the
    // class doc).
    private void HeadDownTheRoute(NavLayout layout)
    {
        if (_headed)
        {
            return;
        }
        _headed = true;
        if (Driver?.Options?.ReplayInput is not null)
        {
            GD.Print(FormattableString.Invariant($"CAMERA start heading kept yaw={Yaw} reason=input-log"));
            return;
        }
        var (dx, dz) = CameraBoom.StartHeading(DungeonBuilder.PlanBoxesOf(layout), DungeonBuilder.RouteOf(layout));
        // The camera looks along the pivot's -Z, which yaw y turns to (-sin y, 0, -cos y).
        Yaw = Mathf.Atan2(-dx, -dz);
        GD.Print(FormattableString.Invariant($"CAMERA start heading=({dx},{dz}) yaw={Yaw}"));
    }

    private void CutAwayOccluders(NavLayout layout)
    {
        _occluded.Clear();
        var from = ToMm3(Camera!.GlobalPosition);
        foreach (var k in CameraBoom.Occluding(from, ToMm3(GlobalPosition), _walls))
        {
            _occluded.Add(k);
        }
        if (Driver is not null)
        {
            var state = Driver.State;
            foreach (var (id, actor) in state.Actors)
            {
                if (actor.Kind != ActorKind.Player || !actor.Alive)
                {
                    continue;
                }
                var before = Driver.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : actor;
                var body = ToMm3(ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), Driver.Alpha));
                foreach (var k in CameraBoom.Occluding(from, (body.X, body.Y + BodyPointMm, body.Z), _walls))
                {
                    _occluded.Add(k);
                }
            }
        }
        if (Dungeon is not null && Dungeon.CutAway(layout, _occluded))
        {
            _maxCut = Math.Max(_maxCut, _occluded.Count);
        }
    }

    // Proof lines: frames where the boom was shortened, the most pieces cut at once, and any frame the camera left the
    // walkable boxes (expected never; printed at once if it happens).
    private void Audit()
    {
        _frames++;
        var cam = Camera!.GlobalPosition;
        var (x, z) = ToMm(cam.X, cam.Z);
        var inside = false;
        foreach (var b in _boxes)
        {
            if (x >= b.MinX && x <= b.MaxX && z >= b.MinZ && z <= b.MaxZ)
            {
                inside = true;
                break;
            }
        }
        if (!inside)
        {
            _outside++;
            GD.Print(FormattableString.Invariant($"CAMERA outside frame={_frames} at=({x},{z})"));
        }
        if (_frames == 120 || _frames % 600 == 0)
        {
            GD.Print(FormattableString.Invariant(
                $"CAMERA audit frame={_frames} clamped={_clamped} outside={_outside} cut={_occluded.Count} maxcut={_maxCut} boom={Boom:F2} fit={_fitMm} pitchoff={_pitchOffset:F3} unsolved={_unsolvedEpisodes}"));
        }
    }

    private static (int X, int Z) ToMm(float x, float z) => (Mathf.RoundToInt(x / MmToM), Mathf.RoundToInt(z / MmToM));

    private static (int X, int Y, int Z) ToMm3(Vector3 v) =>
        (Mathf.RoundToInt(v.X / MmToM), Mathf.RoundToInt(v.Y / MmToM), Mathf.RoundToInt(v.Z / MmToM));
}
