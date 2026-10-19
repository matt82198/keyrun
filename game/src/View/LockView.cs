using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.View;

/// <summary>
/// The Lock on the Hollow Warden as a floor ring at the boss's feet (LOCK-VIEW; BIBLE s9 "The Lock"). View-only: every
/// frame it reads the sim's <see cref="EncounterState.Boss"/> through <see cref="SimDriver.Run"/> and the tick of the
/// last <c>lock_broken</c> / <c>lock_failed</c> from <see cref="SimDriver.Stepped"/>, asks <see cref="LockRule.For"/>
/// for the look, and draws it: violet while a Lock Window is open, shrinking from 4.0 m to 1.5 m as the Window runs
/// out (a faint ring stays at 4.0 m so the shrink reads), a green flash when the party breaks it, a red flash when it
/// closes unbroken, and a dim red ring under everything once the boss has enraged. Nothing it does feeds the sim.
/// </summary>
public partial class LockView : Node3D
{
    private const float FloorLift = 0.05f;
    private const float RingWidthM = 0.22f;
    private const float EnrageWidthM = 0.14f;
    private const float GuideWidthM = 0.06f;
    private const float GuideAlpha = 0.22f;
    private const int Segments = 64;

    private ImmediateMesh? _mesh;
    private int _brokenTick = -1;
    private int _failedTick = -1;
    private LockPhase _lastPhase = LockPhase.Idle;

    /// <summary>The driver to read the run and its events from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    private SimDriver? Driver { get; set; }

    /// <summary>A 0xRRGGBB colour from the rule at an opacity in permille.</summary>
    /// <param name="rgb">The colour, 0xRRGGBB.</param>
    /// <param name="alphaPermille">Opacity, 0..1000.</param>
    /// <returns>The Godot colour.</returns>
    public static Color ToColor(int rgb, int alphaPermille) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alphaPermille / 1000f);

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

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Driver is null || _mesh is null)
        {
            return;
        }
        _mesh.ClearSurfaces();
        if (Driver.Run?.Encounter.Boss is not { } boss)
        {
            return;
        }
        var state = Driver.State;
        var bossId = boss.Config.BossActorId;
        if (!state.Actors.TryGetValue(bossId, out var actor) || !actor.Alive)
        {
            return;
        }

        var tick = state.Tick;
        var window = boss.Config.LockWindowTicks;
        // The sim's Active is the truth: it is still set on its EndsTick (the close happens in that tick's step), which
        // reads as 0 ticks left rather than a one-frame gap before the fail flash.
        var open = boss.Active is not null;
        var left = open ? Math.Max(0, boss.Active!.EndsTick - tick) : 0;
        var look = LockRule.For(
            open,
            left,
            window,
            boss.LocksBroken,
            boss.LockFails,
            boss.Enraged,
            _brokenTick < 0 ? -1 : tick - _brokenTick,
            _failedTick < 0 ? -1 : tick - _failedTick);
        if (look.Phase != _lastPhase)
        {
            // Stdout only (never the run log or any digest): names each phase change so a capture can be matched to it.
            GD.Print(FormattableString.Invariant($"LOCK view phase={look.Phase} tick={tick} radiusMm={look.RadiusMm} opened={boss.LocksOpened} broken={boss.LocksBroken} fails={boss.LockFails}"));
            _lastPhase = look.Phase;
        }
        if (look.AlphaPermille <= 0 && !look.EnrageRing)
        {
            return;
        }

        var before = Driver.PreviousState.Actors.TryGetValue(bossId, out var prev) ? prev : actor;
        var centre = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), Driver.Alpha);

        if (look.EnrageRing && look.Phase != LockPhase.Enraged)
        {
            Ring(centre, LockRule.EnrageRadiusMm * 0.001f, EnrageWidthM, ToColor(LockRule.EnrageColor, LockRule.EnrageAlphaPermille));
        }
        var radius = look.RadiusMm * 0.001f;
        var width = look.Phase == LockPhase.Enraged ? EnrageWidthM : RingWidthM;
        if (look.Phase == LockPhase.Open)
        {
            // Ease between this tick's radius and the next one's so the shrink is smooth at 20 Hz.
            var next = LockRule.OpenRadiusMm(left - 1, window) * 0.001f;
            radius = Mathf.Lerp(radius, next, Driver.Alpha);
            Ring(centre, LockRule.OpenStartRadiusMm * 0.001f, GuideWidthM, ToColor(look.Color, (int)(GuideAlpha * 1000)));
        }
        if (look.AlphaPermille > 0)
        {
            Ring(centre, radius, width, ToColor(look.Color, look.AlphaPermille));
        }
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        foreach (var e in events)
        {
            if (e.Type == "lock_broken")
            {
                _brokenTick = e.Tick;
            }
            else if (e.Type == "lock_failed")
            {
                _failedTick = e.Tick;
            }
        }
    }

    private void OnRunStarted()
    {
        _brokenTick = -1;
        _failedTick = -1;
        _lastPhase = LockPhase.Idle;
    }

    private void Ring(Vector3 centre, float radius, float width, Color color)
    {
        _mesh!.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        for (var i = 0; i <= Segments; i++)
        {
            var a = Mathf.Tau * i / Segments;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            _mesh.SurfaceSetColor(color);
            _mesh.SurfaceAddVertex(centre + (dir * (radius - (width / 2))) + new Vector3(0, FloorLift, 0));
            _mesh.SurfaceSetColor(color);
            _mesh.SurfaceAddVertex(centre + (dir * (radius + (width / 2))) + new Vector3(0, FloorLift, 0));
        }
        _mesh.SurfaceEnd();
    }
}
