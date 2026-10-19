using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Encounter;

namespace Keyrun.View;

/// <summary>
/// The dungeon shell (design/ART-PLAN.md section 8 lane 2; STATE.md D16-D18): dresses the run's rooms and corridors
/// with the CC0 KayKit Dungeon Remastered kit (game/assets/kaykit-dungeon-remastered/, provenance in
/// game/assets/ATTRIBUTION.md) and lights them room by room. It reads the same <see cref="NavLayout"/> GreyBox reads
/// (<c>SimDriver.Run.Content.Layout</c>, built from game/content/data/layout.json), hands it to the pure, unit-tested
/// <see cref="DungeonPlan"/> and only instantiates what that returns: one MultiMeshInstance3D per kit mesh (so the whole
/// dungeon costs a few dozen draw calls), one gate node per gated corridor (hidden once its pull clears, the sim's own
/// <see cref="NavLayout.IsOpen"/> rule) and one OmniLight3D per planned light.
/// Units: the plan is in mm, Godot is 1 unit = 1 m (ActorView.MmToM), so every position and size is divided by 1000.
/// Fallback: if the kit folder is missing (an asset-less checkout), this node switches itself off and leaves the
/// GreyBox CSG walls on; when the kit is present it switches GreyBox off. Render-only: it never writes sim state.
/// </summary>
public partial class DungeonBuilder : Node3D
{
    private const float MmToM = 0.001f;

    private readonly Dictionary<string, List<(Mesh Mesh, Transform3D Local)>> _meshes = new(StringComparer.Ordinal);
    private readonly List<(NavBox Box, Node3D Node)> _gates = new();

    // CAMERA-FEEL (lane 10): every MultiMesh instance of occluder k (wall piece k, then door-frame pillar k - walls),
    // with its placed transform, so CameraRig can cut it away (zero-scale) and put it back.
    private readonly List<List<(MultiMesh Mesh, int Index, Transform3D Placed)>> _occluderSlots = new();
    private readonly HashSet<int> _cut = new();
    private NavLayout? _built;
    private ulong _frames;
    private long _maxDrawCalls;

    /// <summary>The driver to read the run's layout and pull index from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    /// <summary>The grey-box fallback this node replaces when the kit is present.</summary>
    [Export]
    public NodePath GreyBoxPath { get; set; } = new("../GreyBox");

    /// <summary>The kit folder (res:// path).</summary>
    [Export]
    public string KitDir { get; set; } = "res://assets/kaykit-dungeon-remastered";

    /// <summary>The one material every kit mesh is drawn with (the pack's shared colour atlas).</summary>
    [Export]
    public Material? KitMaterial { get; set; }

    /// <summary>Torchlight colour (every room but the boss arena).</summary>
    [Export]
    public Color TorchColor { get; set; } = new(1.0f, 0.62f, 0.32f);

    /// <summary>Torchlight energy.</summary>
    [Export]
    public float TorchEnergy { get; set; } = 2.2f;

    /// <summary>The Warden arena's colder vault light.</summary>
    [Export]
    public Color WardenColor { get; set; } = new(0.55f, 0.72f, 1.0f);

    /// <summary>Warden arena light energy.</summary>
    [Export]
    public float WardenEnergy { get; set; } = 3.0f;

    /// <summary>True when the kit folder is present and this node draws the dungeon (false = GreyBox fallback).</summary>
    public bool KitAvailable { get; private set; }

