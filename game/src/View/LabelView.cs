using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using KickOrder = Keyrun.Bots.Shared.KickOrder;
using KickSeat = Keyrun.Bots.Shared.KickSeat;
using KitRegistry = Keyrun.Bots.KitRegistry;
using Readiness = Keyrun.Bots.Shared.Readiness;

namespace Keyrun.View;

/// <summary>
/// One floating nameplate per sim actor other than the Warden: the content display name ("Choir Cantor") only
/// (HUD-DECLUTTER: no health number over a head; enemy health is in UnitFrames), and a marker on the Warden's current target. It also gives every actor the run spawns a body: an Actor.tscn instance (ActorView)
/// under <see cref="ActorsPath"/>, coloured by role, unless the scene already holds a view for that id (the Warden's,
/// which CameraRig follows). Plates and bodies of actors that leave the state (a fresh run) are freed.
/// <para>
/// HUD polish (design/ART-PLAN.md section 8 lane 8): plates are drawn in screen space on a 2D layer under the HUD, not
/// as Label3D billboards. Each frame every plate's anchor (<see cref="PlateHeight"/> above its body) is projected with
/// <c>Camera3D.UnprojectPosition</c>, the plates are sorted nearest the camera first, and
/// <see cref="HudLayout"/>.Separate stacks them so no two plates (five Pair Warriors, a Twin beside the boss) and no
/// plate and the top bar or party frames share a pixel, and every plate stays inside the viewport.
/// The font size is fixed, so the boss and Twin plates read the same from the door
/// as up close.
/// </para>
/// <para>
/// NAMEPLATE-DECLUTTER (Matt 2026-10-19, "reduce screen clutter"): before Separate, plates of the SAME name whose boxes
/// share a pixel fold into one plate (<see cref="HudDeclutter"/>.PlateCrowds), drawn at the target's spot when the
/// target is among them, as "Paired Warrior x5" (target: "&gt; Paired Warrior x5 &lt;"), so five Pair Warriors on one
/// spot no longer stand in a tall column. Plates of different names still separate.
/// </para>
/// <para>
/// Lane PLATE-RECTS (finding f-9d4a31a5): every frame's drawn plates, after the fold and the separation, are published
/// as <see cref="PlateRects"/> (each plate's box and the actors folded into it), so CombatText places its numbers clear
/// of the plates where they actually draw instead of a copy of this layout that never folded a crowd.
/// </para>
/// <para>
/// STACKED-PLATES (Matt 2026-10-20, ledger f-e933a2b7: "enemy nameplates stack vertically with interruptible cast bars,
/// hover to interrupt like WoW"): a casting enemy's plate carries its cast bar under the name (except an enemy a unit
/// frame shows, whose cast stays on that frame only, NAMEPLATE-DECLUTTER) (spell and seconds left,
/// fill by elapsed fraction) with a mark left of the bar: an amber diamond and amber frame when the cast can be kicked,
/// a grey square and grey frame when it cannot (shape and colour, BIBLE s3); a kicked cast turns the bar red and a
/// completed one green while it fades (<c>CastBarView.Endings</c>). A casting enemy is never folded into a same-name
/// crowd. Plates are placed by <see cref="PlateStack"/>.Layout (nearest first, lowest id on a tie, moved only up or
/// down, no two blocks share a pixel); CastBarView no longer draws enemy bars over heads. <see cref="PlateRects"/> holds
/// each whole block, which PlayerInput hovers: a hovered enemy plate is the mouseover unit the Q kick goes at.
/// </para>
/// </summary>
public partial class LabelView : Node3D
{
    private const int NameFont = 15;
    private const int CastFont = 12;
    private const int PlatePad = 4;
    private const int OutlineSize = 4;

    private static readonly Color CastBack = new(0.05f, 0.05f, 0.07f, 0.85f);
    private static readonly Color CastFill = new(0.95f, 0.72f, 0.2f, 0.95f);
    private static readonly Color KickColor = new(1f, 0.6f, 0.1f, 1f);
    private static readonly Color LockedColor = new(0.55f, 0.55f, 0.6f, 1f);
    private static readonly Color InterruptedColor = new(0.9f, 0.15f, 0.12f, 1f);
    private static readonly Color DoneColor = new(0.35f, 0.85f, 0.4f, 1f);

