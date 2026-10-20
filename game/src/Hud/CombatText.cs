using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.View;
using CombatTextRule = Keyrun.Content.CombatTextRule;
using DrawnPlate = Keyrun.Content.DrawnPlate;
using DrawnPlates = Keyrun.Content.DrawnPlates;
using HudLayout = Keyrun.Content.HudLayout;
using PixelPoint = Keyrun.Content.PixelPoint;
using PixelRect = Keyrun.Content.PixelRect;

namespace Keyrun.Hud;

/// <summary>
/// Floating combat text (HUD-DECLUTTER, Matt 2026-10-19: "only show the played characters damage numbers in fight"):
/// one rising, fading number over the target for each hit the human's seat lands
/// (<see cref="HudDeclutter.WorldDamageNumber"/>). Bots' hits spawn nothing, and damage the party takes reads as health
/// leaving the party frames, never as a number over a bot. Reads the sim events only. Lane MISS-VIEW: the human's own
/// "miss" event (a blinded hit, PR #223) floats "MISS" the same way (<see cref="CombatTextRule.HumanMiss"/>); a bot's or
/// an enemy's miss shows nothing.
/// <para>
/// Lane COMBATTEXT-OVERLAP (finding f-caae8653): numbers are laid out once per process frame with
/// <see cref="HudClearance.PlaceNumber"/> clear of every name plate (the box LabelView draws before separation,
/// <see cref="HudClearance.PlateRect"/>) and of the HUD panels (top bar band, party frames, unit frames, ability
/// bar), so a number never sits on a nameplate or a panel. When the run ends (the run-end panel opens) every live
/// number is cleared and none spawn until the next run.
/// </para>
/// <para>
/// Lane DMG-NUMBERS (finding f-07a02dc5): the over-head spot is where the plate and the cast bar label draw, so a
/// number moved only off the plate's pre-separation box still landed on a separated neighbour plate or on the cast bar
/// label. Numbers now go beside the target's plate as LabelView draws it (the same separation and obstacles), drifting sideways and down away from it, clear of every drawn plate, every cast bar
/// block (<c>CastBarView.BarRects</c>), every ping label, the HUD panels and the numbers placed before them
/// (<see cref="CombatTextRule.PlaceNumber"/>). MISS text follows the same rule.
/// </para>
/// <para>
/// Lane PLATE-RECTS (finding f-9d4a31a5): the plates are no longer a copy of LabelView's layout (that copy never folded
/// a same-name crowd, so a stacked "Paired Warrior x2" plate drew where the copy held two narrow boxes, and numbers
/// landed on it). CombatText reads <c>LabelView.PlateRects</c>, the plates as they draw this frame, through
/// <see cref="DrawnPlates"/>; a number on an actor folded into a crowd goes beside the crowd's one plate.
/// </para>
/// </summary>
public partial class CombatText : Control
{
    private const float LifeSeconds = 1.0f;
    private const float RisePx = 42f;
    private const float AnchorHeightM = 2.6f;
    private const int FontSize = 18;
    private const int OutlineSize = 4;

    private static readonly Color NumberColor = new(1f, 0.93f, 0.55f, 1f);
    private static readonly Color Outline = new(0f, 0f, 0f, 0.85f);

    private readonly List<Number> _numbers = new();
    private SimDriver? _driver;
    private bool _ended;
    private bool _endFrameLogged;

    /// <summary>The driver to read events from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <summary>The party frames, a HUD block numbers keep clear of.</summary>
    [Export]
    public NodePath PartyFramesPath { get; set; } = new("../PartyFrames");

    /// <summary>The enemy unit frames, HUD blocks numbers keep clear of.</summary>
    [Export]
    public NodePath UnitFramesPath { get; set; } = new("../UnitFrames");

    /// <summary>The over-head cast bars (Arena Hud/CastBars), whose blocks numbers and plates keep clear of.</summary>
    [Export]
    public NodePath CastBarsPath { get; set; } = new("../../CastBars");

    /// <summary>The SPREAD/STACK world labels, which plates (and so numbers) keep clear of.</summary>
    [Export]
    public NodePath PingsPath { get; set; } = new("../Pings");

