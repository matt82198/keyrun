using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// Window rings on the floor (BIBLE s4, Setups open Windows that Payoffs close). A target or self Window rides its
/// carrier as a synthetic effect (<c>window:&lt;id&gt;:&lt;setupClass&gt;</c>, <see cref="WindowState"/>); it draws as
/// a ring around that actor's feet. A floor-patch Window (<see cref="SimState.FloorWindows"/>, e.g. Bulwark) draws as a
/// ring of its own radius at its centre. The ring's lit arc is the time left: it starts full when the Window opens and
/// sweeps down to nothing as it expires. The ring is a colour band (BIBLE s4: "The Window ring shows which classes'
/// Payoffs can close it by colour band"): one equal segment per class whose Payoff can close it, in that class's colour
/// (<see cref="WindowBandRule"/>, read from the loaded content's classes, Payoff abilities and combo rows). The lit
/// arc keeps every segment as it shrinks, so the closers stay readable until the Window expires. A Window with no
/// closer falls back to its id colour (<see cref="ColorOf"/>); a Lock Window is drawn thicker in the fixed Lock violet.
/// </summary>
public partial class WindowRingView : Node3D
{
    private const float FloorLift = 0.04f;
    private const float ActorRingRadiusM = 0.85f;
    private const float RingWidthM = 0.12f;
    private const int Segments = 48;

    private readonly Dictionary<string, int> _openedAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _stale = new();
    private readonly Dictionary<(string SetupClass, bool Floor), IReadOnlyList<WindowBandSegment>> _bands = new();
    private ImmediateMesh? _mesh;

    /// <summary>The driver to read state from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    private SimDriver? Driver { get; set; }

    /// <summary>The colour a Window id draws in (stable per id).</summary>
    /// <param name="windowId">The window id.</param>
    /// <returns>The colour.</returns>
    public static Color ColorOf(string windowId) => windowId switch
    {
        "sundered" => new Color(0.95f, 0.35f, 0.2f, 0.85f),
        "bulwark" => new Color(0.3f, 0.7f, 1f, 0.75f),
        _ => Color.FromHsv((StableHash(windowId) % 360) / 360f, 0.65f, 0.95f, 0.8f),
    };

    /// <inheritdoc />
    public override void _Ready()
    {
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        _mesh = new ImmediateMesh();
        AddChild(new MeshInstance3D
        {
            Mesh = _mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                VertexColorUseAsAlbedo = true,
            },
        });
        if (Driver is not null)
        {
            Driver.RunStarted += OnRunStarted;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (Driver is not null)
        {
            Driver.RunStarted -= OnRunStarted;
        }
    }

    private void OnRunStarted()
    {
        _openedAt.Clear();
        _bands.Clear();
    }

