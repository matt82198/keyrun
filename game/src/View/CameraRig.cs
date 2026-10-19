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

    private readonly HashSet<int> _occluded = new();
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

    /// <summary>Camera distance behind the pivot, metres (the full boom; walls may shorten it).</summary>
    [Export]
    public float Distance { get; set; } = 7.5f;

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
        Apply();
    }

    /// <summary>Turns the rig by a mouse delta, radians.</summary>
    public void Look(float yawDelta, float pitchDelta)
    {
        Yaw = Mathf.Wrap(Yaw + yawDelta, -Mathf.Pi, Mathf.Pi);
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
    public override void _Process(double delta) => Apply();

    private NavLayout? CurrentLayout() => Driver?.Run?.Content.Layout ?? Driver?.Setup?.Content.Layout;

    private void Apply()
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
        if (_walls.Count > 0)
        {
            var pivot = GlobalPosition;
            var desired = pivot + Transform.Basis * new Vector3(0, 0, Distance);
            var fit = CameraBoom.Clamp(ToMm(pivot.X, pivot.Z), ToMm(desired.X, desired.Z), _walls);
            Boom = Distance * fit.Ppm / CameraBoom.Whole;
            if (fit.Ppm < CameraBoom.Whole)
            {
                _clamped++;
            }
        }
        Camera.Position = new Vector3(0, 0, Boom);
        if (_layout is not null && _walls.Count > 0)
        {
            CutAwayOccluders(_layout);
            Audit();
        }
    }

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
                $"CAMERA audit frame={_frames} clamped={_clamped} outside={_outside} cut={_occluded.Count} maxcut={_maxCut} boom={Boom:F2}"));
        }
    }

    private static (int X, int Z) ToMm(float x, float z) => (Mathf.RoundToInt(x / MmToM), Mathf.RoundToInt(z / MmToM));

    private static (int X, int Y, int Z) ToMm3(Vector3 v) =>
        (Mathf.RoundToInt(v.X / MmToM), Mathf.RoundToInt(v.Y / MmToM), Mathf.RoundToInt(v.Z / MmToM));
}
