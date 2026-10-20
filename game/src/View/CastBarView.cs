using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Hud;
using Keyrun.Sim.Core;

namespace Keyrun.View;

/// <summary>
/// Cast bars, drawn in screen space over the 3D view: one above every casting actor's head (the sim carries a cast as
/// state: CastId, CastStartTick, CastEndsTick), filled by the fraction of the cast elapsed at the displayed fractional
/// tick, with "caster: ability" and the seconds left. An interruptible cast has an amber frame (kick it), an
/// uninterruptible one grey. The Warden's own cast also shows as a wide bar above the ability bar. When the sim
/// interrupts a cast the bar turns red and reads "caster: ability interrupted"; a completed cast flashes green; both
/// fade out.
/// <para>
/// HUD-DECLUTTER (Matt 2026-10-19): only enemies get an over-head bar. A party member's cast (the human's and the
/// bots') is drawn inside its party frame (<c>PartyFrames</c>); the Warden's wide bar over the ability bar stays.
/// NAMEPLATE-DECLUTTER: an enemy a unit frame shows (the boss, the Warden's target) has its cast drawn on that frame
/// only, so no cast reads twice (<see cref="HudDeclutter"/>.OverheadCast with <c>UnitFrameModel.Build</c>).
/// </para>
/// <para>
/// HUD-POLISH-2 (design/ART-PLAN.md section 2, no HUD text overlaps another): the over-head bars are laid out once a
/// frame by <see cref="HudLayout"/>.CastBars, nearest the camera first, so two casts side by side never intersect, every
/// bar stays inside the viewport with the top bar's 16 px side gutter, clear of the top bar and the party frames, and the
/// label is cut with <see cref="HudLayout"/>.FitCastLabel to the bar's width. <see cref="BarRects"/> exposes this frame's
/// blocks so <c>Pings</c> and <c>LabelView</c> keep their text clear of them.
/// </para>
/// </summary>
public partial class CastBarView : Control
{
    private const float HeadHeightM = 3.0f; // above the nameplate (LabelView, PlateRule plate height)
    private const float FadeSeconds = 0.9f;
    private const int FontSize = 14;

    private static readonly Vector2 PlayerBarSize = new(320, 22);
    private static readonly Color Back = new(0.05f, 0.05f, 0.07f, 0.8f);
    private static readonly Color Fill = new(0.95f, 0.72f, 0.2f, 0.95f);
    private static readonly Color KickFrame = new(1f, 0.6f, 0.1f, 1f);
    private static readonly Color LockedFrame = new(0.55f, 0.55f, 0.6f, 1f);
    private static readonly Color InterruptedColor = new(0.9f, 0.15f, 0.12f, 1f);
    private static readonly Color DoneColor = new(0.35f, 0.85f, 0.4f, 1f);

    private readonly List<Ending> _endings = new();
    private readonly Dictionary<int, int> _castLogged = new();
    private readonly List<Bar> _bars = new();
    private readonly List<PixelRect> _rects = new();
    private ulong _laidOutFrame = ulong.MaxValue;

    /// <summary>The driver to read state and events from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../SimDriver");

    /// <summary>The party frames, a HUD obstacle the bars keep clear of (absent: no party-frame obstacle).</summary>
    [Export]
    public NodePath PartyFramesPath { get; set; } = new("../HudRoot/PartyFrames");

    /// <summary>The enemy unit frames (HUD-DECLUTTER), a HUD obstacle the bars keep clear of.</summary>
    [Export]
    public NodePath UnitFramesPath { get; set; } = new("../HudRoot/UnitFrames");

    /// <summary>Gap between the player's cast bar and the bottom of the screen, px (clears the ability bar).</summary>
    [Export]
    public float PlayerBarBottomGap { get; set; } = 150;

    /// <summary>
    /// This frame's over-head bar blocks (the label band plus the bar) in viewport pixels, live and fading casts alike.
    /// Laid out on first use each frame, so every reader sees the rectangles this node draws.
    /// </summary>
    public IReadOnlyList<PixelRect> BarRects
    {
        get
        {
            Layout();
            return _rects;
        }
    }

