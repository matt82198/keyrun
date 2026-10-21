using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Encounter;

namespace Keyrun.View;

/// <summary>
/// Grey-box walls for the dungeon layout (lane NAV-CORE, STATE.md D18): plain CSG boxes built from the run's
/// <see cref="NavLayout"/> (game/content/data/layout.json), so the frames show rooms, corridors and doorways before the
/// kit pieces arrive. Since ART-PLAN lane 2 this is the FALLBACK: DungeonBuilder.cs (the KayKit shell, same layout) switches
/// this node off when game/assets/kaykit-dungeon-remastered/ is present and leaves it on when it is missing. One floor slab per box, a
/// low wall along every box edge except where another box continues through it (the doorways), and a gate slab across
/// each gated corridor's doorway while its pull is uncleared (the sim's own open rule, <see cref="NavLayout.IsOpen"/>).
/// Render-only: it reads <see cref="SimDriver.Run"/> and never writes sim state.
/// </summary>
public partial class GreyBox : Node3D
{
    private const float MmToM = 0.001f;
    private const float FloorThickness = 0.1f;
    private const float WallHeight = 1.6f;
    private const float WallThickness = 0.3f;
    private const float GateHeight = 2.2f;
    private const float GateThickness = 0.4f;

    // Floor colours from game/content/data/palette.json (feat-telegraph-palette): the telegraph contrast rule is pinned against them.
    private static readonly Color RoomFloor = ToGodot(Keyrun.Content.PaletteLoader.Load().Floors.Room);
    private static readonly Color CorridorFloor = ToGodot(Keyrun.Content.PaletteLoader.Load().Floors.Corridor);
    private static readonly Color WallColor = new(0.55f, 0.56f, 0.60f);
    private static readonly Color GateColor = new(0.45f, 0.22f, 0.14f);

    private readonly List<(NavBox Box, CsgBox3D Node)> _gates = new();
    private NavLayout? _built;

    /// <summary>The driver to read the run's layout and pull index from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    /// <summary>Optional floor material (the arena's grid shader); null = flat grey.</summary>
    [Export]
    public Material? FloorMaterial { get; set; }

    private SimDriver? Driver { get; set; }

