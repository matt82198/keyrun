using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using PixelRect = Keyrun.Content.PixelRect;

namespace Keyrun.Hud;

/// <summary>One row of a unit frame, in pixels from the frame's top edge.</summary>
/// <param name="Top">The row's top edge.</param>
/// <param name="Bottom">The row's bottom edge.</param>
public readonly record struct FrameRow(float Top, float Bottom);

/// <summary>
/// A unit frame's vertical layout (Godot-free): the name, the health bar, then the cast row, the Break bar row
/// (BREAK-BAR) and the Lock row (LOCK-ROW) each only when present, stacked under the health bar.
/// </summary>
/// <param name="Height">The frame's height.</param>
/// <param name="Cast">The cast row, or null when the enemy is not casting.</param>
/// <param name="Break">The Break bar row, or null when the frame has no Break bar.</param>
/// <param name="Lock">The Lock row, or null when the boss's Lock is not showing.</param>
public sealed record UnitFrameRows(float Height, FrameRow? Cast, FrameRow? Break, FrameRow? Lock = null)
{
    /// <summary>The Lock row's height (LOCK-ROW).</summary>
    public const float LockHeight = 15;

    /// <summary>The health bar's top edge.</summary>
    public const float HealthTop = 24;

    /// <summary>The health bar's bottom edge.</summary>
    public const float HealthBottom = 37;

    /// <summary>The cast row's height.</summary>
    public const float CastHeight = 15;

    /// <summary>The space between rows and under the last row.</summary>
    public const float Pad = 4;