    private SimDriver? Driver { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        KitAvailable = ResourceLoader.Exists(KitDir + "/" + DungeonPlan.FileOf("wall"));
        var grey = GetNodeOrNull<Node3D>(GreyBoxPath);
        if (KitAvailable)
        {
            if (grey is not null)
            {
                grey.Visible = false;
                grey.ProcessMode = ProcessModeEnum.Disabled;
            }
        }
        else
        {
            GD.Print("DUNGEON fallback greybox kit=" + KitDir);
            ProcessMode = ProcessModeEnum.Disabled;
        }
    }

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
        ReportFrameCost();
    }

    private void Build(NavLayout layout)
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
        _gates.Clear();
        _occluderSlots.Clear();
        _cut.Clear();
        _built = layout;

        var boxes = PlanBoxesOf(layout);
        var route = RouteOf(layout);
        var plan = DungeonPlan.Build(boxes, route);

        var placed = new List<KitPiece>();
        placed.AddRange(plan.Floor.Select(Tile));
        placed.AddRange(plan.WallPieces.Select(w => w.Piece));
        placed.AddRange(plan.DoorFrames);
        placed.AddRange(plan.Props);
        placed.AddRange(plan.WallDecor);

        // Occluder k = wall piece k, then door frame k - walls: the order OccluderBoxes (and so CameraRig) uses.
        var occluderOf = new Dictionary<KitPiece, int>(ReferenceEqualityComparer.Instance);
        foreach (var piece in plan.WallPieces.Select(w => w.Piece).Concat(plan.DoorFrames))
        {
            occluderOf[piece] = _occluderSlots.Count;
            _occluderSlots.Add(new List<(MultiMesh, int, Transform3D)>());
        }

        var multimeshes = 0;
        foreach (var group in placed.GroupBy(p => p.Piece, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var parts = MeshesOf(group.Key);
            for (var m = 0; m < parts.Count; m++)
            {
                var (mesh, local) = parts[m];
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = mesh,
                    InstanceCount = group.Count(),
                };
                var i = 0;
                foreach (var p in group)
                {
                    var xf = Placement(p) * local;
                    if (occluderOf.TryGetValue(p, out var k))
                    {
                        _occluderSlots[k].Add((mm, i, xf));
                    }
                    mm.SetInstanceTransform(i++, xf);
                }
                AddChild(new MultiMeshInstance3D
                {
                    Name = group.Key + "-" + m,
                    Multimesh = mm,
                    MaterialOverride = KitMaterial,
                    // Floors receive shadows but cast none (nothing is under them): keeps them out of the shadow pass.
                    CastShadow = group.Key.StartsWith("floor_", StringComparison.Ordinal)
                        ? GeometryInstance3D.ShadowCastingSetting.Off
                        : GeometryInstance3D.ShadowCastingSetting.On,
                });
                multimeshes++;
            }
        }

        foreach (var gate in plan.Gates)
        {
            var node = new Node3D { Name = gate.BoxId + "-gate", Transform = Placement(gate.Piece) };
            foreach (var (mesh, local) in MeshesOf(gate.Piece.Piece))
            {
                node.AddChild(new MeshInstance3D { Mesh = mesh, Transform = local, MaterialOverride = KitMaterial });
            }
            AddChild(node);
            _gates.Add((layout.Boxes.First(b => b.Id == gate.BoxId), node));
        }

        foreach (var l in plan.Lights)
        {
            var warden = l.Kind == LightKind.Warden;
            AddChild(new OmniLight3D
            {
                Name = l.BoxId + "-light",
                Position = new Vector3(l.X * MmToM, l.Y * MmToM, l.Z * MmToM),
                LightColor = warden ? WardenColor : TorchColor,
                LightEnergy = warden ? WardenEnergy : TorchEnergy,
                OmniRange = l.RangeMm * MmToM,
                OmniAttenuation = 1.2f,
                ShadowEnabled = false,
            });
        }

        GD.Print($"DUNGEON built boxes={boxes.Count} tiles={plan.Floor.Count} walls={plan.WallPieces.Count} " +
                 $"frames={plan.DoorFrames.Count} props={plan.Props.Count} decor={plan.WallDecor.Count} gates={plan.Gates.Count} " +
                 $"lights={plan.Lights.Count} instances={placed.Count} multimeshes={multimeshes}");
    }

    /// <summary>The run layout's boxes as <see cref="DungeonPlan"/> reads them (mm), in route order.</summary>
    /// <param name="layout">The run's layout.</param>
    /// <returns>Plan boxes.</returns>
    public static IReadOnlyList<PlanBox> PlanBoxesOf(NavLayout layout) =>
        layout.Boxes.Select(b => new PlanBox(b.Id, b.IsRoom,
            checked((int)b.Box.MinX), checked((int)b.Box.MaxX), checked((int)b.Box.MinZ), checked((int)b.Box.MaxZ),
            b.GatePullId is not null)).ToList();

    /// <summary>The run layout's route points (mm), one per box.</summary>
    /// <param name="layout">The run's layout.</param>
    /// <returns>Route points.</returns>
    public static IReadOnlyList<(int X, int Z)> RouteOf(NavLayout layout) =>
        layout.Route.Select(p => (checked((int)p.X), checked((int)p.Z))).ToList();

    /// <summary>
    /// The solid boxes the camera keeps out of and cuts away (CAMERA-FEEL): every wall piece, then every door-frame
    /// pillar, in the plan's order (occluder k here is occluder k in <see cref="CutAway"/>).
    /// </summary>
    /// <param name="layout">The run's layout.</param>
    /// <returns>Wall and pillar boxes, mm.</returns>
    public static IReadOnlyList<WallBox> OccluderBoxes(NavLayout layout)
    {
        var boxes = PlanBoxesOf(layout);
        return DungeonPlan.WallBoxes(boxes).Concat(DungeonPlan.FrameBoxes(boxes)).ToList();
    }

    /// <summary>
    /// Cuts away exactly the given occluders (wall pieces and pillars, <see cref="OccluderBoxes"/> indices) for this
    /// frame: each one's instances get a zero-scale transform, and any occluder cut last frame but not now gets its placed
    /// transform back. Floors, props, sconces, gates and lights are never touched. Returns false (and does nothing) when the
    /// kit shell is not built for <paramref name="layout"/> (GreyBox fallback, or a layout not built yet).
    /// </summary>
    /// <param name="layout">The layout the indices refer to.</param>
    /// <param name="occluders">Occluder indices to hide.</param>
    /// <returns>True when applied.</returns>
    public bool CutAway(NavLayout layout, IReadOnlySet<int> occluders)
    {
        if (!KitAvailable || !ReferenceEquals(layout, _built))
        {
            return false;
        }
        foreach (var k in _cut.Where(k => !occluders.Contains(k)).ToList())
        {
            foreach (var (mesh, index, placed) in _occluderSlots[k])
            {
                mesh.SetInstanceTransform(index, placed);
            }
            _cut.Remove(k);
        }
        foreach (var k in occluders)
        {
            if (k < 0 || k >= _occluderSlots.Count || !_cut.Add(k))
            {
                continue;
            }
            foreach (var (mesh, index, placed) in _occluderSlots[k])
            {
                mesh.SetInstanceTransform(index, new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), placed.Origin));
            }
        }
        return true;
    }

    // A floor tile as a kit piece: the tile rectangle, its slab top just under y 0, at the kit scale.
    private static KitPiece Tile(FloorTile t)
    {
        var n = DungeonPlan.Nominal[t.Piece];
        return new KitPiece(t.BoxId, t.Piece, (t.MinX + t.MaxX) / 2, DungeonPlan.FloorY(t.Piece), (t.MinZ + t.MaxZ) / 2, 0,
            t.MaxX - t.MinX, n.Y * DungeonPlan.KitScale / 100, t.MaxZ - t.MinZ);
    }

    private static Transform3D Placement(KitPiece p)
    {
        var n = DungeonPlan.Nominal[p.Piece];
        var scale = new Vector3((float)p.SizeX / n.X, (float)p.SizeY / n.Y, (float)p.SizeZ / n.Z);
        var basis = new Basis(Vector3.Up, p.QuarterTurns * Mathf.Pi / 2f) * Basis.FromScale(scale);
        return new Transform3D(basis, new Vector3(p.X * MmToM, p.Y * MmToM, p.Z * MmToM));
    }

    // Every mesh in a kit piece's imported scene, with its transform relative to the scene root (loaded once).
    private List<(Mesh Mesh, Transform3D Local)> MeshesOf(string piece)
    {
        if (_meshes.TryGetValue(piece, out var cached))
        {
            return cached;
        }
        var parts = new List<(Mesh, Transform3D)>();
        var scene = GD.Load<PackedScene>(KitDir + "/" + DungeonPlan.FileOf(piece));
        var root = scene?.Instantiate<Node3D>();
        if (root is not null)
        {
            Collect(root, Transform3D.Identity, parts, isRoot: true);
            ReportNominal(piece, parts);
            root.Free();
        }
        else
        {
            GD.PushError("DUNGEON missing kit piece " + piece);
        }
        _meshes[piece] = parts;
        return parts;
    }

    private static void Collect(Node node, Transform3D parent, List<(Mesh, Transform3D)> into, bool isRoot)
    {
        var here = parent;
        if (node is Node3D n3 && !isRoot)
        {
            here = parent * n3.Transform;
        }
        if (node is MeshInstance3D mi && mi.Mesh is not null)
        {
            into.Add((mi.Mesh, here));
        }
        foreach (var child in node.GetChildren())
        {
            Collect(child, here, into, isRoot: false);
        }
    }

    // ART-PLAN section 6: print the imported size so a wrong nominal (and so a wrong scale) shows in the log.
    private static void ReportNominal(string piece, List<(Mesh Mesh, Transform3D Local)> parts)
    {
        if (parts.Count == 0)
        {
            return;
        }
        var box = parts.Select(p => p.Local * p.Mesh.GetAabb()).Aggregate((a, b) => a.Merge(b));
        var n = DungeonPlan.Nominal[piece];
        var off = Mathf.Max(Mathf.Abs(box.Size.X * 1000f - n.X), Mathf.Max(Mathf.Abs(box.Size.Y * 1000f - n.Y), Mathf.Abs(box.Size.Z * 1000f - n.Z)));
        if (off > 25f)
        {
            GD.Print($"DUNGEON nominal-mismatch piece={piece} imported=({box.Size.X:F2},{box.Size.Y:F2},{box.Size.Z:F2}) nominal=({n.X},{n.Y},{n.Z})mm");
        }
    }

    // The frame budget (draw calls at 1280x720) measured from the renderer, printed a few times per run.
    private void ReportFrameCost()
    {
        _frames++;
        var calls = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _maxDrawCalls = Math.Max(_maxDrawCalls, calls);
        if (_frames == 120 || _frames % 1200 == 0)
        {
            var objects = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
            var prims = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
            GD.Print($"DUNGEON perf frame={_frames} drawcalls={calls} max={_maxDrawCalls} objects={objects} primitives={prims}");
        }
    }
}
