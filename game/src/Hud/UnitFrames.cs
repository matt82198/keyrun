using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using PixelRect = Keyrun.Content.PixelRect;

namespace Keyrun.Hud;

/// <summary>One enemy unit frame (Godot-free): which frame, and the enemy it shows.</summary>
/// <param name="Kind">"target" (the human's current target) or "boss" (the engaged boss).</param>
/// <param name="ActorId">The enemy's actor id.</param>
public sealed record UnitFrame(string Kind, int ActorId);

/// <summary>
/// Which enemy unit frames show (HUD-DECLUTTER, Matt 2026-10-19: "enemy health does not need to be overhead, they can
/// have unitframes but not just a random number"): a boss frame while a live boss (class id ending ".boss") is in the
/// run, then a target frame for the human's current target when it is a live enemy other than that boss (the boss
/// frame already shows it). Display only.
/// </summary>
public static class UnitFrameModel
{
    /// <summary>The frames to draw, top to bottom.</summary>
    /// <param name="state">The sim state.</param>
    /// <param name="humanActorId">The played character's actor id.</param>
    /// <returns>The frames.</returns>
    public static IReadOnlyList<UnitFrame> Build(SimState state, int humanActorId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var frames = new List<UnitFrame>();
        int? bossId = null;
        foreach (var (id, actor) in state.Actors)
        {
            if (actor.Alive && actor.Kind != ActorKind.Player && actor.ClassId.EndsWith(".boss", StringComparison.Ordinal))
            {
                bossId = id;
                frames.Add(new UnitFrame("boss", id));
                break;
            }
        }
        if (state.Actors.TryGetValue(humanActorId, out var me) && me.TargetId is { } targetId && targetId != bossId &&
            state.Actors.TryGetValue(targetId, out var target) && target.Alive && target.Kind != ActorKind.Player)
        {
            frames.Add(new UnitFrame("target", targetId));
        }
        return frames;
    }
}

/// <summary>
/// The enemy unit frames (HUD-DECLUTTER), down the right edge under the Slack bar: the enemy's name, a health bar with
/// its health, and its live cast as a bar under the health (amber frame when interruptible, grey when not), so the
/// enemy's cast reads on its frame as well as over its head (BIBLE s3, "Cast bars are the enemy's language"). Reads
/// the sim state only.
/// </summary>
public partial class UnitFrames : Control
{
    private const float FrameWidth = 260;
    private const float FrameHeight = 60;
    private const float Gap = 8;
    private const float RightGutter = 16;
    private const float Top = 124;
    private const int NameFont = 15;
    private const int SmallFont = 11;

    private static readonly Color HealthFill = new(0.78f, 0.22f, 0.2f, 1f);
    private static readonly Color TargetEdge = new(1f, 0.92f, 0.35f, 1f);
    private static readonly Color BossEdge = new(0.62f, 0.3f, 0.85f, 1f);
    private static readonly Color CastFill = new(0.95f, 0.72f, 0.2f, 0.95f);
    private static readonly Color KickFrame = new(1f, 0.6f, 0.1f, 1f);
    private static readonly Color LockedFrame = new(0.55f, 0.55f, 0.6f, 1f);

    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _castLogged = new(StringComparer.Ordinal);
    private SimDriver? _driver;

    /// <summary>The driver to read the run from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>This frame's unit frames in viewport pixels, a HUD obstacle plates and over-head cast bars keep clear of.</summary>
    public IReadOnlyList<PixelRect> FrameRects
    {
        get
        {
            var rects = new List<PixelRect>();
            if (_driver is null || !Visible)
            {
                return rects;
            }
            var origin = GetGlobalRect().Position;
            var frames = UnitFrameModel.Build(_driver.State, HumanId);
            for (var i = 0; i < frames.Count; i++)
            {
                var r = FrameRect(i);
                rects.Add(new PixelRect((int)(origin.X + r.Position.X), (int)(origin.Y + r.Position.Y), (int)r.Size.X, (int)r.Size.Y));
            }
            return rects;
        }
    }