    private SimDriver? Driver { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
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
        for (var i = _endings.Count - 1; i >= 0; i--)
        {
            _endings[i].Age += (float)delta;
            if (_endings[i].Age >= FadeSeconds)
            {
                _endings.RemoveAt(i);
            }
        }
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (Driver is null)
        {
            return;
        }
        Layout();
        var font = ThemeDB.FallbackFont;
        var local = GetGlobalRect().Position;
        var size = new Vector2(HudLayout.CastBarWidth, HudLayout.CastBarHeight);
        for (var i = 0; i < _bars.Count; i++)
        {
            var b = _bars[i];
            var r = _rects[i];
            var origin = new Vector2(r.X, r.Y + HudLayout.CastLabelHeight) - local;
            DrawBar(font, origin, size, b.Progress, b.Fill, b.Frame, b.Label);
        }

        // The Warden's own cast also fills the wide slot over the ability bar.
        var now = Driver.State.Tick + Driver.Alpha;
        if (Driver.State.Actors.TryGetValue(SimDriver.WardenId, out var me) && me.Alive && me.CastId is { } castId &&
            me.CastStartTick is { } start && me.CastEndsTick is { } end)
        {
            var progress = Mathf.Clamp((now - start) / Mathf.Max(1f, end - start), 0f, 1f);
            var secondsLeft = Mathf.Max(0f, (end - now) / Tick.TickHz);
            DrawBar(font, PlayerBarOrigin(), PlayerBarSize, progress, Fill, FrameOf(castId), $"{Driver.AbilityName(castId)}  {secondsLeft:0.0}s");
        }
        foreach (var e in _endings)
        {
            if (e.ActorId == SimDriver.WardenId)
            {
                var color = new Color(e.Interrupted ? InterruptedColor : DoneColor, 1f - (e.Age / FadeSeconds));
                DrawBar(font, PlayerBarOrigin(), PlayerBarSize, 1f, color, color, e.Interrupted ? $"Interrupted: {e.Name}" : e.Name);
            }
        }
    }

    // Collects this frame's over-head bars (live casts, then fading endings), nearest the camera first, and places them
    // with HudLayout.CastBars. Runs once per process frame; _Draw and every BarRects reader share the result.
    private void Layout()
    {
        var frameNo = Engine.GetProcessFrames();
        if (frameNo == _laidOutFrame)
        {
            return;
        }
        _laidOutFrame = frameNo;
        _bars.Clear();
        _rects.Clear();
        var camera = GetViewport()?.GetCamera3D();
        if (Driver is null || camera is null)
        {
            return;
        }
        var view = GetViewport().GetVisibleRect().Size;
        var width = (int)view.X;
        var height = (int)view.Y;
        var font = ThemeDB.FallbackFont;
        int Measure(string s) => (int)Math.Ceiling(font.GetStringSize(s, HorizontalAlignment.Left, -1, FontSize).X);

        var now = Driver.State.Tick + Driver.Alpha;
        var unitFrames = Frames();
        foreach (var (id, actor) in Driver.State.Actors)
        {
            if (!actor.Alive || actor.CastId is not { } castId || actor.CastStartTick is not { } start || actor.CastEndsTick is not { } end)
            {
                continue;
            }
            // HUD-DECLUTTER: a party member's cast is drawn in its party frame, never over its head; NAMEPLATE-DECLUTTER:
            // an enemy on a unit frame has its cast on that frame only.
            if (!HudDeclutter.OverheadCast(actor, unitFrames))
            {
                continue;
            }
            var progress = Mathf.Clamp((now - start) / Mathf.Max(1f, end - start), 0f, 1f);
            var secondsLeft = Mathf.Max(0f, (end - now) / Tick.TickHz);
            var label = HudLayout.FitCastLabel(Driver.DisplayName(actor), Driver.AbilityName(castId), $"  {secondsLeft:0.0}s",
                HudLayout.CastBarWidth, Measure);
            AddBar(camera, id, width, height, progress, Fill, FrameOf(castId), label);
        }
        foreach (var e in _endings)
        {
            if (Driver.State.Actors.TryGetValue(e.ActorId, out var ender) && !HudDeclutter.OverheadCast(ender, unitFrames))
            {
                continue;
            }
            var color = new Color(e.Interrupted ? InterruptedColor : DoneColor, 1f - (e.Age / FadeSeconds));
            var caster = Driver.State.Actors.TryGetValue(e.ActorId, out var a) ? Driver.DisplayName(a) : string.Empty;
            var label = HudLayout.FitCastLabel(caster, e.Interrupted ? $"{e.Name} interrupted" : e.Name, string.Empty,
                HudLayout.CastBarWidth, Measure);
            AddBar(camera, e.ActorId, width, height, 1f, color, color, label);
        }
        if (_bars.Count == 0)
        {
            return;
        }
        _bars.Sort((x, y) => x.Depth.CompareTo(y.Depth));

        var obstacles = new List<PixelRect> { HudLayout.TopBar(width, height).Band(width) };
        if (HudObstacles.PartyFrames(GetNodeOrNull<Control>(PartyFramesPath), Driver.State) is { } frames)
        {
            obstacles.Add(frames);
        }
        if (GetNodeOrNull<UnitFrames>(UnitFramesPath) is { } units)
        {
            obstacles.AddRange(units.FrameRects);
        }
        var anchors = new PixelPoint[_bars.Count];
        for (var i = 0; i < _bars.Count; i++)
        {
            anchors[i] = _bars[i].Anchor;
        }
        _rects.AddRange(HudLayout.CastBars(anchors, width, height, obstacles));
    }

