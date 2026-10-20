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
/// (yaw unbounded, pitch clamped), with its Camera3D child held up to <see cref="ZoomMm"/> behind
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
/// (<see cref="CameraBoom.Occluding"/>) is hidden for that frame by <see cref="DungeonBuilder.CutAway"/>. The same sight
/// lines go to <see cref="RoomGateView.CutAway"/>, which hides a shut room entry gate they cross by the same rule
/// (GATE-CUTAWAY).</item>
/// <item>WoW framing (CAMERA-FRAMING, supersedes PARTY-FRAMING's boom and pitch fit; Matt 2026-10-20 "it should feel
/// almost exact to wow"): the camera follows the player at the wheel zoom (<see cref="ZoomMm"/>, starting at
/// <see cref="CameraFraming.DefaultBoomMm"/>, one <see cref="CameraFraming.ZoomStepMm"/> per wheel notch, clamped by
/// <see cref="CameraFraming.Zoom"/>), pitched <see cref="CameraFraming.DefaultPitchMrad"/> at
/// <see cref="CameraFraming.VerticalFovDeg"/>, so the player is drawn a quarter to a third of the frame tall. The boom
/// eases toward the zoom (<see cref="BoomRate"/>) and the eased boom still goes through the wall clamp, so the walls
/// win. The boom no longer grows to fit the whole party and the camera never pitches on its own: the party frames say
/// where the bots are, as in WoW. <see cref="CameraBoom.FitParty"/> still runs at the zoom with no pitch room as a
/// readout only: the audit line's <c>fit=</c> and a <c>CAMERA fit unsolvable</c> line once per episode report a living
/// party body or the Warden's current target (from <see cref="BodyBottomMm"/> to <see cref="BodyTopMm"/>) out of frame.
/// Proof line, measured on the drawn camera (<c>Camera3D.UnprojectPosition</c> of the Warden's feet and
/// <see cref="CameraFraming.PlayerHeightMm"/> above them): <c>CAMERA framing frame=F player=P zoom=Z boom=B</c>, P the
/// Warden's drawn height in permille of the frame (game/src/View/CameraFramingSceneTest.ps1).</item>
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
    private readonly List<(int X, int Y, int Z)> _sightTargets = new();
    private readonly List<(int X, int Y, int Z)> _points = new();
    private readonly List<int> _pointIds = new();
    private bool _unsolvable;
    private ulong _unsolvedEpisodes;
    private float _boomWant;
    private int _fitMm;
    private int _framedPermille = -1;
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

    /// <summary>The room entry gates (GATE-VIEW) cut away like walls.</summary>
    [Export]
    public NodePath GatesPath { get; set; } = new("../RoomGates");

    /// <summary>The wheel zoom: the boom the camera wants behind the pivot, mm (walls may shorten it).</summary>
    public int ZoomMm { get; private set; } = CameraFraming.DefaultBoomMm;

    /// <summary>Margin kept between a framed point and the frustum edge in the party readout, metres.</summary>
    [Export]
    public float FramePad { get; set; } = 0.5f;

    /// <summary>How fast the boom eases toward the zoom, per second (k = 1 - exp(-dt rate)).</summary>
    [Export]
    public float BoomRate { get; set; } = 6f;

    /// <summary>Part of the lower half-frame above the ability bar, permille (the bar's top edge is at 57 percent).</summary>
    [Export]
    public int HudBottomPermille { get; set; } = 550;

    /// <summary>Part of the upper half-frame below the top bar band, permille.</summary>
    [Export]
    public int HudTopPermille { get; set; } = 850;

    /// <summary>Pivot height above the target's feet, metres.</summary>
    [Export]
    public float PivotHeight { get; set; } = CameraFraming.PivotHeightMm * MmToM;

    /// <summary>Heading, radians. Pi looks along world +z; -Pi/2 looks along world +x.</summary>
    public float Yaw { get; private set; } = Mathf.Pi;

    /// <summary>Pitch, radians (negative looks down).</summary>
    public float Pitch { get; private set; } = CameraFraming.DefaultPitchMrad / 1000f;

    /// <summary>The boom length this frame after the wall clamp, metres.</summary>
    public float Boom { get; private set; }

    private Node3D? Target { get; set; }

    private Camera3D? Camera { get; set; }

    private SimDriver? Driver { get; set; }

    private DungeonBuilder? Dungeon { get; set; }

    private RoomGateView? Gates { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Target = GetNodeOrNull<Node3D>(TargetPath);
        Camera = GetNodeOrNull<Camera3D>("Camera");
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        Dungeon = GetNodeOrNull<DungeonBuilder>(DungeonPath);
        Gates = GetNodeOrNull<RoomGateView>(GatesPath);
        if (Camera is not null)
        {
            // WoW's default lens (CameraFraming.VerticalFovDeg); the rig owns its camera's framing.
            Camera.Fov = CameraFraming.VerticalFovDeg;
        }
        Boom = ZoomMm * MmToM;
        _boomWant = Boom;
        Apply(0f);
    }

    /// <summary>Turns the rig by a mouse delta, radians (pitch within its clamp).</summary>
    public void Look(float yawDelta, float pitchDelta)
    {
        Yaw = Mathf.Wrap(Yaw + yawDelta, -Mathf.Pi, Mathf.Pi);
        Pitch = Mathf.Clamp(Pitch + pitchDelta, MinPitch, MaxPitch);
    }

    /// <summary>Zooms by mouse-wheel notches (negative is in), within the <see cref="CameraFraming"/> zoom clamp.</summary>
    public void Zoom(int notches) => ZoomMm = CameraFraming.Zoom(ZoomMm, notches);

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp)
            {
                Zoom(-1);
            }
            else if (wheel.ButtonIndex == MouseButton.WheelDown)
            {
                Zoom(1);
            }
        }
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
        if (Camera is null)
        {
            Boom = ZoomMm * MmToM;
            return;
        }

        _boomWant = Ease(_boomWant, ZoomMm * MmToM, BoomRate, dt);
        Boom = ClampToWalls(_boomWant, Transform.Basis, out var cut);
        if (cut)
        {
            _clamped++;
        }
        Camera.Position = new Vector3(0, 0, Boom);
        ReadParty();
        if (_layout is not null && _walls.Count > 0)
        {
            CutAwayOccluders(_layout);
            Audit();
        }
    }

    // CAMERA-FRAMING: the party fit is a readout at the drawn boom with no pitch room (it never moves the camera): fit=
    // in the audit line is the boom the fit would want there, and a body it cannot frame logs once per episode.
    private void ReadParty()
    {
        GatherPoints();
        if (_points.Count == 0)
        {
            return;
        }
        var basis = Transform.Basis;
        var pivot = ToMm3(GlobalPosition);
        var forward = ToPermille(-basis.Z);
        var up = ToPermille(basis.Y);
        var (tanH, tanUp, tanDown) = Tangents();
        var pad = Mathf.RoundToInt(FramePad / MmToM);
        var boomMm = Math.Max(1, Mathf.RoundToInt(Boom / MmToM));

        var fit = CameraBoom.FitParty(pivot, forward, up, _points, tanH, tanUp, tanDown, pad, boomMm, boomMm, 0, 0);
        _fitMm = fit.BoomMm;

        var unsolvable = !fit.Solved;
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
        _sightTargets.Clear();
        var from = ToMm3(Camera!.GlobalPosition);
        var pivot = ToMm3(GlobalPosition);
        _sightTargets.Add(pivot);
        foreach (var k in CameraBoom.Occluding(from, pivot, _walls))
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
                var chest = (body.X, body.Y + BodyPointMm, body.Z);
                _sightTargets.Add(chest);
                foreach (var k in CameraBoom.Occluding(from, chest, _walls))
                {
                    _occluded.Add(k);
                }
            }
        }
        Gates?.CutAway(from, _sightTargets);
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
        if (_frames % 60 == 0)
        {
            MeasureFraming();
            GD.Print(FormattableString.Invariant(
                $"CAMERA framing frame={_frames} player={_framedPermille} zoom={ZoomMm} boom={Boom:F2} pitch={Pitch:F3} fov={Camera.Fov:F1}"));
        }
        if (_frames == 120 || _frames % 600 == 0)
        {
            GD.Print(FormattableString.Invariant(
                $"CAMERA audit frame={_frames} clamped={_clamped} outside={_outside} cut={_occluded.Count} maxcut={_maxCut} boom={Boom:F2} fit={_fitMm} unsolved={_unsolvedEpisodes}"));
        }
    }

    // The Warden's drawn height on the real camera: its feet and CameraFraming.PlayerHeightMm above them projected to the
    // viewport, permille of the viewport height (-1 when either point is behind the camera or there is no target).
    private void MeasureFraming()
    {
        _framedPermille = -1;
        var size = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        if (Target is null || Camera is null || size.Y <= 0f)
        {
            return;
        }
        var feet = Target.GlobalPosition;
        var head = feet + new Vector3(0, CameraFraming.PlayerHeightMm * MmToM, 0);
        if (Camera.IsPositionBehind(feet) || Camera.IsPositionBehind(head))
        {
            return;
        }
        var span = Camera.UnprojectPosition(feet).Y - Camera.UnprojectPosition(head).Y;
        _framedPermille = Mathf.RoundToInt(span / size.Y * 1000f);
    }

    private static (int X, int Z) ToMm(float x, float z) => (Mathf.RoundToInt(x / MmToM), Mathf.RoundToInt(z / MmToM));

    private static (int X, int Y, int Z) ToMm3(Vector3 v) =>
        (Mathf.RoundToInt(v.X / MmToM), Mathf.RoundToInt(v.Y / MmToM), Mathf.RoundToInt(v.Z / MmToM));
}