    // The band for a Window of this setup class (cached; content is fixed for a run). Empty when no content is loaded.
    private IReadOnlyList<WindowBandSegment> BandOf(string setupClass, bool floor)
    {
        if (Driver?.Registry is not { } registry)
        {
            return Array.Empty<WindowBandSegment>();
        }
        if (!_bands.TryGetValue((setupClass, floor), out var band))
        {
            band = WindowBandRule.Band(WindowBandRule.ClosingClasses(registry, setupClass, floor));
            _bands[(setupClass, floor)] = band;
        }
        return band;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Driver is null || _mesh is null)
        {
            return;
        }
        _mesh.ClearSurfaces();
        _seen.Clear();
        var state = Driver.State;
        var now = state.Tick + Driver.Alpha;
        foreach (var (id, actor) in state.Actors)
        {
            if (!actor.Alive)
            {
                continue;
            }
            var before = Driver.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : actor;
            var centre = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), Driver.Alpha);
            var ring = 0;
            foreach (var e in actor.Effects)
            {
                if (e.EndsTick <= state.Tick || WindowState.ParseWindowEffectId(e.EffectId) is not { } parsed)
                {
                    continue;
                }
                var key = FormattableString.Invariant($"a{id}:{e.EffectId}:{e.EndsTick}");
                var left = Remaining(key, state.Tick, e.EndsTick, now);
                var width = parsed.Alternate ? RingWidthM * 2 : RingWidthM;
                // LOCK-VIEW: a Lock Window draws in the fixed Lock violet (LockRule.OpenColor, LockView's ring), not its id's hash.
                var radius = ActorRingRadiusM + (ring * 0.2f);
                if (parsed.Alternate)
                {
                    DrawRing(centre, radius, width, left, LockView.ToColor(LockRule.OpenColor, 850));
                }
                else
                {
                    DrawBand(centre, radius, width, left, BandOf(parsed.SetupClass, floor: false), 850, ColorOf(parsed.WindowId));
                }
                ring++;
            }
        }
        foreach (var (windowId, patch) in state.FloorWindows)
        {
            if (patch.EndsTick <= state.Tick)
            {
                continue;
            }
            var key = FormattableString.Invariant($"f{windowId}:{patch.SetupActorId}:{patch.EndsTick}");
            var left = Remaining(key, state.Tick, patch.EndsTick, now);
            DrawBand(ActorView.ToMetres(patch.Pos), patch.RadiusMm * 0.001f, RingWidthM * 1.5f, left, BandOf(patch.SetupClass, floor: true), 750, ColorOf(windowId));
        }

        _stale.Clear();
        foreach (var key in _openedAt.Keys)
        {
            if (!_seen.Contains(key))
            {
                _stale.Add(key);
            }
        }
        foreach (var key in _stale)
        {
            _openedAt.Remove(key);
        }
    }

    // Fraction of the Window's life left at the displayed fractional tick; its life starts the first tick it is seen.
    private float Remaining(string key, int tick, int endsTick, float now)
    {
        _seen.Add(key);
        if (!_openedAt.TryGetValue(key, out var opened))
        {
            opened = tick;
            _openedAt[key] = opened;
        }
        var life = Mathf.Max(1f, endsTick - opened);
        return Mathf.Clamp((endsTick - now) / life, 0f, 1f);
    }

    private void DrawRing(Vector3 centre, float radius, float width, float fraction, Color color)
    {
        if (fraction <= 0f)
        {
            return;
        }
        // A faint full ring underneath, then the lit arc of the time left, starting at 12 o'clock.
        Strip(centre, radius, width * 0.6f, 0f, 1f, new Color(color, color.A * 0.25f));
        Strip(centre, radius, width, 0f, fraction, color);
    }

    // The colour band: each closer's segment on the faint full ring, and the same segments scaled into the lit arc so
    // every closer stays on the ring while the time left shrinks. No closer: a plain ring in the fallback colour.
    private void DrawBand(Vector3 centre, float radius, float width, float fraction, IReadOnlyList<WindowBandSegment> band, int alphaPermille, Color fallback)
    {
        if (band.Count == 0)
        {
            DrawRing(centre, radius, width, fraction, fallback);
            return;
        }
        if (fraction <= 0f)
        {
            return;
        }
        foreach (var seg in band)
        {
            var color = LockView.ToColor(seg.Color, alphaPermille);
            var from = seg.StartPermille / 1000f;
            var to = seg.EndPermille / 1000f;
            Strip(centre, radius, width * 0.6f, from, to, new Color(color, color.A * 0.25f));
            Strip(centre, radius, width, from * fraction, to * fraction, color);
        }
    }

    // A ring strip from fraction `from` to `to` of a full turn, clockwise from 12 o'clock.
    private void Strip(Vector3 centre, float radius, float width, float from, float to, Color color)
    {
        var span = to - from;
        if (span <= 0f)
        {
            return;
        }
        var steps = Math.Max(2, (int)Mathf.Ceil(Segments * span));
        var start = (-Mathf.Pi / 2) + (Mathf.Tau * from);
        var sweep = Mathf.Tau * span;
        _mesh!.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        for (var i = 0; i <= steps; i++)
        {
            var a = start + (sweep * i / steps);
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            _mesh.SurfaceSetColor(color);
            _mesh.SurfaceAddVertex(centre + (dir * (radius - (width / 2))) + new Vector3(0, FloorLift, 0));
            _mesh.SurfaceSetColor(color);
            _mesh.SurfaceAddVertex(centre + (dir * (radius + (width / 2))) + new Vector3(0, FloorLift, 0));
        }
        _mesh.SurfaceEnd();
    }

    private static uint StableHash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s)
        {
            h = (h ^ c) * 16777619u;
        }
        return h;
    }
}
