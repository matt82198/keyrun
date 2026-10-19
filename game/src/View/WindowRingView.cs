using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// Window rings on the floor (BIBLE s4, Setups open Windows that Payoffs close). A target or self Window rides its
/// carrier as a synthetic effect (<c>window:&lt;id&gt;:&lt;setupClass&gt;</c>, <see cref="WindowState"/>); it draws as
/// a ring around that actor's feet. A floor-patch Window (<see cref="SimState.FloorWindows"/>, e.g. Bulwark) draws as a
/// ring of its own radius at its centre. The ring's lit arc is the time left: it starts full when the Window opens and
/// sweeps down to nothing as it expires. Colour is per window id; a Lock Window is drawn thicker.
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
            Driver.RunStarted += _openedAt.Clear;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (Driver is not null)
        {
            Driver.RunStarted -= _openedAt.Clear;
        }
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
                DrawRing(centre, ActorRingRadiusM + (ring * 0.2f), width, left, parsed.Alternate ? LockView.ToColor(Keyrun.Content.LockRule.OpenColor, 850) : ColorOf(parsed.WindowId));
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
            DrawRing(ActorView.ToMetres(patch.Pos), patch.RadiusMm * 0.001f, RingWidthM * 1.5f, left, ColorOf(windowId));
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
        Strip(centre, radius, width * 0.6f, 1f, new Color(color, color.A * 0.25f));
        Strip(centre, radius, width, fraction, color);
    }

    private void Strip(Vector3 centre, float radius, float width, float fraction, Color color)
    {
        var steps = Math.Max(2, (int)Mathf.Ceil(Segments * fraction));
        var sweep = Mathf.Tau * fraction;
        _mesh!.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        for (var i = 0; i <= steps; i++)
        {
            var a = (-Mathf.Pi / 2) + (sweep * i / steps);
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