    /// <summary>The name plates (Arena Labels, LabelView), whose drawn plates numbers go beside and keep clear of.</summary>
    [Export]
    public NodePath LabelsPath { get; set; } = new("../../../Labels");

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
    public override void _Process(double delta)
    {
        for (var i = _numbers.Count - 1; i >= 0; i--)
        {
            _numbers[i].Age += (float)delta;
            if (_numbers[i].Age >= LifeSeconds)
            {
                _numbers.RemoveAt(i);
            }
        }
        Layout();
        if (_ended && !_endFrameLogged && _driver is not null)
        {
            // Proof: how many numbers the first frame after the run-end panel opened still draws (0 once cleared).
            _endFrameLogged = true;
            HudNodes.Proof(_driver, FormattableString.Invariant($"HUD numbers drawn after=runend live={_numbers.Count}"));
        }
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var local = GetGlobalRect().Position;
        foreach (var n in _numbers)
        {
            if (n.Placed is not { } box)
            {
                continue;
            }
            var t = n.Age / LifeSeconds;
            var text = n.Text;
            var at = new Vector2(box.X + OutlineSize, box.Y + OutlineSize + FontSize) - local;
            var alpha = 1f - (t * t);
            DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, FontSize, OutlineSize, new Color(Outline, Outline.A * alpha));
            DrawString(font, at, text, HorizontalAlignment.Left, -1, FontSize, new Color(NumberColor, alpha));
        }
    }

    // Places every live number for this frame: beside its target's drawn plate (LabelView.PlateRects, its own plate or
    // the crowd plate it is folded into; DrawnPlates.PlateOf), drifting away from it, clear of every drawn plate, cast
    // bar, ping label, HUD panel and number placed before it (CombatTextRule.PlaceNumber). The first
    // placement of a number prints its proof line; "raw" there is the old over-head spot (rising with age), the one
    // that sits on the plate, so raw_hit=1 shows the collision the placement avoided.
    private void Layout()
    {
        var camera = GetViewport()?.GetCamera3D();
        if (_driver is null || camera is null || _numbers.Count == 0)
        {
            foreach (var n in _numbers)
            {
                n.Placed = null;
            }
            return;
        }
        var view = GetViewport().GetVisibleRect().Size;
        var width = (int)view.X;
        var height = (int)view.Y;
        var band = HudLayout.TopBar(width, height).Band(width);
        var bars = GetNodeOrNull<CastBarView>(CastBarsPath)?.BarRects ?? Array.Empty<PixelRect>();
        var pings = GetNodeOrNull<Pings>(PingsPath)?.LabelRects ?? Array.Empty<PixelRect>();
        var frames = FrameRects();
        var real = GetNodeOrNull<LabelView>(LabelsPath)?.PlateRects ?? Array.Empty<DrawnPlate>();
        var avoid = new List<PixelRect>(DrawnPlates.Rects(real)) { band, AbilityBarRect(view) };
        avoid.AddRange(bars);
        avoid.AddRange(pings);
        avoid.AddRange(frames);
        foreach (var n in _numbers)
        {
            var feet = n.World;
            if (_driver.State.Actors.TryGetValue(n.TargetId, out var target))
            {
                feet = ActorView.ToMetres(target.Pos);
            }
            var world = feet + new Vector3(0, AnchorHeightM, 0);
            if (camera.IsPositionBehind(world))
            {
                n.Placed = null;
                continue;
            }
            var screen = camera.UnprojectPosition(world);
            var baseline = new PixelPoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y - (RisePx * (n.Age / LifeSeconds))));
            var raw = HudClearance.NumberRect(baseline, (int)Math.Ceiling(HudNodes.Width(n.Text, FontSize)), FontSize, OutlineSize);
            var drawnPlate = DrawnPlates.PlateOf(real, n.TargetId);
            var hasPlate = drawnPlate.HasValue;
            var plate = drawnPlate.GetValueOrDefault();
            var placed = CombatTextRule.PlaceNumber(hasPlate ? plate : raw, raw.Width, raw.Height, n.Age / LifeSeconds, width, height, avoid);
            n.Placed = placed;
            if (placed is not { } box)
            {
                continue;
            }
            avoid.Add(box);
            if (!n.RealLogged && real.Count > 0)
            {
                // Lane PLATE-RECTS proof: the number checked against the plates LabelView actually draws this frame.
                // stacked = how many actors the target's drawn plate stands for (a folded same-name crowd when > 1).
                n.RealLogged = true;
                var stacked = 0;
                foreach (var p in real)
                {
                    foreach (var id in p.ActorIds)
                    {
                        stacked = id == n.TargetId ? p.ActorIds.Count : stacked;
                    }
                }
                HudNodes.Proof(_driver, FormattableString.Invariant(
                    $"HUD numbers real target={n.TargetId} number={HudClearance.Format(box)} plates={real.Count} stacked={stacked} real_hit={(DrawnPlates.Hits(box, real) ? 1 : 0)}"));
            }
            if (!n.Logged && hasPlate)
            {
                n.Logged = true;
                HudNodes.Proof(_driver, FormattableString.Invariant(
                    $"HUD numbers place target={n.TargetId} plate={HudClearance.Format(plate)} number={HudClearance.Format(box)} raw_hit={(raw.Intersects(plate) ? 1 : 0)}"));
                var barHit = 0;
                foreach (var b in bars)
                {
                    barHit |= box.Intersects(b) ? 1 : 0;
                }
                var plateHit = DrawnPlates.Hits(box, real) ? 1 : 0;
                HudNodes.Proof(_driver, FormattableString.Invariant(
                    $"HUD numbers clear target={n.TargetId} text={n.Text} bars={bars.Count} plates={real.Count} bar_hit={barHit} plate_hit={plateHit}"));
            }
        }
    }

    // The HUD frames plates and numbers keep out of: the party frames and the unit frames.
    private List<PixelRect> FrameRects()
    {
        var rects = new List<PixelRect>();
        if (HudObstacles.PartyFrames(GetNodeOrNull<Control>(PartyFramesPath), _driver!.State) is { } frames)
        {
            rects.Add(frames);
        }
        if (GetNodeOrNull<UnitFrames>(UnitFramesPath) is { } units)
        {
            rects.AddRange(units.FrameRects);
        }
        return rects;
    }

    // The ability bar footprint (the player's meters plus the slot row), a block numbers keep out of.
    private PixelRect AbilityBarRect(Vector2 view)
    {
        var bar = AbilityBar.Footprint(view, (_driver!.Setup?.Slots.Count ?? 0) + 1);
        return new PixelRect((int)bar.Position.X, (int)bar.Position.Y, (int)Math.Ceiling(bar.Size.X), (int)Math.Ceiling(bar.Size.Y));
    }

    private void OnRunStarted()
    {
        _numbers.Clear();
        _ended = false;
        _endFrameLogged = false;
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        if (_driver is null || _ended)
        {
            return;
        }
        if (_driver.Run?.RunEnd.Result is not null)
        {
            // The run-end panel opens on this step: nothing floats over it.
            _ended = true;
            var live = _numbers.Count;
            _numbers.Clear();
            HudNodes.Proof(_driver, FormattableString.Invariant($"HUD numbers cleared reason=runend live={live}"));
            return;
        }
        var human = _driver.Setup?.HumanActorId ?? SimDriver.WardenId;
        foreach (var e in events)
        {
            if (CombatTextRule.HumanMiss(e.Type, (e.Get("sourceId") as JsInt)?.Value, (e.Get("targetId") as JsInt)?.Value, human, out var missed))
            {
                // Lane MISS-VIEW: the human's blinded hit (a "miss" event, Mitigation.DealDamage) floats "MISS" over its
                // target; a bot's or an enemy's miss shows nothing. Own proof prefix, so "HUD fct" stays damage-only.
                _numbers.Add(new Number(missed, CombatTextRule.MissText, WorldOf(missed)));
                HudNodes.Proof(_driver, FormattableString.Invariant($"HUD miss source={human} target={missed} text={CombatTextRule.MissText}"));
                continue;
            }
            if (!HudDeclutter.WorldDamageNumber(e, human, out var sourceId, out var targetId, out var amount))
            {
                continue;
            }
            _numbers.Add(new Number(targetId, amount.ToString(System.Globalization.CultureInfo.InvariantCulture), WorldOf(targetId)));
            HudNodes.Proof(_driver, FormattableString.Invariant($"HUD fct source={sourceId} target={targetId} amount={amount}"));
        }
    }

    // Where a new number starts: its target's feet, or the origin when the target is gone.
    private Vector3 WorldOf(int targetId) =>
        _driver!.State.Actors.TryGetValue(targetId, out var target) ? ActorView.ToMetres(target.Pos) : Vector3.Zero;

    private sealed class Number(int targetId, string text, Vector3 world)
    {
        public int TargetId { get; } = targetId;

        public string Text { get; } = text;

        public Vector3 World { get; } = world;

        public float Age { get; set; }

        public PixelRect? Placed { get; set; }

        public bool Logged { get; set; }

        public bool RealLogged { get; set; }
    }
}
