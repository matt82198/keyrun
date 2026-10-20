using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Core;

namespace Keyrun.Hud;

/// <summary>
/// The controls hint (Arena.tscn <c>Hud/Help</c>, "Hollow Vault - WASD move, ... Esc frees the mouse") as a run-start
/// cue rather than a permanent banner (lane HUD-TEXT-OVERLAYS, finding f-6917dd70: "reduce screen clutter"). It is
/// fully visible when a run starts, holds for <see cref="HoldTicks"/> of run time, or until the human Warden's first
/// completed cast (the first ability press), then fades over <see cref="FadeTicks"/> and stays faded for the rest of
/// the run. Run time is counted in sim steps while the label is visible, so it is deterministic under
/// <c>--fixed-fps</c> and does not run down while the between-runs screen hides the label (<see cref="Screen"/> owns
/// <c>Visible</c>; this node only drives <c>Modulate</c> alpha). There is no key to bring it back: no help key exists.
/// Under <c>--autorun</c> or <c>--replay-input</c> it prints <c>HUD help shown|fade|alpha|gone ...</c> proof lines
/// (HelpHintSceneTest.ps1); every alpha printed is read back from <c>Modulate</c>.
/// </summary>
public partial class HelpHint : Label
{
    /// <summary>Sim steps the hint holds at full alpha before it fades on its own (5 s at 20 Hz).</summary>
    public const int HoldTicks = 100;

    /// <summary>Sim steps the fade takes (1 s at 20 Hz).</summary>
    public const int FadeTicks = 20;

    private SimDriver? _driver;
    private int _shown;
    private int? _fadeShown;
    private bool _gone;

    /// <summary>The driver to read the run from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../SimDriver");

    /// <summary>
    /// The hint's alpha <paramref name="sinceFade"/> steps after its fade began: 1 before the fade, falling linearly to
    /// 0 at <see cref="FadeTicks"/>, 0 after (Godot-free).
    /// </summary>
    /// <param name="sinceFade">Steps since the fade began, or null before it began.</param>
    /// <returns>The alpha, [0, 1].</returns>
    public static float AlphaAt(int? sinceFade) => sinceFade is not { } k
        ? 1f
        : Math.Clamp(1f - (float)k / FadeTicks, 0f, 1f);

    /// <summary>True when one step's events hold the human Warden's completed cast (an ability press landed).</summary>
    /// <param name="events">The step's events.</param>
    /// <returns>True for a <c>cast_complete</c> with <c>actorId</c> = <see cref="SimDriver.WardenId"/>.</returns>
    public static bool WardenCast(IReadOnlyList<SimEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            if (e.Type == "cast_complete" && e.Get("actorId") is JsInt id && id.Value == SimDriver.WardenId)
            {
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _driver = HudNodes.FindDriver(this, DriverPath);
        if (_driver is null)
        {
            return;
        }
        _driver.Stepped += OnStepped;
        _driver.RunStarted += OnRunStarted;
        if (_driver.Run is not null)
        {
            OnRunStarted();
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

    private void OnRunStarted()
    {
        _shown = 0;
        _fadeShown = null;
        _gone = false;
        SetAlpha(1f);
        Proof(FormattableString.Invariant(
            $"HUD help shown tick={_driver?.Run?.State.Tick ?? 0} alpha={Modulate.A:0.00} hold={HoldTicks} fade={FadeTicks}"));
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (_gone || !IsVisibleInTree())
        {
            return;
        }
        _shown++;
        if (_fadeShown is not { } from)
        {
            var cast = WardenCast(events);
            if (cast || _shown >= HoldTicks)
            {
                _fadeShown = _shown;
                Proof(FormattableString.Invariant($"HUD help fade tick={tick} shown={_shown} reason={(cast ? "cast" : "time")}"));
            }
            return;
        }
        SetAlpha(AlphaAt(_shown - from));
        Proof(FormattableString.Invariant($"HUD help alpha tick={tick} shown={_shown} a={Modulate.A:0.00}"));
        if (Modulate.A <= 0f)
        {
            _gone = true;
            Proof(FormattableString.Invariant($"HUD help gone tick={tick} shown={_shown} alpha={Modulate.A:0.00}"));
        }
    }

    private void SetAlpha(float alpha) => Modulate = new Color(Modulate, alpha);

    // The proof lines print only in the scripted and replayed runs the scene test drives.
    private void Proof(string line)
    {
        if (_driver is { } d && (d.Options.Autorun || d.Options.ReplayInput is not null))
        {
            GD.Print(line);
        }
    }
}
