using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
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

    private readonly Dictionary<string, Node3D> _shut = new(StringComparer.Ordinal);
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
            GD.Print(FormattableString.Invariant($"GATEVIEW probe tick={tick} shut={(shut.Count == 0 ? "none" : string.Join(",", shut))}"));
        }
    }

    // A new run (Enter, a queued run, the screen): no gate stands. A run created with its pull already spawned (a
    // --pull N start: createRun has no event sink, so no gate_closed) is gated by the sim from its first step, so the
    // gate is put up here from the same state the sim reads (Pulls.IsSpawned on the active pull's room).
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
        if (index < run.Dungeon.Pulls.Count && Pulls.IsSpawned(run.State, run.Dungeon, index) &&
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
        GD.Print(FormattableString.Invariant(
            $"GATEVIEW shut room={room} tick={tick} node={node.Name} x={door.X} z={door.Z} widthMm={door.Width} inTree={node.IsInsideTree()}"));
    }

    private void Open(string room, int tick)
    {
        var had = Remove(room);
        var inTree = GetNodeOrNull<Node3D>(room + NodeSuffix) is not null;
        GD.Print(FormattableString.Invariant($"GATEVIEW open room={room} tick={tick} removed={had} inTree={inTree}"));
    }

    private bool Remove(string room)
    {
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