    private static readonly Color MeleeColor = new(0.78f, 0.22f, 0.2f);
    private static readonly Color LieutenantColor = new(0.62f, 0.3f, 0.85f);
    private static readonly Color OtherEnemyColor = new(0.85f, 0.45f, 0.15f);
    private static readonly Color PlayerColor = new(0.2f, 0.4f, 0.9f);
    private static readonly Color TargetColor = new(1f, 0.92f, 0.35f);
    private static readonly Color Outline = new(0f, 0f, 0f, 0.85f);

    private readonly Dictionary<int, Plate> _plates = new();
    private readonly Dictionary<int, Node3D> _spawned = new();
    private readonly List<int> _gone = new();
    private readonly List<Shown> _shown = new();
    private readonly List<Drawn> _drawn = new();
    private readonly List<DrawnPlate> _plateRects = new();
    private readonly HashSet<string> _crowdLogged = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _castLogged = new();
    private readonly Dictionary<int, CastBarView.CastEnding> _endings = new();
    private readonly Dictionary<int, int> _kickLogged = new();
    private IReadOnlyDictionary<int, int> _kickNumbers = new Dictionary<int, int>();
    private KitRegistry? _kits;
    // IDLE-PACKS (f-1bf2dace): the packs that have engaged this frame (their members keep one label each).
    private IReadOnlySet<string> _engagedPacks = new HashSet<string>(StringComparer.Ordinal);
    // No pack display names exist on main (packs carry only an id), so every idle pack is labelled by its leader and "+N".
    private static readonly Dictionary<string, string> NoPackNames = new(StringComparer.Ordinal);

    private Control? _canvas;

    /// <summary>The driver to read state from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../SimDriver");

    /// <summary>The node actor bodies live under.</summary>
    [Export]
    public NodePath ActorsPath { get; set; } = new("../Actors");

    /// <summary>The per-actor body scene (Actor.tscn).</summary>
    [Export]
    public PackedScene? ActorScene { get; set; }

    /// <summary>The party frames, a HUD obstacle plates keep clear of (absent: no party-frame obstacle).</summary>
    [Export]
    public NodePath PartyFramesPath { get; set; } = new("../Hud/HudRoot/PartyFrames");

    /// <summary>The enemy unit frames (HUD-DECLUTTER), a HUD obstacle plates keep clear of.</summary>
    [Export]
    public NodePath UnitFramesPath { get; set; } = new("../Hud/HudRoot/UnitFrames");

    /// <summary>The over-head cast bars (CastBarView), whose placed blocks plates keep clear of.</summary>
    [Export]
    public NodePath CastBarsPath { get; set; } = new("../Hud/CastBars");

    /// <summary>The SPREAD/STACK labels and callouts (Pings), whose placed boxes plates keep clear of.</summary>
    [Export]
    public NodePath PingsPath { get; set; } = new("../Hud/HudRoot/Pings");

    /// <summary>
    /// The plates this frame draws, in draw order (lane PLATE-RECTS): each box after the same-name crowd fold and
    /// HudLayout.Separate, and the actors folded into it, the leader first. Laid out in <c>_Process</c>, which runs
    /// before the HUD's (Arena tree order), so CombatText reads the plates of the frame it draws on. Empty with no
    /// camera or no plate on screen.
    /// </summary>
    public IReadOnlyList<DrawnPlate> PlateRects => _plateRects;

    private SimDriver? Driver { get; set; }