    /// <inheritdoc />
    public override void _Ready() => Driver = GetNodeOrNull<SimDriver>(DriverPath);

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        var run = Driver?.Run;
        var layout = run?.Content.Layout ?? Driver?.Setup?.Content.Layout;
        if (layout is null)
        {
            return;
        }
        if (!ReferenceEquals(layout, _built))
        {
            Build(layout);
        }
        foreach (var (box, node) in _gates)
        {
            node.Visible = run is null || !NavLayout.IsOpen(box, run.Dungeon, run.Encounter.PullIndex);
        }
    }

    private void Build(NavLayout layout)
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
        _gates.Clear();
        _built = layout;

        var boxes = layout.Boxes.Select(b => b.Box).ToList();
        for (var i = 0; i < layout.Boxes.Count; i++)
        {
            var b = layout.Boxes[i];
            var floor = Slab(b.Id + "-floor",
                new Vector3(Mid(b.Box.MinX, b.Box.MaxX), -FloorThickness / 2, Mid(b.Box.MinZ, b.Box.MaxZ)),
                new Vector3(Len(b.Box.MinX, b.Box.MaxX), FloorThickness, Len(b.Box.MinZ, b.Box.MaxZ)),
                b.IsRoom ? RoomFloor : CorridorFloor);
            if (FloorMaterial is not null && b.IsRoom)
            {
                floor.Material = FloorMaterial;
            }
            AddChild(floor);
        }

        var n = 0;
        foreach (var w in WallSegments(boxes))
        {
            var alongX = w.Z0 == w.Z1;
            var size = alongX
                ? new Vector3(Len(w.X0, w.X1) + WallThickness, WallHeight, WallThickness)
                : new Vector3(WallThickness, WallHeight, Len(w.Z0, w.Z1) + WallThickness);
            AddChild(Slab("wall-" + n++, new Vector3(Mid(w.X0, w.X1), WallHeight / 2, Mid(w.Z0, w.Z1)), size, WallColor));
        }

        for (var k = 1; k < layout.Boxes.Count; k++)
        {
            var box = layout.Boxes[k];
            if (box.GatePullId is null)
            {
                continue;
            }
            var door = layout.Route[k - 1];
            var gate = Slab(box.Id + "-gate",
                new Vector3(door.X * MmToM, GateHeight / 2, Mid(box.Box.MinZ, box.Box.MaxZ)),
                new Vector3(GateThickness, GateHeight, Len(box.Box.MinZ, box.Box.MaxZ)),
                GateColor);
            AddChild(gate);
            _gates.Add((box, gate));
        }
    }

    /// <summary>
    /// Wall segments (mm) along every box edge, minus the stretches where another box carries on through that edge
    /// (a doorway): for an edge at x = e, another box opens it over its z range when it holds points just past e.
    /// Each segment runs along x (Z0 == Z1) or along z (X0 == X1).
    /// </summary>
    /// <param name="boxes">The layout boxes.</param>
    /// <returns>The segments.</returns>
    public static IReadOnlyList<(long X0, long Z0, long X1, long Z1)> WallSegments(IReadOnlyList<Bounds> boxes)
    {
        var walls = new List<(long, long, long, long)>();
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            var others = boxes.Where((_, j) => j != i).ToList();
            // +x edge, -x edge (spans in z); +z edge, -z edge (spans in x).
            foreach (var (z0, z1) in Subtract(b.MinZ, b.MaxZ, others.Where(o => o.MinX <= b.MaxX && o.MaxX > b.MaxX).Select(o => (o.MinZ, o.MaxZ))))
            {
                walls.Add((b.MaxX, z0, b.MaxX, z1));
            }
            foreach (var (z0, z1) in Subtract(b.MinZ, b.MaxZ, others.Where(o => o.MinX < b.MinX && o.MaxX >= b.MinX).Select(o => (o.MinZ, o.MaxZ))))
            {
                walls.Add((b.MinX, z0, b.MinX, z1));
            }
            foreach (var (x0, x1) in Subtract(b.MinX, b.MaxX, others.Where(o => o.MinZ <= b.MaxZ && o.MaxZ > b.MaxZ).Select(o => (o.MinX, o.MaxX))))
            {
                walls.Add((x0, b.MaxZ, x1, b.MaxZ));
            }
            foreach (var (x0, x1) in Subtract(b.MinX, b.MaxX, others.Where(o => o.MinZ < b.MinZ && o.MaxZ >= b.MinZ).Select(o => (o.MinX, o.MaxX))))
            {
                walls.Add((x0, b.MinZ, x1, b.MinZ));
            }
        }
        return walls;
    }

    // [lo, hi] minus the union of the gaps, as ordered non-empty intervals.
    private static IEnumerable<(long Lo, long Hi)> Subtract(long lo, long hi, IEnumerable<(long Lo, long Hi)> gaps)
    {
        var cursor = lo;
        foreach (var (gLo, gHi) in gaps.OrderBy(g => g.Lo))
        {
            if (gHi <= cursor)
            {
                continue;
            }
            if (gLo > cursor)
            {
                yield return (cursor, System.Math.Min(gLo, hi));
            }
            cursor = System.Math.Max(cursor, gHi);
            if (cursor >= hi)
            {
                yield break;
            }
        }
        if (cursor < hi)
        {
            yield return (cursor, hi);
        }
    }

    private static CsgBox3D Slab(string name, Vector3 position, Vector3 size, Color color) => new()
    {
        Name = name,
        Position = position,
        Size = size,
        Material = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.95f },
    };

    // The palette stores 0..255 channels and a 0..100 alpha percentage (ContentPurityTests keeps the content assembly integer-only).
    private static Color ToGodot(Keyrun.Content.PaletteColor c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 100f);

    private static float Mid(long a, long b) => (a + b) * 0.5f * MmToM;

    private static float Len(long a, long b) => (b - a) * MmToM;
}
