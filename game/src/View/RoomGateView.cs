using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.View;

/// <summary>
/// The room's entry gate (GATE-VIEW; STATE.md D27 "the door shuts behind them"): when the sim emits
/// <c>gate_closed {room}</c> (the pull's <c>pull_start</c> tick, every living player inside the room box) this node puts
/// a barred iron gate across that room's entry doorway, and on <c>gate_open {room}</c> (with the <c>pull_clear</c>) it
/// takes it out of the scene tree again. The doorway is where the box before the room in route order (the corridor the
/// party walked in through) meets the room's wall: x = the room box's near edge, the edge <see cref="NavLayout.GateClamp"/>
/// holds actors at, z and width = that corridor's. The corridor-exit gates (<c>wall_gated</c>, hidden on
/// <see cref="NavLayout.IsOpen"/>) stay DungeonBuilder's; layout.json carries no entry-gate geometry, so none is read.
/// Primitive geometry only (BoxMesh posts, beam and bars; no asset, D17). Render-only: nothing it does feeds the sim.
/// Stdout only (never the run log or any digest): <c>GATEVIEW shut|open|probe</c> lines name what is in the scene tree,
/// for <c>game/src/Host/GateViewSceneTest.ps1</c>.
/// Cut-away (GATE-CUTAWAY): a shut gate is an occluder like a room wall. Each frame <see cref="CameraRig"/> hands its
/// sight lines (camera to pivot and to each living party body) to <see cref="CutAway"/>; a shut gate whose
/// <see cref="BoxOf"/> box one of them crosses (<see cref="CameraBoom.Occluding"/>, the walls' rule) is hidden
/// (<c>Visible</c> false) for that frame and shown again the frame it is clear, so a gate behind the party stays in view.
/// Foreground (GATE-FOREGROUND): a shut gate whose screen rect (its box projected through the viewport camera) overlaps
/// the ability bar footprint (<see cref="Keyrun.Hud.AbilityBar.Footprint"/>), or holds a party body it stands nearer the
/// camera than, is cut the same way: a camera behind the doorway (the Choir entry) sees over the gate, which would
/// otherwise fill the bottom of the screen under the bar. The probe adds <c>scr=</c>, <c>bar=</c> and <c>party=</c>.
/// The probe line adds <c>cut=</c> (shut gates not visible), <c>cam=</c> and <c>at=</c> (the last sight lines, mm) for
/// <c>game/src/Host/GateCutawaySceneTest.ps1</c>.
/// </summary>
public partial class RoomGateView : Node3D
{
    private const float MmToM = 0.001f;
    private const float GateHeightM = 2.2f;
    private const float PostWidthM = 0.22f;
    private const float BarWidthM = 0.07f;
    private const float BarSpacingM = 0.32f;
    private const float BeamHeightM = 0.18f;
    private const int ProbeEveryTicks = 20;
    private const string NodeSuffix = "-entry-gate";

    private static readonly Color IronColor = new(0.16f, 0.15f, 0.15f);
    private static readonly Color GlowColor = new(0.85f, 0.32f, 0.12f);

    private const int GateHeightMm = 2200;
    private const int PostHalfMm = 110;

    private readonly Dictionary<string, Node3D> _shut = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WallBox> _boxes = new(StringComparer.Ordinal);
    private readonly List<(int X, int Y, int Z)> _sightTo = new();
    private readonly Dictionary<string, (int X0, int Y0, int X1, int Y1, int Near)?> _screen = new(StringComparer.Ordinal);
    private readonly List<(int X, int Y, int Depth)> _partyScreen = new();
    private (int X0, int Y0, int X1, int Y1)? _bar;
    private (int X, int Y, int Z)? _sightFrom;
    private StandardMaterial3D? _iron;

    /// <summary>The driver to read the run, its layout and its events from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    private SimDriver? Driver { get; set; }