    private Node3D? Actors { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        Actors = GetNodeOrNull<Node3D>(ActorsPath);
        // Layer 0 draws over the 3D view and under the HUD CanvasLayer (layer 1).
        var layer = new CanvasLayer { Layer = 0, Name = "Plates" };
        AddChild(layer);
        _canvas = new Control { Name = "PlateCanvas", MouseFilter = Control.MouseFilterEnum.Ignore };
        _canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.Draw += DrawPlates;
        layer.AddChild(_canvas);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Driver is null)
        {
            return;
        }
        var state = Driver.State;
        var targetId = state.Actors.TryGetValue(SimDriver.WardenId, out var me) ? me.TargetId : null;
        var frames = UnitFrameModel.Build(state, Driver.Setup?.HumanActorId ?? SimDriver.WardenId);
        // f-07f1b477: the enemy the boss unit frame names draws no over-head plate (PlateRule.DrawsOverhead).
        var bossFrameId = frames.FirstOrDefault(f => f.Kind == "boss")?.ActorId;
        // f-77a68e6b: enemy plates draw only within PlateRule.PlateDistanceMm of the viewing player (WoW's nameplate
        // max distance), measured from the human's sim position; with no human in the state every enemy is in range.
        var viewerId = Driver.Setup?.HumanActorId ?? SimDriver.WardenId;
        Vec3? viewer = state.Actors.TryGetValue(viewerId, out var viewing) ? viewing.Pos : null;
        // IDLE-PACKS (f-1bf2dace): each enemy's layout pack, and the packs that have engaged: the run's engaged groups
        // (EncounterState.Engaged, the pack_engage and pull_start ids) plus any pack with a living member holding threat.
        var packOf = PackOf();
        var engaged = new HashSet<string>(StringComparer.Ordinal);
        if (Driver.Run is { } run)
        {
            engaged.UnionWith(run.Encounter.Engaged);
        }
        foreach (var (id, packName) in packOf)
        {
            if (state.Actors.TryGetValue(id, out var member) && member.Alive && member.Threat.Count > 0)
            {
                engaged.Add(packName);
            }
        }
        _engagedPacks = engaged;
        foreach (var (id, actor) in state.Actors)
        {
            EnsureBody(id, actor);
            if (!_plates.TryGetValue(id, out var plate))
            {
                plate = new Plate(id, Driver.DisplayName(actor));
                _plates[id] = plate;
            }
            plate.ClassId = actor.ClassId;
            plate.Tint = BodyColor(actor);
            plate.PackId = actor.Kind != ActorKind.Player && packOf.TryGetValue(id, out var pack) ? pack : null;
            // The Warden's own name and health live on the ability bar's meters, not over his head; the boss's name
            // lives on its unit frame.
            // LABEL-CULL: the one rule every label reads (LabelGroups.Labelled). A dead actor's plate hides on the frame its
            // state carries the death, and it is in no "xN" group (LabelGroups.Build), so it never counts.
            plate.Visible = LabelGroups.Labelled(new LabelActor(id, plate.BaseName, actor.Alive,
                Enemy: actor.Kind != ActorKind.Player, Human: id == SimDriver.WardenId,
                DistanceMm: viewer is { } v ? DistanceMm(v, actor.Pos) : 0), bossFrameId);
            if (!plate.Visible)
            {
                continue;
            }
            if (!plate.Logged)
            {
                // HUD-DECLUTTER proof: the lines this plate draws ("name" only; health lives in the unit frames).
                plate.Logged = true;
                HudNodes.Proof(Driver, $"HUD plate actor={id} lines={string.Join(",", Plate.Lines)}");
            }
            var before = Driver.PreviousState.Actors.TryGetValue(id, out var prev) ? prev : actor;
            var t = Driver.Alpha;
            plate.Anchor = ActorView.ToMetres(before.Pos).Lerp(ActorView.ToMetres(actor.Pos), t) + new Vector3(0, PlateHeight(actor), 0);
            plate.Targeted = id == targetId;
            // NAMEPLATE-DECLUTTER still holds: an enemy a unit frame shows (the boss, the Warden's target) has its cast on
            // that frame only, so its plate carries no second bar (HudDeclutter.OverheadCast).
            plate.CastOnPlate = actor.Kind != ActorKind.Player && HudDeclutter.OverheadCast(actor, frames);
            plate.CastId = plate.CastOnPlate ? actor.CastId : null;
            plate.CastStart = actor.CastStartTick;
            plate.CastEnd = actor.CastEndsTick;
            LogPlateCast(id, plate);
        }
        _endings.Clear();
        if (GetNodeOrNull<CastBarView>(CastBarsPath) is { } castBars)
        {
            foreach (var e in castBars.Endings)
            {
                _endings[e.ActorId] = e;
            }
        }

        _kickNumbers = KickNumbers();

