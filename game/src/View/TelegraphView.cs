using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.View;

/// <summary>
/// Draws the sim's pending telegraphs on the floor. For each telegraph id SimDriver reports it spawns
/// one Telegraph.tscn instance: a faint full-size outline plus a red fill that grows from the origin
/// as the lead time runs out. When the sim's <c>telegraph_hit</c> event for that id arrives it flashes
/// bright and fades, then frees the instance; a <c>telegraph_cancel</c> (the cast was kicked, stopped or replaced, so
/// nothing lands) frees it at once with no flash (<see cref="TelegraphEnd"/>). A cone draws its own angle and a circle the full turn
/// (<see cref="TelegraphFan"/>); a line still draws as its cone stand-in until it gets its own mesh.
/// </summary>
public partial class TelegraphView : Node3D
{
    private const float MmToM = 0.001f;
    private const float FloorLift = 0.03f;
    private const float FlashSeconds = 0.15f;
    private const float FadeSeconds = 0.65f;
    private const int ArcSegments = 32;

    private static readonly Color FillColor = new(0.9f, 0.08f, 0.05f, 0.5f);
    private static readonly Color OutlineColor = new(0.9f, 0.1f, 0.05f, 0.16f);
    private static readonly Color FlashColor = new(1f, 0.55f, 0.4f, 0.95f);

    private readonly Dictionary<int, Shown> _shown = new();
    private readonly List<int> _scratch = new();
    private readonly Dictionary<(int Source, int Due), int> _leadStart = new();

    /// <summary>The driver to read telegraphs and events from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    /// <summary>The per-telegraph scene (Outline and Fill MeshInstance3D children).</summary>
    [Export]
    public PackedScene? TelegraphScene { get; set; }

    private SimDriver? Driver { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        if (Driver is not null)
        {
            Driver.Stepped += OnStepped;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (Driver is not null)
        {
            Driver.Stepped -= OnStepped;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Driver is null || TelegraphScene is null)
        {
            return;
        }
        foreach (var tg in Driver.ActiveTelegraphs)
        {
            if (!_shown.ContainsKey(tg.Id))
            {
                _shown[tg.Id] = Spawn(tg);
            }
        }

        var now = Driver.State.Tick + Driver.Alpha; // fractional tick being displayed
        _scratch.Clear();
        foreach (var (id, s) in _shown)
        {
            if (s.ResolvedAge is { } age)
            {
                age += (float)delta;
                s.ResolvedAge = age;
                s.Fill.Scale = Vector3.One;
                var c = age < FlashSeconds
                    ? FlashColor
                    : FillColor.Lerp(new Color(FillColor, 0f), Mathf.Clamp((age - FlashSeconds) / FadeSeconds, 0f, 1f));
                s.FillMaterial.AlbedoColor = c;
                s.OutlineMaterial.AlbedoColor = new Color(OutlineColor, OutlineColor.A * (1f - Mathf.Clamp(age / (FlashSeconds + FadeSeconds), 0f, 1f)));
                if (age >= FlashSeconds + FadeSeconds)
                {
                    _scratch.Add(id);
                }
            }
            else
            {
                var p = Mathf.Clamp((now - s.StartTick) / Mathf.Max(1f, s.ResolvesTick - s.StartTick), 0.02f, 1f);
                s.Fill.Scale = new Vector3(p, 1f, p);
            }
        }
        foreach (var id in _scratch)
        {
            _shown[id].Root.QueueFree();
            _shown.Remove(id);
        }
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        foreach (var e in events)
        {
            if (e.Type == "telegraph" && e.Get("actorId") is JsInt src && e.Get("resolvesTick") is JsInt due)
            {
                _leadStart[((int)src.Value, (int)due.Value)] = e.Tick;
            }
            else if (TelegraphEnd.Of(e.Type) is var end && end != TelegraphEndKind.None &&
                     e.Get("telegraphId") is JsInt id && _shown.TryGetValue((int)id.Value, out var s))
            {
                if (end == TelegraphEndKind.Flash)
                {
                    s.ResolvedAge ??= 0f;
                }
                else
                {
                    // TELEGRAPH-CANCEL: the cast was kicked, stopped or replaced; nothing lands, so no flash.
                    s.Root.QueueFree();
                    _shown.Remove((int)id.Value);
                }
            }
        }
    }

    private Shown Spawn(Telegraph tg)
    {
        var root = TelegraphScene!.Instantiate<Node3D>();
        AddChild(root);
        root.Position = new Vector3(tg.Origin.X * MmToM, FloorLift, tg.Origin.Z * MmToM);
        root.Rotation = new Vector3(0, -tg.Yaw * Mathf.Tau / 1024f, 0); // local +x = sim facing
        var radius = (tg.Radius ?? 0) * MmToM;
        var halfAngle = TelegraphFan.Span1024(tg.Shape, tg.Angle1024) / 2f * Mathf.Tau / TelegraphFan.FullTurn;
        var mesh = BuildCone(radius, halfAngle);

        var outline = root.GetNode<MeshInstance3D>("Outline");
        var fill = root.GetNode<MeshInstance3D>("Fill");
        var outlineMat = NewMaterial(OutlineColor);
        var fillMat = NewMaterial(FillColor);
        outline.Mesh = mesh;
        outline.MaterialOverride = outlineMat;
        fill.Mesh = mesh;
        fill.MaterialOverride = fillMat;
        fill.Position = new Vector3(0, 0.005f, 0);
        // The registry record carries no lead time; the "telegraph" event that registered it does.
        var key = (tg.SourceId, tg.ResolvesTick);
        var start = _leadStart.TryGetValue(key, out var t0) ? t0 : Driver!.State.Tick - 1;
        _leadStart.Remove(key);
        return new Shown(root, fill, fillMat, outlineMat, start, tg.ResolvesTick);
    }

    private static StandardMaterial3D NewMaterial(Color c) => new()
    {
        AlbedoColor = c,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
    };

    // A flat fan in the local XZ plane: apex at the origin, arc of the given radius from -half to +half
    // around local +x. Local point (r cos a, 0, r sin a) lands at world angle theta + a under the
    // root's -theta Y rotation, matching Telegraphs.PointInCone's facing.
    private static ArrayMesh BuildCone(float radius, float halfAngle)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        for (var i = 0; i < ArcSegments; i++)
        {
            var a0 = -halfAngle + (2 * halfAngle * i / ArcSegments);
            var a1 = -halfAngle + (2 * halfAngle * (i + 1) / ArcSegments);
            st.AddVertex(Vector3.Zero);
            st.AddVertex(new Vector3(radius * Mathf.Cos(a0), 0, radius * Mathf.Sin(a0)));
            st.AddVertex(new Vector3(radius * Mathf.Cos(a1), 0, radius * Mathf.Sin(a1)));
        }
        return st.Commit();
    }

    private sealed class Shown
    {
        public Shown(Node3D root, MeshInstance3D fill, StandardMaterial3D fillMaterial, StandardMaterial3D outlineMaterial, int startTick, int resolvesTick)
        {
            Root = root;
            Fill = fill;
            FillMaterial = fillMaterial;
            OutlineMaterial = outlineMaterial;
            StartTick = startTick;
            ResolvesTick = resolvesTick;
        }

        public Node3D Root { get; }

        public MeshInstance3D Fill { get; }

        public StandardMaterial3D FillMaterial { get; }

        public StandardMaterial3D OutlineMaterial { get; }

        public int StartTick { get; }

        public int ResolvesTick { get; }

        public float? ResolvedAge { get; set; }
    }
}