    private int HumanId => _driver?.Setup?.HumanActorId ?? SimDriver.WardenId;

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _driver = HudNodes.FindDriver(this, DriverPath);
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
        if (_driver?.Run is not { } run)
        {
            return;
        }
        var frames = UnitFrameModel.Build(run.State, HumanId);
        for (var i = 0; i < frames.Count; i++)
        {
            if (run.State.Actors.TryGetValue(frames[i].ActorId, out var actor))
            {
                DrawFrame(FrameRect(i), frames[i], actor, run);
            }
        }
    }

    private Rect2 FrameRect(int index) =>
        new(Size.X - RightGutter - FrameWidth, Top + (index * (FrameHeight + Gap)), FrameWidth, FrameHeight);

    private void DrawFrame(Rect2 rect, UnitFrame frame, ActorState actor, Run run)
    {
        DrawRect(rect, HudNodes.Back);
        DrawRect(rect, frame.Kind == "boss" ? BossEdge : TargetEdge, filled: false, width: 2f);
        HudNodes.Text(this, rect.Position + new Vector2(8, 18), _driver!.DisplayName(actor), NameFont, Colors.White);

        var hp = new Rect2(rect.Position.X + 8, rect.Position.Y + 24, rect.Size.X - 16, 13);
        var frac = actor.MaxHealth <= 0 ? 0f : Mathf.Clamp((float)actor.Health / actor.MaxHealth, 0f, 1f);
        DrawRect(hp, new Color(0, 0, 0, 0.5f));
        DrawRect(new Rect2(hp.Position, new Vector2(hp.Size.X * frac, hp.Size.Y)), HealthFill);
        DrawString(ThemeDB.FallbackFont, new Vector2(hp.Position.X + 4, hp.End.Y - 2),
            FormattableString.Invariant($"{actor.Health} / {actor.MaxHealth}"), HorizontalAlignment.Left, -1, SmallFont, Colors.White);

        if (actor.CastId is not { } castId || actor.CastStartTick is not { } start || actor.CastEndsTick is not { } end)
        {
            return;
        }
        var now = run.State.Tick + _driver.Alpha;
        var progress = Mathf.Clamp((now - start) / Mathf.Max(1f, end - start), 0f, 1f);
        var secondsLeft = Mathf.Max(0f, (end - now) / Tick.TickHz);
        var cast = new Rect2(hp.Position.X, hp.End.Y + 4, hp.Size.X, 15);
        var interruptible = run.Content.Abilities.TryGetValue(castId, out var def) && def.Interruptible;
        DrawRect(cast, new Color(0.05f, 0.05f, 0.07f, 0.85f));
        DrawRect(new Rect2(cast.Position, new Vector2(cast.Size.X * progress, cast.Size.Y)), CastFill);
        DrawRect(cast, interruptible ? KickFrame : LockedFrame, filled: false, width: 2f);
        // NAMEPLATE-DECLUTTER: the frame's cast label is the spell name (never "<enemy name>..."), cut to the bar with the
        // seconds kept whole.
        var suffix = FormattableString.Invariant($"  {secondsLeft:0.0}s");
        var room = (int)cast.Size.X - 8 - (int)Math.Ceiling(HudNodes.Width(suffix, SmallFont));
        var spell = Keyrun.Content.HudLayout.FitTitle(_driver.AbilityName(castId), room,
            s => (int)Math.Ceiling(HudNodes.Width(s, SmallFont)));
        HudNodes.Text(this, new Vector2(cast.Position.X + 4, cast.End.Y - 3), spell + suffix, SmallFont, Colors.White);
    }

    private void OnRunStarted()
    {
        _shown.Clear();
        _castLogged.Clear();
    }

    // Proof lines (autorun only): "HUD unitframe kind=K actor=A name="N" hp=H/M" when a frame first shows an enemy, and
    // "HUD unitframe cast kind=K actor=A ability=X interruptible=0|1" once per cast start on a shown frame.
    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (_driver?.Run is not { } run || !_driver.Options.Autorun)
        {
            return;
        }
        foreach (var f in UnitFrameModel.Build(run.State, HumanId))
        {
            if (!run.State.Actors.TryGetValue(f.ActorId, out var actor))
            {
                continue;
            }
            var key = FormattableString.Invariant($"{f.Kind}:{f.ActorId}");
            if (_shown.Add(key))
            {
                HudNodes.Proof(_driver, FormattableString.Invariant(
                    $"HUD unitframe kind={f.Kind} actor={f.ActorId} name=\"{_driver.DisplayName(actor)}\" hp={actor.Health}/{actor.MaxHealth}"));
            }
            if (actor.CastId is { } castId && actor.CastStartTick is { } start && actor.CastEndsTick is not null &&
                (!_castLogged.TryGetValue(key, out var last) || last != start))
            {
                _castLogged[key] = start;
                var interruptible = run.Content.Abilities.TryGetValue(castId, out var def) && def.Interruptible;
                HudNodes.Proof(_driver, FormattableString.Invariant(
                    $"HUD unitframe cast kind={f.Kind} actor={f.ActorId} ability={castId} interruptible={(interruptible ? 1 : 0)}"));
            }
        }
    }
}
