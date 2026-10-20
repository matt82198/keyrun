using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Hud;

/// <summary>
/// The Twins' one shared Break bar on the boss unit frame (BIBLE s9 mechanic 2, "share one Break bar"; BREAK-BAR,
/// HUD-DECLUTTER: on the boss frame, never over a model). <see cref="UnitFrames"/> grows the boss frame by
/// <see cref="RowHeight"/> once <see cref="BossBreakBarReadout.For"/> has a bar for its boss, and this control draws
/// <see cref="BreakBarRule.For"/> into that row: break progress filling amber as interrupts and controls on either
/// twin drain the sim bar, a full gold bar with a white flash the tick it breaks (the Twins are staggered), held full
/// until the sim refills it, then empty again. Reads the sim state only.
/// </summary>
public partial class BreakBar : Control
{
    /// <summary>Height a boss frame grows by for its Break bar row, pixels.</summary>
    public const float RowHeight = 16;

    private const int LabelFont = 10;
    private const string ShotEnv = "KEYRUN_BREAKBAR_SHOT";

    private SimDriver? _driver;
    private UnitFrames? _units;
    private string? _lastLogged;
    private readonly HashSet<string> _shots = new(StringComparer.Ordinal);

    /// <summary>The driver to read the run from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>The unit frames whose boss frame holds the bar.</summary>
    [Export]
    public NodePath UnitFramesPath { get; set; } = new("../UnitFrames");

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _driver = HudNodes.FindDriver(this, DriverPath);
        _units = GetNodeOrNull<UnitFrames>(UnitFramesPath);
        if (_driver is not null)
        {
            _driver.Stepped += OnStepped;
            _driver.RunStarted += OnRunStarted;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_driver is not null)
        {
            _driver.Stepped -= OnStepped;
            _driver.RunStarted -= OnRunStarted;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta) => QueueRedraw();

    /// <inheritdoc />
    public override void _Draw()
    {
        if (Current() is not { } now || _units?.BossBreakRow is not { } row)
        {
            return;
        }
        var look = now.Look;
        var rect = new Rect2(row.Position + (_units.GetGlobalRect().Position - GetGlobalRect().Position), row.Size);
        DrawRect(rect, new Color(0.05f, 0.05f, 0.07f, 0.85f));
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X * look.FillPermille / 1000f, rect.Size.Y)), HudNodes.Rgb(look.Color, 0.95f));
        if (look.FlashAlphaPermille > 0)
        {
            DrawRect(rect, new Color(1f, 1f, 1f, look.FlashAlphaPermille / 1000f));
        }
        DrawRect(rect, HudNodes.Rgb(look.Phase == BreakBarPhase.Broken ? BreakBarRule.BrokenColor : BreakBarRule.FillColor), filled: false, width: 1f);
        var label = look.Phase == BreakBarPhase.Broken
            ? "BROKEN"
            : FormattableString.Invariant($"BREAK {look.FillPermille / 10}%");
        HudNodes.Text(this, new Vector2(rect.Position.X + 4, rect.End.Y - 2), label, LabelFont, Colors.White);
    }

    private (int BossId, BossBreakBar Bar, BreakBarLook Look)? Current()
    {
        if (_driver?.Run is not { } run)
        {
            return null;
        }
        foreach (var frame in UnitFrameModel.Build(run.State, _driver.Setup?.HumanActorId ?? SimDriver.WardenId))
        {
            if (frame.Kind != "boss" || BossBreakBarReadout.For(run.Encounter, frame.ActorId) is not { } bar)
            {
                continue;
            }
            var look = BreakBarRule.For(bar.Bar.Current, bar.Bar.Max, bar.Bar.CooldownReadyTick, run.State.Tick, Keyrun.Sim.Encounter.BreakBar.ResetCooldownTicks);
            return (frame.ActorId, bar, look);
        }
        return null;
    }

    private void OnRunStarted()
    {
        _lastLogged = null;
        _shots.Clear();
    }

    // Proof line (autorun only), whenever the bar's value or phase changes and on its first tick:
    // "HUD breakbar boss=B bar=ID tick=T current=C max=M fill=F phase=filling|broken".
    // KEYRUN_BREAKBAR_SHOT=DIR (rendering window only) also saves breakbar-fill.png the first tick fill is above 0 and
    // breakbar-broken.png the tick it breaks.
    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (_driver is null || !_driver.Options.Autorun || Current() is not { } now)
        {
            return;
        }
        var phase = now.Look.Phase == BreakBarPhase.Broken ? "broken" : "filling";
        var key = FormattableString.Invariant($"{now.Bar.BarId}:{now.Bar.Bar.Current}:{phase}");
        if (key != _lastLogged)
        {
            _lastLogged = key;
            HudNodes.Proof(_driver, FormattableString.Invariant(
                $"HUD breakbar boss={now.BossId} bar={now.Bar.BarId} tick={_driver.State.Tick} current={now.Bar.Bar.Current} max={now.Bar.Bar.Max} fill={now.Look.FillPermille} phase={phase}"));
        }
        if (now.Look.FillPermille > 0)
        {
            MaybeShot(now.Look.Phase == BreakBarPhase.Broken ? "breakbar-broken.png" : "breakbar-fill.png");
        }
    }

    private void MaybeShot(string name)
    {
        if (System.Environment.GetEnvironmentVariable(ShotEnv) is not { Length: > 0 } dir ||
            DisplayServer.GetName() == "headless" || !_shots.Add(name))
        {
            return;
        }
        Directory.CreateDirectory(dir);
        CaptureAfterDraw(Path.Combine(dir, name));
    }

    private async void CaptureAfterDraw(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"HUD breakbar shot {path} {(err == Error.Ok ? "ok" : "error " + err)}");
    }
}
