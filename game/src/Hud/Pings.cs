using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Keyrun.View;
using HudLayout = Keyrun.Content.HudLayout;
using PingEvent = Keyrun.Bots.Shared.PingEvent;
using PingKind = Keyrun.Bots.Shared.PingKind;
using PingWords = Keyrun.Bots.Shared.PingWords;
using PixelPoint = Keyrun.Content.PixelPoint;
using PointMm = Keyrun.Bots.PointMm;
using PixelRect = Keyrun.Content.PixelRect;

namespace Keyrun.Hud;

/// <summary>HUD rectangles several screen-space layers keep their text clear of (viewport pixels).</summary>
public static class HudObstacles
{
    // PartyFrames.cs draws one 68 px frame per seat with an 8 px gap (FrameHeight, Gap).
    private const int PartyFrameStride = 76;
    private const int PartyFrameGap = 8;

    /// <summary>The party frames' block: one frame per player seat down from the node's top left, or null.</summary>
    /// <param name="frames">The PartyFrames node (null or hidden: no block).</param>
    /// <param name="state">The sim state (its player count is the seat count).</param>
    /// <returns>The block, or null when there is none.</returns>
    public static PixelRect? PartyFrames(Control? frames, SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (frames is not { Visible: true })
        {
            return null;
        }
        var seats = state.Actors.Values.Count(a => a.Kind == ActorKind.Player);
        if (seats == 0)
        {
            return null;
        }
        var at = frames.GetGlobalRect().Position;
        return new PixelRect((int)at.X, (int)at.Y, (int)frames.Size.X, (seats * PartyFrameStride) - PartyFrameGap);
    }
}

/// <summary>
/// The SPREAD / STACK labels (ENGINE-PLAN 3C): each call is drawn over its spot in the world (projected through the
/// arena camera) and listed as a callout banner under the Slack bar, so the instruction reads even off screen.
/// Reads the sim state only.
/// <para>
/// HUD-POLISH-2 (design/ART-PLAN.md section 2, no HUD text overlaps another): the world labels are laid out once a
/// frame with <see cref="HudLayout"/>.PingLabels, in call order, so simultaneous pings on nearby bodies stack one per
/// row next to their spot (two rows at most either way for five on one body) instead of one pile, inside the 16 px side
/// gutter and clear of the top bar, the party frames, the callout banners and the cast bars (<c>CastBarView.BarRects</c>).
/// <see cref="LabelRects"/> exposes the placed labels and banners so LabelView's name plates keep clear of them.
/// </para>
/// <para>
/// NAMEPLATE-DECLUTTER (Matt 2026-10-19, "reduce screen clutter"): a SPREAD call draws its banner only, never a second
/// "SPREAD: move apart" in the world (<see cref="HudDeclutter"/>.WorldPingLabel); STACK keeps its world label, the spot
/// to group up on. HUD-STACK-ONCE: that world label is the marker word alone (<see cref="HudDeclutter"/>.WorldPingText,
/// "STACK"), so "STACK: group up" is drawn once, by the banner.
/// </para>
/// <para>
/// PING-FEEDBACK (design/BIBLE.md s10, "the game turns it into the party's language ("kick", "stack here",
/// "Window!")"): the human's G (<see cref="PlayerInput.Pinged"/>) puts the word <see cref="PingWords"/>.Say picks from
/// the bots' own ping context over the pinged target (or the pinger, a spot ping, as SimDriver.RoutePing builds it)
/// for <see cref="PingWords.ShowTicks"/> (2 s), laid out with the call labels.
/// </para>
/// </summary>
public partial class Pings : Control
{
    private const int CalloutFont = 22;
    private const float CalloutTop = 100;

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _textLogged = new(StringComparer.Ordinal);
    private readonly List<Shown> _labels = new();
    private readonly List<Callout> _callouts = new();
    private readonly List<PixelRect> _rects = new();
    private ulong _laidOutFrame = ulong.MaxValue;
    private SimDriver? _driver;
    private PlayerInput? _input;
    private Said? _said;

    /// <summary>The human's input, whose G-ping (<see cref="PlayerInput.Pinged"/>) this node puts into words.</summary>
    [Export]
    public NodePath PlayerInputPath { get; set; } = new("../../../PlayerInput");

    /// <summary>The driver to read the run from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>The over-head cast bars, whose blocks the world labels keep clear of (absent: no cast-bar obstacle).</summary>
    [Export]
    public NodePath CastBarsPath { get; set; } = new("../../CastBars");

    /// <summary>The party frames, a HUD obstacle the world labels keep clear of (absent: no party-frame obstacle).</summary>
    [Export]
    public NodePath PartyFramesPath { get; set; } = new("../PartyFrames");