    private void AddBar(Camera3D camera, int actorId, int width, int height, float progress, Color fill, Color frame, string label)
    {
        if (!ScreenPoint(camera, actorId, out var p, out var depth))
        {
            return;
        }
        var anchor = new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y));
        // Skip a bar no pixel of which would show where its caster stands (a caster well off screen).
        if (HudLayout.Shows(HudLayout.CastBarRect(anchor), width, height))
        {
            _bars.Add(new Bar(anchor, depth, progress, fill, frame, label));
        }
    }

    // The enemy unit frames this frame shows (NAMEPLATE-DECLUTTER: their enemies' casts draw there, not over the head).
    private IReadOnlyList<UnitFrame> Frames() =>
        Driver is null ? Array.Empty<UnitFrame>() : UnitFrameModel.Build(Driver.State, Driver.Setup?.HumanActorId ?? SimDriver.WardenId);

    private Color FrameOf(string castId) =>
        Driver?.Run?.Content.Abilities.TryGetValue(castId, out var def) == true && def.Interruptible ? KickFrame : LockedFrame;

    private Vector2 PlayerBarOrigin() => new((Size.X - PlayerBarSize.X) / 2, Size.Y - PlayerBarBottomGap);

    private bool ScreenPoint(Camera3D camera, int actorId, out Vector2 point, out float depth)
    {
        point = default;
        depth = 0f;
        if (Driver is null || !Driver.State.Actors.TryGetValue(actorId, out var actor))
        {
            return false;
        }
        var before = Driver.PreviousState.Actors.TryGetValue(actorId, out var prev) ? prev : actor;
        var world = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), Driver.Alpha) + new Vector3(0, HeadHeightM, 0);
        if (camera.IsPositionBehind(world))
        {
            return false;
        }
        point = camera.UnprojectPosition(world);
        depth = camera.GlobalPosition.DistanceSquaredTo(world);
        return true;
    }

    private void DrawBar(Font font, Vector2 origin, Vector2 size, float progress, Color fill, Color frame, string label)
    {
        DrawRect(new Rect2(origin, size), Back);
        DrawRect(new Rect2(origin, new Vector2(size.X * progress, size.Y)), fill);
        DrawRect(new Rect2(origin, size), frame, filled: false, width: 2f);
        var textSize = font.GetStringSize(label, HorizontalAlignment.Left, -1, FontSize);
        var textPos = new Vector2(origin.X + ((size.X - textSize.X) / 2), origin.Y - 4);
        DrawString(font, textPos + new Vector2(1, 1), label, HorizontalAlignment.Left, -1, FontSize, new Color(0, 0, 0, frame.A));
        DrawString(font, textPos, label, HorizontalAlignment.Left, -1, FontSize, new Color(1, 1, 1, frame.A));
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (Driver is null)
        {
            return;
        }
        LogCastStarts();
        foreach (var e in events)
        {
            if (e.Type == "interrupt" && e.Get("targetId") is JsInt target && e.Get("abilityId") is JsString ability)
            {
                _endings.Add(new Ending((int)target.Value, Driver.AbilityName(ability.Value), interrupted: true));
            }
            else if (e.Type == "cast_complete" && e.Get("actorId") is JsInt actor && e.Get("abilityId") is JsString done &&
                Driver.Run?.Content.Abilities.TryGetValue(done.Value, out var def) == true && def.CastTicks > 0)
            {
                _endings.Add(new Ending((int)actor.Value, Driver.AbilityName(done.Value), interrupted: false));
            }
        }
    }

    private void OnRunStarted()
    {
        _endings.Clear();
        _castLogged.Clear();
    }

    // HUD-DECLUTTER proof (autorun only): one "HUD castbar actor=A kind=player|enemy where=frame|overhead ability=X"
    // line per cast start, the placement Layout uses (HudDeclutter.OverheadCast).
    private void LogCastStarts()
    {
        if (Driver is null || !Driver.Options.Autorun)
        {
            return;
        }
        var unitFrames = Frames();
        foreach (var (id, actor) in Driver.State.Actors)
        {
            if (!actor.Alive || actor.CastId is not { } castId || actor.CastStartTick is not { } start)
            {
                continue;
            }
            if (_castLogged.TryGetValue(id, out var last) && last == start)
            {
                continue;
            }
            _castLogged[id] = start;
            HudNodes.Proof(Driver, FormattableString.Invariant(
                $"HUD castbar actor={id} kind={HudDeclutter.Side(actor)} where={HudDeclutter.CastPlacement(actor, unitFrames)} ability={castId}"));
        }
    }

    private readonly record struct Bar(PixelPoint Anchor, float Depth, float Progress, Color Fill, Color Frame, string Label);

    private sealed class Ending
    {
        public Ending(int actorId, string name, bool interrupted)
        {
            ActorId = actorId;
            Name = name;
            Interrupted = interrupted;
        }

        public int ActorId { get; }

        public string Name { get; }

        public bool Interrupted { get; }

        public float Age { get; set; }
    }
}