    /// <summary>
    /// The layout for a frame. BOSS-CAST-ROW (HUD-DECLUTTER): an idle enemy's frame reserves no cast row, so the
    /// Break bar row (or the frame's bottom edge) sits directly under the health bar; a live cast opens its row under
    /// the health bar for exactly as long as it lasts, and nothing moves while it does.
    /// </summary>
    /// <param name="casting">The frame's enemy has a live cast.</param>
    /// <param name="breakBar">The frame carries a Break bar row.</param>
    /// <param name="lockRow">The frame carries a Lock row (LOCK-ROW), under the Break bar row.</param>
    /// <returns>The layout.</returns>
    public static UnitFrameRows For(bool casting, bool breakBar, bool lockRow = false)
    {
        var y = HealthBottom;
        FrameRow? cast = null;
        if (casting)
        {
            cast = new FrameRow(y + Pad, y + Pad + CastHeight);
            y += Pad + CastHeight;
        }
        FrameRow? brk = null;
        if (breakBar)
        {
            brk = new FrameRow(y + Pad, y + BreakBar.RowHeight);
            y += BreakBar.RowHeight;
        }
        FrameRow? lck = null;
        if (lockRow)
        {
            lck = new FrameRow(y + Pad, y + Pad + LockHeight);
            y += Pad + LockHeight;
        }
        return new UnitFrameRows(y + Pad, cast, brk, lck);
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
    private const float Gap = 8;
    private const float RightGutter = 16;
    private const float Top = 124;
    private const int NameFont = 15;
    private const int SmallFont = 11;
    private const int LockStacksFont = 13;

    private static readonly Color HealthFill = new(0.78f, 0.22f, 0.2f, 1f);
    private static readonly Color TargetEdge = new(1f, 0.92f, 0.35f, 1f);
    private static readonly Color BossEdge = new(0.62f, 0.3f, 0.85f, 1f);
    private static readonly Color CastFill = new(0.95f, 0.72f, 0.2f, 0.95f);
    private static readonly Color KickFrame = new(1f, 0.6f, 0.1f, 1f);
    private static readonly Color LockedFrame = new(0.55f, 0.55f, 0.6f, 1f);

    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rowsLogged = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lockLogged = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lockRowLogged = new(StringComparer.Ordinal);
    private readonly Keyrun.Sim.Encounter.LockRowTracker _lockRow = new(Keyrun.Content.LockRule.FlashTicks);
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
                var r = FrameRect(frames, i);
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
                DrawFrame(FrameRect(frames, i), frames[i], actor, run);
            }
        }
    }

    /// <summary>The boss frame's Break bar row in this control's pixels (BREAK-BAR, drawn by <see cref="BreakBar"/>), or null when no boss frame has a bar.</summary>
    public Rect2? BossBreakRow
    {
        get
        {
            if (_driver?.Run is not { } run || !Visible)
            {
                return null;
            }
            var frames = UnitFrameModel.Build(run.State, HumanId);
            for (var i = 0; i < frames.Count; i++)
            {
                if (RowsOf(frames[i]).Break is { } row)
                {
                    var r = FrameRect(frames, i);
                    return new Rect2(r.Position.X + 8, r.Position.Y + row.Top, r.Size.X - 16, row.Bottom - row.Top);
                }
            }
            return null;
        }
    }

    // BREAK-BAR: a boss frame whose boss has a Break bar (BossBreakBarReadout) grows by BreakBar.RowHeight for it.
    private Rect2 FrameRect(IReadOnlyList<UnitFrame> frames, int index)
    {
        var y = Top;
        for (var j = 0; j < index; j++)
        {
            y += FrameHeightOf(frames[j]) + Gap;
        }
        return new Rect2(Size.X - RightGutter - FrameWidth, y, FrameWidth, FrameHeightOf(frames[index]));
    }

    private float FrameHeightOf(UnitFrame frame) => RowsOf(frame).Height;

    // The frame's rows: a cast row while its enemy casts, a Break bar row when the boss has a Break bar (BossBreakBarReadout),
    // a Lock row while the armed boss's Lock is showing (LOCK-ROW).
    private UnitFrameRows RowsOf(UnitFrame frame)
    {
        if (_driver?.Run is not { } run)
        {
            return UnitFrameRows.For(casting: false, breakBar: false);
        }
        var casting = run.State.Actors.TryGetValue(frame.ActorId, out var actor) && IsCasting(actor);
        var breakBar = frame.Kind == "boss" && Keyrun.Sim.Encounter.BossBreakBarReadout.For(run.Encounter, frame.ActorId) is not null;
        return UnitFrameRows.For(casting, breakBar, LockRowOf(frame, run) is not null);
    }

    // LOCK-ROW (f-8566b4f0; BIBLE s3 shape plus colour, never colour alone): the armed boss's Lock state in words and the
    // Tempo on the line (R-LOCK-1), from the sim events this control has read.
    private Keyrun.Sim.Encounter.BossLockRow? LockRowOf(UnitFrame frame, Run run) =>
        frame.Kind == "boss" && run.Encounter.Boss is { } boss && boss.Config.BossActorId == frame.ActorId
            ? _lockRow.Row(boss.Config, run.State.Tick)
            : null;

    private static int LockRowColor(Keyrun.Sim.Encounter.LockRowState state) => state switch
    {
        Keyrun.Sim.Encounter.LockRowState.Broken => Keyrun.Content.LockRule.BrokenColor,
        Keyrun.Sim.Encounter.LockRowState.Failed => Keyrun.Content.LockRule.FailedColor,
        Keyrun.Sim.Encounter.LockRowState.Enraged => Keyrun.Content.LockRule.EnrageColor,
        _ => Keyrun.Content.LockRule.OpenColor,
    };

    private static bool IsCasting(ActorState actor) =>
        actor.CastId is not null && actor.CastStartTick is not null && actor.CastEndsTick is not null;

    private void DrawFrame(Rect2 rect, UnitFrame frame, ActorState actor, Run run)
    {
        DrawRect(rect, HudNodes.Back);
        DrawRect(rect, frame.Kind == "boss" ? BossEdge : TargetEdge, filled: false, width: 2f);
        HudNodes.Text(this, rect.Position + new Vector2(8, 18), _driver!.DisplayName(actor), NameFont, Colors.White);
        // LOCK-STAKES (R-LOCK-1): the boss's Tempo stacks from failed Locks, right-aligned on the name row in the Lock's
        // failed red ("LOCK x1/3"). Not in the Break bar: that bar is the Twins'.
        if (frame.Kind == "boss" && Keyrun.Sim.Encounter.BossLockReadout.For(run.Encounter, frame.ActorId) is { } lockStacks)
        {
            var w = HudNodes.Width(lockStacks.Text, LockStacksFont);
            HudNodes.Text(this, new Vector2(rect.End.X - 8 - w, rect.Position.Y + 18), lockStacks.Text, LockStacksFont,
                Keyrun.View.LockView.ToColor(Keyrun.Content.LockRule.FailedColor, 1000));
        }

        var rows = RowsOf(frame);
        var hp = new Rect2(rect.Position.X + 8, rect.Position.Y + UnitFrameRows.HealthTop, rect.Size.X - 16, UnitFrameRows.HealthBottom - UnitFrameRows.HealthTop);
        var frac = actor.MaxHealth <= 0 ? 0f : Mathf.Clamp((float)actor.Health / actor.MaxHealth, 0f, 1f);
        DrawRect(hp, new Color(0, 0, 0, 0.5f));
        DrawRect(new Rect2(hp.Position, new Vector2(hp.Size.X * frac, hp.Size.Y)), HealthFill);
        DrawString(ThemeDB.FallbackFont, new Vector2(hp.Position.X + 4, hp.End.Y - 2),
            FormattableString.Invariant($"{actor.Health} / {actor.MaxHealth}"), HorizontalAlignment.Left, -1, SmallFont, Colors.White);

        if (rows.Lock is { } lockRowRect && LockRowOf(frame, run) is { } lockRow)
        {
            // LOCK-ROW: a framed row in the ring's colour with the state and stake in words (never the colour alone).
            var box = new Rect2(hp.Position.X, rect.Position.Y + lockRowRect.Top, hp.Size.X, lockRowRect.Bottom - lockRowRect.Top);
            DrawRect(box, new Color(0.05f, 0.05f, 0.07f, 0.85f));
            DrawRect(box, Keyrun.View.LockView.ToColor(LockRowColor(lockRow.State), 1000), filled: false, width: 2f);
            var text = Keyrun.Content.HudLayout.FitTitle(lockRow.Text, (int)box.Size.X - 8,
                s => (int)Math.Ceiling(HudNodes.Width(s, SmallFont)));
            HudNodes.Text(this, new Vector2(box.Position.X + 4, box.End.Y - 3), text, SmallFont, Colors.White);
        }

        if (actor.CastId is not { } castId || actor.CastStartTick is not { } start || actor.CastEndsTick is not { } end ||
            rows.Cast is not { } castRow)
        {
            return;
        }
        var now = run.State.Tick + _driver.Alpha;
        var progress = Mathf.Clamp((now - start) / Mathf.Max(1f, end - start), 0f, 1f);
        var secondsLeft = Mathf.Max(0f, (end - now) / Tick.TickHz);
        var cast = new Rect2(hp.Position.X, rect.Position.Y + castRow.Top, hp.Size.X, castRow.Bottom - castRow.Top);
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
        _rowsLogged.Clear();
        _lockLogged.Clear();
        _lockRowLogged.Clear();
        _lockRow.Reset();
    }

    private static string Row(FrameRow? row) =>
        row is { } r ? FormattableString.Invariant($"{r.Top:0}-{r.Bottom:0}") : "none";

    // Proof lines (autorun only): "HUD unitframe kind=K actor=A name="N" hp=H/M" when a frame first shows an enemy,
    // "HUD unitframe cast kind=K actor=A ability=X interruptible=0|1" once per cast start on a shown frame, and
    // "HUD unitframe rows kind=K actor=A height=H hp=T-B cast=T-B|none start=S|- spell="X" break=T-B|none tick=N"
    // whenever a shown frame's row layout changes (rows in pixels from the frame's top edge), and
    // "HUD unitframe lock kind=boss actor=A text="LOCK xN/M" tempoBp=T tick=N" whenever a boss frame's Lock stack
    // readout changes (LOCK-STAKES), and
    // "HUD unitframe lockrow kind=boss actor=A state=S lock=I stakeBp=T text="LOCK open -- ..." row=T-B tick=N" whenever
    // the boss frame's Lock row changes (LOCK-ROW; state=none row=none when it clears).
    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        // LOCK-ROW: every step's events feed the Lock row, interactive or autorun.
        _lockRow.Observe(events);
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
            var rows = RowsOf(f);
            var rowsLine = FormattableString.Invariant(
                $"HUD unitframe rows kind={f.Kind} actor={f.ActorId} height={rows.Height:0} hp={UnitFrameRows.HealthTop:0}-{UnitFrameRows.HealthBottom:0} cast={Row(rows.Cast)} start={(rows.Cast is null ? "-" : FormattableString.Invariant($"{actor.CastStartTick}"))} spell=\"{(rows.Cast is not null && actor.CastId is { } sid ? _driver.AbilityName(sid) : "")}\" break={Row(rows.Break)}");
            if (!_rowsLogged.TryGetValue(key, out var lastRows) || lastRows != rowsLine)
            {
                _rowsLogged[key] = rowsLine;
                HudNodes.Proof(_driver, rowsLine + FormattableString.Invariant($" tick={tick}"));
            }
            if (f.Kind == "boss" && Keyrun.Sim.Encounter.BossLockReadout.For(run.Encounter, f.ActorId) is { } lockStacks)
            {
                var lockLine = FormattableString.Invariant(
                    $"HUD unitframe lock kind={f.Kind} actor={f.ActorId} text=\"{lockStacks.Text}\" tempoBp={lockStacks.TempoBp}");
                if (!_lockLogged.TryGetValue(key, out var lastLock) || lastLock != lockLine)
                {
                    _lockLogged[key] = lockLine;
                    HudNodes.Proof(_driver, lockLine + FormattableString.Invariant($" tick={tick}"));
                }
            }
            if (f.Kind == "boss" && run.Encounter.Boss is not null)
            {
                var lockRow = LockRowOf(f, run);
                var lockRowLine = lockRow is null
                    ? FormattableString.Invariant($"HUD unitframe lockrow kind={f.Kind} actor={f.ActorId} state=none lock=0 stakeBp=0 text=\"\" row=none")
                    : FormattableString.Invariant(
                        $"HUD unitframe lockrow kind={f.Kind} actor={f.ActorId} state={lockRow.State.ToString().ToLowerInvariant()} lock={lockRow.LockIndex} stakeBp={lockRow.StakeTempoBp} text=\"{lockRow.Text}\" row={Row(rows.Lock)}");
                var known = _lockRowLogged.TryGetValue(key, out var lastLockRow);
                if ((known || lockRow is not null) && lastLockRow != lockRowLine)
                {
                    _lockRowLogged[key] = lockRowLine;
                    HudNodes.Proof(_driver, lockRowLine + FormattableString.Invariant($" tick={tick}"));
                }
            }
            // The "HUD unitframe cast" line is no longer emitted here: CastBarView logs it with the castbar line from one
            // placement decision (CastProofLog, R-DECLUTTER-1), so the two proof lines describe the same moment.
        }
    }
}