    /// <summary>
    /// This frame's placed world labels and callout banners, in viewport pixels. Laid out on first use each frame, so
    /// every reader sees the rectangles this node draws.
    /// </summary>
    public IReadOnlyList<PixelRect> LabelRects
    {
        get
        {
            Layout();
            return _rects;
        }
    }

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
        _input = GetNodeOrNull<PlayerInput>(PlayerInputPath);
        if (_input is not null)
        {
            _input.Pinged += OnPinged;
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
        if (_input is not null)
        {
            _input.Pinged -= OnPinged;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // Lay out every process frame (not only when drawn), so the proof line below reports what this frame shows.
        Layout();
        LogPingTexts();
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        Layout();
        var local = GetGlobalRect().Position;
        foreach (var c in _callouts)
        {
            DrawRect(new Rect2(c.Back.Position - local, c.Back.Size), HudNodes.Back);
            HudNodes.Text(this, c.Baseline - local, c.Text, CalloutFont, c.Color);
        }
        for (var i = 0; i < _labels.Count; i++)
        {
            var r = _rects[_callouts.Count + i];
            var baseline = new Vector2(r.X, r.Y + HudLayout.PingFont) - local;
            HudNodes.Text(this, baseline, _labels[i].Text, HudLayout.PingFont, _labels[i].Color);
        }
    }

    private static Color ColorOf(string label) => label == "SPREAD" ? HudNodes.Rgb(0xcc79a7) : HudNodes.Rgb(0x56b4e9);

    // Lays out this frame's callout banners (fixed rows under the Slack bar) and world labels (HudLayout.PingLabels),
    // once per process frame; _rects holds the banners first, then the placed labels in call order.
    private void Layout()
    {
        var frameNo = Engine.GetProcessFrames();
        if (frameNo == _laidOutFrame)
        {
            return;
        }
        _laidOutFrame = frameNo;
        _labels.Clear();
        _callouts.Clear();
        _rects.Clear();
        if (_driver?.Run is not { } run)
        {
            return;
        }
        var calls = PingCalls.Calls(run);
        var said = SaidNow(run);
        if (calls.Count == 0 && said is null)
        {
            return;
        }
        var local = GetGlobalRect().Position;

        // One callout per distinct label, the soonest first.
        var y = CalloutTop;
        foreach (var group in calls.GroupBy(c => c.Label).OrderBy(g => g.Min(c => c.TicksLeft)))
        {
            var soonest = group.Min(c => c.TicksLeft);
            var text = FormattableString.Invariant($"{group.First().Text}  {soonest / (double)Tick.TickHz:0.0}s");
            var w = HudNodes.Width(text, CalloutFont);
            var back = new Rect2(local.X + ((Size.X - w) / 2) - 10, local.Y + y, w + 20, CalloutFont + 10);
            _callouts.Add(new Callout(text, ColorOf(group.Key), back, new Vector2(local.X + ((Size.X - w) / 2), local.Y + y + CalloutFont)));
            _rects.Add(new PixelRect((int)Math.Floor(back.Position.X), (int)Math.Floor(back.Position.Y), (int)Math.Ceiling(back.Size.X) + 1, (int)Math.Ceiling(back.Size.Y) + 1));
            y += CalloutFont + 14;
        }

        var camera = GetViewport()?.GetCamera3D();
        if (camera is null)
        {
            return;
        }
        var view = GetViewport().GetVisibleRect().Size;
        var width = (int)view.X;
        var height = (int)view.Y;
        var texts = new List<string>();
        var colors = new List<Color>();
        var spots = new List<PixelRect>();
        var callLabels = new List<string?>();
        foreach (var call in calls)
        {
            // NAMEPLATE-DECLUTTER: SPREAD's one text is its callout banner; only STACK marks a spot in the world.
            // HUD-STACK-ONCE: the world shows the marker word, never the banner's sentence a second time.
            if (HudDeclutter.WorldPingText(call.Label) is not { } marker)
            {
                continue;
            }
            var world = ActorView.ToMetres(call.Pos) + new Vector3(0, call.Source == PingSource.Mark ? 2.6f : 0.3f, 0);
            if (camera.IsPositionBehind(world))
            {
                continue;
            }
            var p = camera.UnprojectPosition(world);
            var rect = HudLayout.PingLabelRect(new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y)),
                (int)Math.Ceiling(HudNodes.Width(marker, HudLayout.PingFont)));
            // Skip a label no pixel of which would show at its spot (a ping well off screen).
            if (HudLayout.Shows(rect, width, height))
            {
                texts.Add(marker);
                colors.Add(ColorOf(call.Label));
                spots.Add(rect);
                callLabels.Add(call.Label);
            }
        }
        // PING-FEEDBACK: the human's ping word over the pinged target or spot, placed with the call labels.
        if (said is { } s)
        {
            var world = ActorView.ToMetres(s.Pos) + new Vector3(0, s.TargetId is null ? 0.3f : 2.6f, 0);
            if (!camera.IsPositionBehind(world))
            {
                var p = camera.UnprojectPosition(world);
                var rect = HudLayout.PingLabelRect(new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y)),
                    (int)Math.Ceiling(HudNodes.Width(s.Text, HudLayout.PingFont)));
                if (HudLayout.Shows(rect, width, height))
                {
                    texts.Add(s.Text);
                    colors.Add(SaidColor);
                    spots.Add(rect);
                    callLabels.Add(null);
                }
            }
        }
        // STACK-PING-LABEL: one label per call per spot (the Pair's casters each raise their own STACK telegraph).
        var wanted = new List<PixelRect>();
        foreach (var i in HudLayout.DistinctPings(texts, spots))
        {
            _labels.Add(new Shown(texts[i], colors[i], callLabels[i]));
            wanted.Add(spots[i]);
        }
        if (wanted.Count == 0)
        {
            return;
        }
        var obstacles = new List<PixelRect>(_rects) { HudLayout.TopBar(width, height).Band(width) };
        if (HudObstacles.PartyFrames(GetNodeOrNull<Control>(PartyFramesPath), run.State) is { } frames)
        {
            obstacles.Add(frames);
        }
        if (GetNodeOrNull<CastBarView>(CastBarsPath) is { } bars)
        {
            obstacles.AddRange(bars.BarRects);
        }
        _rects.AddRange(HudLayout.PingLabels(wanted, width, height, obstacles));
    }

    // NAMEPLATE-DECLUTTER proof (autorun only): "HUD pingtext label=L banner=B world=W", how many callout banners and
    // world labels draw a call's text this frame, printed whenever the pair changes for that label.
    private void LogPingTexts()
    {
        if (_driver is null || !_driver.Options.Autorun)
        {
            return;
        }
        foreach (var label in new[] { "SPREAD", "STACK" })
        {
            var prefix = label + ":";
            var banner = _callouts.Count(c => c.Text.StartsWith(prefix, StringComparison.Ordinal));
            var world = _labels.Count(l => l.Text.StartsWith(prefix, StringComparison.Ordinal));
            var key = FormattableString.Invariant($"{banner}/{world}");
            if (_textLogged.TryGetValue(label, out var last) && last == key)
            {
                continue;
            }
            _textLogged[label] = key;
            if (banner + world > 0)
            {
                HudNodes.Proof(_driver, FormattableString.Invariant($"HUD pingtext label={label} banner={banner} world={world}"));
            }
        }
        // HUD-STACK-ONCE proof: "HUD pingmarker label=L world=M", how many world markers (no sentence) a call draws.
        foreach (var label in new[] { "SPREAD", "STACK" })
        {
            var markers = _labels.Count(l => string.Equals(l.CallLabel, label, StringComparison.Ordinal));
            var key = "marker:" + label;
            var value = markers.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_textLogged.TryGetValue(key, out var last) && last == value)
            {
                continue;
            }
            _textLogged[key] = value;
            if (markers > 0)
            {
                HudNodes.Proof(_driver, FormattableString.Invariant($"HUD pingmarker label={label} world={markers}"));
            }
        }
    }

    private void OnRunStarted()
    {
        _seen.Clear();
        _textLogged.Clear();
        _said = null;
    }

    private static Color SaidColor => HudNodes.Rgb(0xf0e442);

    // The live ping word, following a pinged actor while it lives (else the spot it was on); null once expired.
    private Said? SaidNow(Run run)
    {
        if (_said is not { } s || run.State.Tick >= s.UntilTick)
        {
            return null;
        }
        return s.TargetId is { } id && run.State.Actors.TryGetValue(id, out var a) && a.Alive ? s with { Pos = a.Pos } : s;
    }

    // PlayerInput.Pinged (raised inside the human's Sample, the pre-step run): the ping SimDriver.RoutePing builds (a
    // target ping on the human's target, else a spot ping at the pinger), put into words from the same state the bots read.
    private void OnPinged(int? targetId)
    {
        if (_driver?.Run is not { } run || !run.State.Actors.TryGetValue(SimDriver.WardenId, out var me))
        {
            return;
        }
        var tick = run.State.Tick;
        var ping = targetId is { } target
            ? new PingEvent(tick, SimDriver.WardenId, PingKind.Target) { TargetId = target }
            : new PingEvent(tick, SimDriver.WardenId, PingKind.Spot) { Point = new PointMm(me.Pos.X, me.Pos.Z) };
        var stackPending = PingCalls.Calls(run).Any(c => c.Source == PingSource.Telegraph && c.Label == "STACK");
        var text = PingWords.Say(run.State.Actors, tick, ping, stackPending);
        var pos = targetId is { } t && run.State.Actors.TryGetValue(t, out var pinged) ? pinged.Pos : me.Pos;
        _said = new Said(text, targetId, pos, tick + PingWords.ShowTicks);
        // One line per G press (interactive runs only: --autorun never pings), the proof the human saw the word.
        GD.Print(FormattableString.Invariant(
            $"HUD pingsay text=\"{text}\" kind={(targetId is null ? "spot" : "target")} target={(targetId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none")} tick={tick} until={tick + PingWords.ShowTicks}"));
    }

    private readonly record struct Said(string Text, int? TargetId, Vec3 Pos, int UntilTick);

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        foreach (var call in PingCalls.Calls(run))
        {
            if (_seen.Add(call.Key))
            {
                HudNodes.Proof(_driver, call.LogLine());
            }
        }
    }

    private readonly record struct Shown(string Text, Color Color, string? CallLabel);

    private readonly record struct Callout(string Text, Color Color, Rect2 Back, Vector2 Baseline);
}