    /// <summary>The rooms whose entry gate node is in the scene tree now, in name order.</summary>
    public IReadOnlyList<string> ShutRooms =>
        GetChildren().OfType<Node3D>()
            .Where(n => n.IsInsideTree() && n.Name.ToString().EndsWith(NodeSuffix, StringComparison.Ordinal))
            .Select(n => n.Name.ToString()[..^NodeSuffix.Length])
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The entry doorway of <paramref name="roomId"/> in <paramref name="layout"/>, mm: the x of the room wall the
    /// previous box (in route order) opens through, and that box's z centre and z width. Null for an unknown room or
    /// the first box (nothing leads into it).
    /// </summary>
    /// <param name="layout">The run's layout.</param>
    /// <param name="roomId">The room box id a <c>gate_closed</c> event names.</param>
    /// <returns>(X, Z, Width) in mm, or null.</returns>
    public static (long X, long Z, long Width)? EntryOf(NavLayout layout, string roomId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var k = -1;
        for (var i = 0; i < layout.Boxes.Count; i++)
        {
            if (string.Equals(layout.Boxes[i].Id, roomId, StringComparison.Ordinal))
            {
                k = i;
                break;
            }
        }
        if (k < 1)
        {
            return null;
        }
        var room = layout.Boxes[k].Box;
        var prev = layout.Boxes[k - 1].Box;
        // The route runs along x: the previous box lies on the room's low-x side or its high-x side.
        var x = prev.MinX + prev.MaxX < room.MinX + room.MaxX ? room.MinX : room.MaxX;
        return (x, IntMath.FloorDiv(prev.MinZ + prev.MaxZ, 2), prev.MaxZ - prev.MinZ);
    }

    /// <summary>
    /// The solid box of the gate across <paramref name="door"/>, mm: post thickness in x about the doorway x, the
    /// doorway width in z, floor to beam top in y. The camera cut-away tests sight lines against it.
    /// </summary>
    /// <param name="room">The gated room id.</param>
    /// <param name="door">The doorway from <see cref="EntryOf"/>.</param>
    /// <returns>The gate box.</returns>
    public static WallBox BoxOf(string room, (long X, long Z, long Width) door)
    {
        var x = checked((int)door.X);
        var z = checked((int)door.Z);
        var half = checked((int)(door.Width / 2));
        return new WallBox(room, x - PostHalfMm, x + PostHalfMm, z - half, z + half, 0, GateHeightMm);
    }

    /// <summary>
    /// Cuts away, for this frame, every shut gate a sight line from <paramref name="from"/> to one of
    /// <paramref name="targets"/> crosses (hidden), and shows every other shut gate. The lines are kept, so a gate that
    /// shuts before the next frame obeys them at once.
    /// </summary>
    /// <param name="from">The camera, mm.</param>
    /// <param name="targets">Sight-line targets (pivot, party bodies), mm.</param>
    public void CutAway((int X, int Y, int Z) from, IReadOnlyList<(int X, int Y, int Z)> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        _sightFrom = from;
        _sightTo.Clear();
        _sightTo.AddRange(targets);
        ProjectHud();
        foreach (var room in _shut.Keys)
        {
            ApplyCut(room);
        }
    }

    private void ApplyCut(string room)
    {
        if (!_shut.TryGetValue(room, out var node) || !_boxes.TryGetValue(room, out var box))
        {
            return;
        }
        var hidden = false;
        if (_sightFrom is { } from)
        {
            var one = new[] { box };
            foreach (var to in _sightTo)
            {
                if (CameraBoom.Occluding(from, to, one).Count > 0)
                {
                    hidden = true;
                    break;
                }
            }
        }
        var screen = ScreenOf(box);
        _screen[room] = screen;
        if (screen is { } r && InForeground(r))
        {
            hidden = true;
        }
        node.Visible = !hidden;
    }

    // GATE-FOREGROUND: the ability bar footprint and the sight targets (party bodies) projected through the viewport
    // camera this frame, px (rounded), the targets with their view depth, mm; a target behind the camera is left out.
    private void ProjectHud()
    {
        _bar = null;
        _partyScreen.Clear();
        var viewport = GetViewport();
        if (viewport?.GetCamera3D() is not { } camera)
        {
            return;
        }
        var bar = Keyrun.Hud.AbilityBar.Footprint(viewport.GetVisibleRect().Size, (Driver?.Setup?.Slots.Count ?? 0) + 1);
        _bar = (Mathf.RoundToInt(bar.Position.X), Mathf.RoundToInt(bar.Position.Y), Mathf.RoundToInt(bar.End.X), Mathf.RoundToInt(bar.End.Y));
        foreach (var t in _sightTo)
        {
            var p = new Vector3(t.X * MmToM, t.Y * MmToM, t.Z * MmToM);
            if (camera.IsPositionBehind(p))
            {
                continue;
            }
            var s = camera.UnprojectPosition(p);
            _partyScreen.Add((Mathf.RoundToInt(s.X), Mathf.RoundToInt(s.Y), DepthMm(camera, p)));
        }
    }