        _gone.Clear();
        foreach (var id in _plates.Keys)
        {
            if (!state.Actors.ContainsKey(id))
            {
                _gone.Add(id);
            }
        }
        foreach (var id in _gone)
        {
            _plates.Remove(id);
            if (_spawned.Remove(id, out var body))
            {
                body.QueueFree();
            }
        }
        LayoutPlates();
        _canvas?.QueueRedraw();
    }

    private void DrawPlates()
    {
        if (_canvas is null)
        {
            return;
        }
        var font = ThemeDB.FallbackFont;
        foreach (var d in _drawn)
        {
            var name = d.Placed.Name;
            var centre = name.X + (name.Width / 2f);
            Line(font, new Vector2(centre, name.Y + PlatePad + NameFont), d.Text, NameFont, d.Targeted ? TargetColor : d.Tint ?? Colors.White);
            if (d.Chevron)
            {
                DrawChevron(centre, name.Y, d.Tint ?? Colors.White);
            }
            DrawCast(font, d);
            DrawKickNumber(font, d);
        }
    }

    // IDLE-PACKS: the chevron on an idle pack's single label, drawn in the pack's threat colour over a black outline. The
    // shape marks the label as a pack, so it is not colour alone (BIBLE s3).
    private void DrawChevron(float centre, int top, Color color)
    {
        var y = top + 2f;
        var left = new Vector2(centre - 4f, y);
        var tip = new Vector2(centre, y + 4f);
        var right = new Vector2(centre + 4f, y);
        _canvas!.DrawLine(left, tip, Outline, 4f);
        _canvas.DrawLine(tip, right, Outline, 4f);
        _canvas.DrawLine(left, tip, color, 2f);
        _canvas.DrawLine(tip, right, color, 2f);
    }

    // KICK-ORDER (BIBLE s3 "A suggested kick order"): a kickable caster's plate carries its kick number, the place in the
    // party's kick order its kick comes off cooldown (1 kicks next). The number is drawn left of the cast's mark, in the
    // cast text size with the outline; the shape is the digit, not a colour.
    private void DrawKickNumber(Font font, Drawn d)
    {
        if (d.Cast is null || d.Placed.Mark is not { } markBox || !_kickNumbers.TryGetValue(d.Placed.Id, out var number))
        {
            return;
        }
        var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var at = new Vector2(markBox.X - HudNodes.Width(text, CastFont) - 4f, markBox.Y + markBox.Height - 2);
        _canvas!.DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, CastFont, 3, Outline);
        _canvas.DrawString(font, at, text, HorizontalAlignment.Left, -1, CastFont, Colors.White);
    }

    // KICK-ORDER: the party's kick places this frame (KickOrder.Compute over every living player whose class has an
    // interrupt, read from its cooldown) and the number each kickable caster carries (KickOrder.NumberCasters, casters in
    // actor-id order). One "HUD kick order seat=S n=N" host line per seat whose place changes.
    private IReadOnlyDictionary<int, int> KickNumbers()
    {
        if (Driver is not { } driver)
        {
            return _kickNumbers;
        }
        var kits = driver.Registry is { } registry ? (_kits ??= KitRegistry.From(registry)) : KitRegistry.Empty;
        var state = driver.State;
        var seats = new List<KickSeat>();
        foreach (var a in state.Actors.Values)
        {
            if (a.Kind != ActorKind.Player || !a.Alive || kits.GetClassKit(a.ClassId)?.InterruptAbilityId is not { Length: > 0 } interrupt)
            {
                continue;
            }
            seats.Add(new KickSeat(a.Id, Readiness.CooldownReadyTick(a, interrupt)));
        }
        var order = KickOrder.Compute(seats, state.Tick);
        var casters = state.Actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.Alive && a.CastId is { } castId && MarkOf(castId) == CastMark.Kick)
            .Select(a => a.Id)
            .ToList();
        foreach (var slot in order)
        {
            if (!_kickLogged.TryGetValue(slot.SeatId, out var last) || last != slot.Order)
            {
                _kickLogged[slot.SeatId] = slot.Order;
                driver.HostLine(FormattableString.Invariant($"HUD kick order seat={slot.SeatId} n={slot.Order}"));
            }
        }
        return KickOrder.NumberCasters(casters, order);
    }

    // STACKED-PLATES: the cast rows under a plate's name: the spell label, the kick/locked mark (diamond or square, amber
    // or grey) and the bar (filled by elapsed fraction, framed in the mark's colour); a just-ended cast fades red
    // (kicked) or green (completed) at full width.
    private void DrawCast(Font font, Drawn d)
    {
        if (d.Placed.CastBar is not { } bar || d.Placed.Mark is not { } markBox || PlateStack.CastLabel(d.Placed) is not { } band)
        {
            return;
        }
        Color frame;
        Color fill;
        float progress;
        float alpha;
        string label;
        CastMark mark;
        if (d.Cast is { } cast)
        {
            mark = cast.Mark;
            frame = mark == CastMark.Kick ? KickColor : LockedColor;
            fill = CastFill;
            progress = cast.Progress;
            alpha = 1f;
            label = cast.Label;
        }
        else if (d.Ending is { } end)
        {
            mark = CastMark.Locked;
            alpha = Mathf.Clamp(end.Opacity, 0f, 1f);
            frame = new Color(end.Interrupted ? InterruptedColor : DoneColor, alpha);
            fill = frame;
            progress = 1f;
            label = end.Interrupted ? $"{end.Name} interrupted" : end.Name;
        }
        else
        {
            return;
        }
        var barRect = new Rect2(bar.X, bar.Y, bar.Width, bar.Height);
        _canvas!.DrawRect(barRect, new Color(CastBack, CastBack.A * alpha));
        _canvas.DrawRect(new Rect2(bar.X, bar.Y, bar.Width * progress, bar.Height), fill);
        _canvas.DrawRect(barRect, frame, filled: false, width: 2f);
        if (d.Cast is not null)
        {
            var points = PlateStack.MarkShape(mark, markBox);
            var poly = new Vector2[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                poly[i] = new Vector2(points[i].X, points[i].Y);
            }
            _canvas.DrawColoredPolygon(poly, frame);
        }
        var text = HudLayout.FitTitle(label, band.Width, s => (int)Math.Ceiling(HudNodes.Width(s, CastFont)));
        var at = new Vector2(band.X + ((band.Width - HudNodes.Width(text, CastFont)) / 2f), band.Bottom - 3);
        _canvas.DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, CastFont, 3, new Color(Outline, Outline.A * alpha));
        _canvas.DrawString(font, at, text, HorizontalAlignment.Left, -1, CastFont, new Color(1, 1, 1, alpha));
    }

    // STACKED-PLATES proof (autorun only): "HUD platecast actor=A ability=X mark=kick|locked" once per cast start.
    private void LogPlateCast(int id, Plate plate)
    {
        if (Driver is null || !Driver.Options.Autorun || plate.CastId is not { } castId || plate.CastStart is not { } start)
        {
            return;
        }
        if (_castLogged.TryGetValue(id, out var last) && last == start)
        {
            return;
        }
        _castLogged[id] = start;
        var mark = MarkOf(castId) == CastMark.Kick ? "kick" : "locked";
        HudNodes.Proof(Driver, $"HUD platecast actor={id} ability={castId} mark={mark}");
    }

    private CastMark MarkOf(string castId) =>
        PlateStack.MarkOf(Driver?.Run?.Content.Abilities.TryGetValue(castId, out var def) == true && def.Interruptible);

    // The live cast a plate draws this frame, or null when its actor is not casting.
    private PlateCast? CastOf(Plate plate)
    {
        if (Driver is null || plate.CastId is not { } castId || plate.CastStart is not { } start || plate.CastEnd is not { } end)
        {
            return null;
        }
        var now = Driver.State.Tick + Driver.Alpha;
        var progress = Mathf.Clamp((now - start) / Mathf.Max(1f, end - start), 0f, 1f);
        var secondsLeft = Mathf.Max(0f, (end - now) / Tick.TickHz);
        var label = HudLayout.FitCastLabel(string.Empty, Driver.AbilityName(castId), FormattableString.Invariant($"  {secondsLeft:0.0}s"),
            PlateStack.CastWidth, s => (int)Math.Ceiling(HudNodes.Width(s, CastFont)),
            // The bar sits inside the caster's own on-screen plate, whose text already names the caster.
            casterPlateShown: true);
        return new PlateCast(MarkOf(castId), progress, label);
    }

    // Places this frame's labels (every process frame, so the proof lines report what draws): project, sort nearest
    // first, draw each idle pack as ONE label (IdlePackLabels.Plan, f-1bf2dace), fold same-name crowds
    // (HudDeclutter.PlateCrowds), then HudLayout.Separate.
    private void LayoutPlates()
    {
        _drawn.Clear();
        _plateRects.Clear();
        var camera = GetViewport()?.GetCamera3D();
        if (_canvas is null || camera is null)
        {
            return;
        }
        var view = GetViewport().GetVisibleRect().Size;
        var width = (int)view.X;
        var height = (int)view.Y;

        // Project every visible plate whose anchor is in front of the camera and on screen; nearest first.
        var origin = camera.GlobalPosition;
        var projected = new List<(Plate Plate, PixelPoint Anchor, float Depth)>();
        foreach (var plate in _plates.Values)
        {
            if (PlateOnScreen(camera, plate, width, height, out var anchor))
            {
                projected.Add((plate, anchor, origin.DistanceSquaredTo(plate.Anchor)));
            }
        }
        if (projected.Count == 0)
        {
            return;
        }
        projected.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        // IDLE-PACKS (f-1bf2dace): an idle pack draws one label at its centre; an engaged pack's members keep one label
        // each; a label whose box would leave the viewport is hidden, not clamped (IdlePackLabels.Plan).
        var labels = IdlePackLabels.Plan(
            projected.Select(p => new IdlePackMember(p.Plate.Id, p.Plate.BaseName, p.Plate.ClassId, true, p.Anchor.X, p.Anchor.Y, p.Plate.PackId)).ToList(),
            _engagedPacks, NoPackNames, width, height, s => (int)Math.Ceiling(HudNodes.Width(s, NameFont)), NameFont);
        var byId = projected.ToDictionary(p => p.Plate.Id);
        _shown.Clear();
        foreach (var label in labels)
        {
            var members = label.ActorIds.Select(id => byId[id]).ToList();
            var anchor = new PixelPoint(label.X, label.Y);
            var leader = byId[label.LeaderId].Plate;
            _shown.Add(new Shown(leader, PlateRect(anchor, label.Text), anchor, members.Min(m => m.Depth), label.Text, label.Chevron,
                label.Chevron, members.Any(m => m.Plate.Targeted), label.Chevron ? leader.Tint : null, label.ActorIds));
        }
        if (_shown.Count == 0)
        {
            return;
        }
        _shown.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        // STACKED-PLATES: a casting enemy (or one whose cast is fading) keeps a plate of its own (PlateStack.FoldKey). An
        // idle pack's label folds only with itself (its key carries the pack id).
        var crowds = HudDeclutter.PlateCrowds(_shown.Select(s => new PlateCandidate(
            s.Idle ? "pack:" + s.Plate.PackId : PlateStack.FoldKey(s.Plate.BaseName, s.Plate.Id, ShowsCast(s.Plate)), s.Rect, s.Targeted)).ToList());
        var specs = new List<PlateSpec>(crowds.Count);
        var byLeader = new Dictionary<int, (string Text, bool Targeted, int Crowd, PlateCast? Cast, CastBarView.CastEnding? Ending, bool Chevron, Color? Tint, IReadOnlyList<int> Folded)>();
        for (var ci = 0; ci < crowds.Count; ci++)
        {
            // NAMEPLATE-DECLUTTER: one plate per same-name crowd, at its leader (the target when it is in the crowd).
            var c = crowds[ci];
            var s = _shown[c.Leader];
            var targeted = c.Members.Any(m => _shown[m].Targeted);
            var text = HudDeclutter.PlateText(s.Text, c.Members.Count, targeted);
            // An idle pack's label is not casting: its members are idle, so no cast or cast fade draws on it.
            var cast = s.Idle ? null : CastOf(s.Plate);
            CastBarView.CastEnding? ending = cast is null && !s.Idle && s.Plate.CastOnPlate && _endings.TryGetValue(s.Plate.Id, out var e) ? e : null;
            byLeader[s.Plate.Id] = (text, targeted, ci, cast, ending, s.Chevron, s.Tint, c.Members.SelectMany(m => _shown[m].Folded).ToArray());
            specs.Add(new PlateSpec(s.Plate.Id, s.Anchor, PlateRect(s.Anchor, text).Width, (long)Math.Round(s.Depth * 1000f), cast is not null || ending is not null));
        }
        foreach (var placed in PlateStack.Layout(specs, width, height, Obstacles(width, height)))
        {
            var info = byLeader[placed.Id];
            var drawn = new Drawn(info.Text, placed, info.Targeted, info.Crowd, info.Cast, info.Ending, info.Chevron, info.Tint);
            _drawn.Add(drawn);
            _plateRects.Add(new DrawnPlate(placed.Block, info.Folded.ToArray()));
        }
        LogCrowds(crowds);
        LogIdlePacks();
    }

    // IDLE-PACKS: the layout pack each enemy belongs to (PackGroup.Spawns), keyed by actor id. Empty on a flat arena.
    private Dictionary<int, string> PackOf()
    {
        var packOf = new Dictionary<int, string>();
        if (Driver?.Run is { } run && run.Content.Packs is { } packs)
        {
            foreach (var pack in packs)
            {
                foreach (var spawn in pack.Spawns)
                {
                    packOf[spawn.ActorId] = pack.Id;
                }
            }
        }
        return packOf;
    }

    // IDLE-PACKS proof (autorun only): "HUD idlepack pack=ID label="TEXT" members=N" once per distinct line, for each idle
    // pack label that survived the edge test this frame.
    private void LogIdlePacks()
    {
        if (Driver is null || !Driver.Options.Autorun)
        {
            return;
        }
        foreach (var s in _shown)
        {
            if (!s.Idle)
            {
                continue;
            }
            var line = FormattableString.Invariant($"HUD idlepack pack={s.Plate.PackId} label=\"{s.Text}\" members={s.Folded.Count}");
            if (_crowdLogged.Add(line))
            {
                HudNodes.Proof(Driver, line);
            }
        }
    }

    // True when the plate shows a cast this frame: an enemy casting, or its just-ended cast still fading.
    private bool ShowsCast(Plate plate) => plate.CastOnPlate && (plate.CastId is not null || _endings.ContainsKey(plate.Id));

    private static PixelRect PlateRect(PixelPoint anchor, string text)
    {
        var w = (int)Math.Ceiling(HudNodes.Width(text, NameFont)) + (2 * PlatePad);
        var h = NameFont + (2 * PlatePad);
        return new PixelRect(anchor.X - (w / 2), anchor.Y - h, w, h);
    }

    // NAMEPLATE-DECLUTTER proof (autorun only): "HUD platecrowd name="N" crowd=C drawn=D target=T marked=M" when C >= 2
    // same-name plates share one spot: D plates of the crowd draw, T is 1 when the Warden's target is in it, M is 1 when
    // a drawn plate of it carries the "> name <" mark. Printed once per distinct line.
    private void LogCrowds(IReadOnlyList<PlateCrowd> crowds)
    {
        if (Driver is null || !Driver.Options.Autorun)
        {
            return;
        }
        for (var ci = 0; ci < crowds.Count; ci++)
        {
            var c = crowds[ci];
            if (c.Members.Count < 2)
            {
                continue;
            }
            var name = _shown[c.Leader].Text;
            var target = c.Members.Any(m => _shown[m].Targeted);
            var drawn = _drawn.Count(d => d.Crowd == ci);
            var mark = _drawn.Any(d => d.Crowd == ci && d.Targeted && d.Text.StartsWith("> ", StringComparison.Ordinal));
            var line = FormattableString.Invariant(
                $"HUD platecrowd name=\"{name}\" crowd={c.Members.Count} drawn={drawn} target={(target ? 1 : 0)} marked={(target && mark ? 1 : 0)}");
            if (_crowdLogged.Add(line))
            {
                HudNodes.Proof(Driver, line);
            }
        }
    }

    private void Line(Font font, Vector2 baselineCentre, string text, int size, Color color)
    {
        var at = baselineCentre - new Vector2(HudNodes.Width(text, size) / 2f, 0);
        _canvas!.DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, size, OutlineSize, Outline);
        _canvas.DrawString(font, at, text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>
    /// The HUD rectangles plates keep out of: the top bar band (help line, objective, Slack), the party frames, every
    /// live or fading cast bar over a head where CastBarView placed it (<c>CastBarView.BarRects</c>) and every
    /// SPREAD/STACK world label and callout where Pings placed it (<c>Pings.LabelRects</c>), so a plate never draws
    /// under another HUD text. Plates are placed last, so they yield to the bars and labels, never the other way.
    /// </summary>
    private List<PixelRect> Obstacles(int width, int height)
    {
        var obstacles = new List<PixelRect> { HudLayout.TopBar(width, height).Band(width) };
        if (Driver is null)
        {
            return obstacles;
        }
        if (HudObstacles.PartyFrames(GetNodeOrNull<Control>(PartyFramesPath), Driver.State) is { } frames)
        {
            obstacles.Add(frames);
        }
        if (GetNodeOrNull<UnitFrames>(UnitFramesPath) is { } units)
        {
            obstacles.AddRange(units.FrameRects);
        }
        if (GetNodeOrNull<CastBarView>(CastBarsPath) is { } bars)
        {
            obstacles.AddRange(bars.BarRects);
        }
        if (GetNodeOrNull<Pings>(PingsPath) is { } pings)
        {
            obstacles.AddRange(pings.LabelRects);
        }
        return obstacles;
    }

    /// <summary>
    /// True when <paramref name="actorId"/>'s name plate is drawn this frame, alone or as a member of a same-name crowd
    /// plate (which still shows the name): the plate is visible (alive, not the Warden) and its anchor projects in front
    /// of the camera inside the viewport, the same test LayoutPlates places plates by. CastBarView asks this so a plated
    /// caster's cast label names only the ability (f-b78d1567, <c>HudLayout.FitCastLabel</c>).
    /// </summary>
    /// <param name="actorId">The actor.</param>
    /// <returns>Whether a plate names that actor on screen.</returns>
    public bool ShowsPlate(int actorId)
    {
        var camera = GetViewport()?.GetCamera3D();
        if (camera is null || !_plates.TryGetValue(actorId, out var plate))
        {
            return false;
        }
        var view = GetViewport().GetVisibleRect().Size;
        return PlateOnScreen(camera, plate, (int)view.X, (int)view.Y, out _);
    }

    // A visible plate whose anchor is in front of the camera and inside the viewport, with its projected pixel.
    private static bool PlateOnScreen(Camera3D camera, Plate plate, int width, int height, out PixelPoint anchor)
    {
        anchor = default;
        if (!plate.Visible || camera.IsPositionBehind(plate.Anchor))
        {
            return false;
        }
        var screen = camera.UnprojectPosition(plate.Anchor);
        if (screen.X < 0 || screen.X > width || screen.Y < 0 || screen.Y > height)
        {
            return false;
        }
        anchor = new PixelPoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        return true;
    }

    private void EnsureBody(int id, ActorState actor)
    {
        if (_spawned.ContainsKey(id) || Actors is null || ActorScene is null || HasStaticView(id))
        {
            return;
        }
        var view = ActorScene.Instantiate<ActorView>();
        view.ActorId = id;
        view.BodyColor = BodyColor(actor);
        view.Name = $"Actor{id}";
        Actors.AddChild(view);
        _spawned[id] = view;
    }

    private bool HasStaticView(int id)
    {
        foreach (var child in Actors!.GetChildren())
        {
            if (child is ActorView v && v.ActorId == id && !_spawned.ContainsValue(v))
            {
                return true;
            }
        }
        return false;
    }

    private static Color BodyColor(ActorState actor) => actor.Kind switch
    {
        ActorKind.Player => PlayerColor,
        _ when actor.ClassId.EndsWith(".lieutenant", StringComparison.Ordinal) => LieutenantColor,
        _ when actor.ClassId.EndsWith(".melee", StringComparison.Ordinal) => MeleeColor,
        _ => OtherEnemyColor,
    };

    // The plate anchor above the feet, metres, per class (ActorBindingRow.PlateHeightMm via PlateRule: 250 mm over the
    // higher of the target height and the scaled model top, the boss capped at 5 m), so a plate sits just over its head.
    private static float PlateHeight(ActorState actor) => ActorView.Binding.For(actor.ClassId).PlateHeightMm * 0.001f;

    // Straight-line distance between two sim positions, mm (integer floor square root), for PlateRule's plate range.
    private static long DistanceMm(Vec3 a, Vec3 b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        long sq = (dx * dx) + (dy * dy) + (dz * dz);
        return sq <= 0 ? 0 : Fixed.Isqrt(sq);
    }

    private sealed class Plate(int id, string baseName)
    {
        /// <summary>The lines every plate draws: the name (HUD-DECLUTTER: no health number over a head).</summary>
        public static readonly string[] Lines = { "name" };

        public int Id { get; } = id;

        public string BaseName { get; } = baseName;

        public bool Visible { get; set; }

        public bool Logged { get; set; }

        public Vector3 Anchor { get; set; }

        public bool Targeted { get; set; }

        public bool CastOnPlate { get; set; }

        /// <summary>The enemy's live cast (null for a party member or an enemy not casting).</summary>
        public string? CastId { get; set; }

        public int? CastStart { get; set; }

        public int? CastEnd { get; set; }

        /// <summary>The actor's class id (".lieutenant" marks a pack leader; IDLE-PACKS).</summary>
        public string ClassId { get; set; } = string.Empty;

        /// <summary>The layout pack the enemy belongs to, or null (IDLE-PACKS, f-1bf2dace).</summary>
        public string? PackId { get; set; }

        /// <summary>The actor's threat colour (BodyColor), kept by an idle pack's single label.</summary>
        public Color Tint { get; set; }
    }

    // One label this frame: a plain actor's plate, or an idle pack's single label (Idle, Chevron) anchored at the pack's centre.
    private readonly record struct Shown(Plate Plate, PixelRect Rect, PixelPoint Anchor, float Depth, string Text, bool Idle,
        bool Chevron, bool Targeted, Color? Tint, IReadOnlyList<int> Folded);

    private readonly record struct PlateCast(CastMark Mark, float Progress, string Label);

    private readonly record struct Drawn(string Text, StackedPlate Placed, bool Targeted, int Crowd, PlateCast? Cast, CastBarView.CastEnding? Ending,
        bool Chevron, Color? Tint);
}