    // The screen rect of a gate box through the viewport camera (its 8 corners projected, px, rounded) and its nearest
    // corner's view depth, mm. A box straddling the camera plane is the whole screen at depth 0; one wholly behind the
    // camera is not on screen (null).
    private (int X0, int Y0, int X1, int Y1, int Near)? ScreenOf(WallBox b)
    {
        var viewport = GetViewport();
        if (viewport?.GetCamera3D() is not { } camera)
        {
            return null;
        }
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        var near = int.MaxValue;
        var behind = 0;
        foreach (var cx in new[] { b.MinX, b.MaxX })
        {
            foreach (var cy in new[] { b.MinY, b.MaxY })
            {
                foreach (var cz in new[] { b.MinZ, b.MaxZ })
                {
                    var p = new Vector3(cx * MmToM, cy * MmToM, cz * MmToM);
                    near = Math.Min(near, DepthMm(camera, p));
                    if (camera.IsPositionBehind(p))
                    {
                        behind++;
                        continue;
                    }
                    var s = camera.UnprojectPosition(p);
                    x0 = Math.Min(x0, s.X);
                    y0 = Math.Min(y0, s.Y);
                    x1 = Math.Max(x1, s.X);
                    y1 = Math.Max(y1, s.Y);
                }
            }
        }
        if (behind == 8)
        {
            return null;
        }
        if (behind > 0)
        {
            var size = viewport.GetVisibleRect().Size;
            return (0, 0, Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y), 0);
        }
        return (Mathf.RoundToInt(x0), Mathf.RoundToInt(y0), Mathf.RoundToInt(x1), Mathf.RoundToInt(y1), near);
    }

    // A gate in the foreground (GATE-FOREGROUND): its screen rect overlaps the ability bar footprint, or holds a party
    // body it is nearer the camera than. Such a gate is cut for the frame like one a sight line crosses.
    private bool InForeground((int X0, int Y0, int X1, int Y1, int Near) r)
    {
        if (_bar is { } bar && r.X0 < bar.X1 && r.X1 > bar.X0 && r.Y0 < bar.Y1 && r.Y1 > bar.Y0)
        {
            return true;
        }
        foreach (var q in _partyScreen)
        {
            if (q.X >= r.X0 && q.X <= r.X1 && q.Y >= r.Y0 && q.Y <= r.Y1 && r.Near < q.Depth)
            {
                return true;
            }
        }
        return false;
    }

    private static int DepthMm(Camera3D camera, Vector3 p) =>
        Mathf.RoundToInt((p - camera.GlobalPosition).Dot(-camera.GlobalBasis.Z) / MmToM);

    private string ProbeSight()
    {
        var cut = _shut.Where(kv => !kv.Value.Visible).Select(kv => kv.Key).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var cam = _sightFrom is { } f ? FormattableString.Invariant($"({f.X},{f.Y},{f.Z})") : "(0,0,0)";
        var at = _sightTo.Count == 0 ? "none" : string.Join(";", _sightTo.Select(t => FormattableString.Invariant($"({t.X},{t.Y},{t.Z})")));
        return $"cut={(cut.Count == 0 ? "none" : string.Join(",", cut))} cam={cam} at={at} {ProbeScreen()}";
    }

    // GATE-FOREGROUND proof fields (stdout only), exactly what the last cut decided on: scr= every shut gate on screen
    // with its rect and nearest view depth (cut or drawn; cut= says which), bar= the ability bar footprint, party= each
    // sight target on screen with its view depth.
    private string ProbeScreen()
    {
        var rects = _shut.Keys.OrderBy(r => r, StringComparer.Ordinal)
            .Where(r => _screen.TryGetValue(r, out var s) && s is not null)
            .Select(r =>
            {
                var s = _screen[r]!.Value;
                return FormattableString.Invariant($"{r}:({s.X0},{s.Y0},{s.X1},{s.Y1},{s.Near})");
            })
            .ToList();
        var bar = _bar is { } b ? FormattableString.Invariant($"({b.X0},{b.Y0},{b.X1},{b.Y1})") : "none";
        var party = _partyScreen.Count == 0 ? "none" : string.Join(";", _partyScreen.Select(q => FormattableString.Invariant($"({q.X},{q.Y},{q.Depth})")));
        return $"scr={(rects.Count == 0 ? "none" : string.Join(";", rects))} bar={bar} party={party}";
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        _iron = new StandardMaterial3D
        {
            AlbedoColor = IronColor,
            Metallic = 0.7f,
            Roughness = 0.45f,
            EmissionEnabled = true,
            Emission = GlowColor,
            EmissionEnergyMultiplier = 0.35f,
        };
        if (Driver is not null)
        {
            Driver.Stepped += OnStepped;
            Driver.RunStarted += OnRunStarted;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (Driver is not null)
        {
            Driver.Stepped -= OnStepped;
            Driver.RunStarted -= OnRunStarted;
        }
    }

    private void OnStepped(int preTick, IReadOnlyList<SimEvent> events)
    {
        var layout = Driver?.Run?.Content.Layout;
        foreach (var e in events)
        {
            if (e.Get("room") is not JsString room)
            {
                continue;
            }
            if (e.Type == "gate_closed" && layout is not null)
            {
                Shut(layout, room.Value, e.Tick);
            }
            else if (e.Type == "gate_open")
            {
                Open(room.Value, e.Tick);
            }
        }
        var tick = Driver?.State.Tick ?? preTick + 1;
        if (layout is not null && tick % ProbeEveryTicks == 0)
        {
            var shut = ShutRooms;
            GD.Print(FormattableString.Invariant($"GATEVIEW probe tick={tick} shut={(shut.Count == 0 ? "none" : string.Join(",", shut))} {ProbeSight()}"));
        }
    }

    // A new run (Enter, a queued run, the screen): no gate stands. A run created with its active pull already engaged
    // (createRun has no event sink, so no gate_closed) is gated by the sim from its first step, so the gate is put up
    // here from the same state the sim reads (Pulls.IsEngaged on the active pull). PRESPAWN-AGGRO: every layout pull
    // exists, idle, from tick 0, so "spawned" no longer means "fought".
    private void OnRunStarted()
    {
        foreach (var room in _shut.Keys.ToList())
        {
            Remove(room);
        }
        if (Driver?.Run is not { } run || run.Content.Layout is not { } layout)
        {
            return;
        }
        var index = run.Encounter.PullIndex;
        if (index < run.Dungeon.Pulls.Count && Pulls.IsEngaged(run.Encounter, run.Dungeon, index) &&
            layout.RoomFor(run.Dungeon.Pulls[index].Id) is { } box)
        {
            Shut(layout, box.Id, run.State.Tick);
        }
    }

    private void Shut(NavLayout layout, string room, int tick)
    {
        if (_shut.ContainsKey(room) || EntryOf(layout, room) is not { } door)
        {
            return;
        }
        var node = Build(room, door.X * MmToM, door.Z * MmToM, door.Width * MmToM);
        AddChild(node);
        _shut[room] = node;
        var box = BoxOf(room, door);
        _boxes[room] = box;
        ApplyCut(room);
        GD.Print(FormattableString.Invariant(
            $"GATEVIEW shut room={room} tick={tick} node={node.Name} x={door.X} z={door.Z} widthMm={door.Width} box=({box.MinX},{box.MaxX},{box.MinZ},{box.MaxZ},{box.MinY},{box.MaxY}) inTree={node.IsInsideTree()}"));
    }

    private void Open(string room, int tick)
    {
        var had = Remove(room);
        var inTree = GetNodeOrNull<Node3D>(room + NodeSuffix) is not null;
        GD.Print(FormattableString.Invariant($"GATEVIEW open room={room} tick={tick} removed={had} inTree={inTree}"));
    }

    private bool Remove(string room)
    {
        _boxes.Remove(room);
        _screen.Remove(room);
        if (!_shut.Remove(room, out var node))
        {
            return false;
        }
        RemoveChild(node);
        node.QueueFree();
        return true;
    }

    // A barred frame across the doorway (the opening runs along z at x): two posts, a top beam, a low rail and evenly
    // spaced vertical bars. Every part is a BoxMesh.
    private Node3D Build(string room, float x, float z, float width)
    {
        var gate = new Node3D { Name = room + NodeSuffix, Position = new Vector3(x, 0, z) };
        var half = width / 2;
        Part(gate, "post-a", new Vector3(0, GateHeightM / 2, -half + PostWidthM / 2), new Vector3(PostWidthM, GateHeightM, PostWidthM));
        Part(gate, "post-b", new Vector3(0, GateHeightM / 2, half - PostWidthM / 2), new Vector3(PostWidthM, GateHeightM, PostWidthM));
        Part(gate, "beam", new Vector3(0, GateHeightM - BeamHeightM / 2, 0), new Vector3(PostWidthM, BeamHeightM, width));
        Part(gate, "rail", new Vector3(0, 0.45f, 0), new Vector3(BarWidthM * 1.6f, BarWidthM * 1.6f, width - 2 * PostWidthM));
        var inner = width - 2 * PostWidthM;
        var bars = Math.Max(1, (int)(inner / BarSpacingM));
        for (var i = 0; i < bars; i++)
        {
            var bz = -inner / 2 + (i + 0.5f) * (inner / bars);
            Part(gate, "bar-" + i, new Vector3(0, (GateHeightM - BeamHeightM) / 2, bz),
                new Vector3(BarWidthM, GateHeightM - BeamHeightM, BarWidthM));
        }
        return gate;
    }

    private void Part(Node3D parent, string name, Vector3 at, Vector3 size) =>
        parent.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            Position = at,
            MaterialOverride = _iron,
        });
}
