using System;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The named ability bar (BIBLE s2): the human's nine ability slots in key order (1-5 rotational, Q interrupt,
/// E defensive, R utility, F movement) plus the G ping, each with its key, the ability's content name, and its state:
/// a dark sweep with seconds left while on cooldown, a light sweep during the global cooldown, dimmed when the
/// class resource is short, its icon tinted red (WoW style, no red border) with an "out of range" caption (finding
/// f-4e8c87b7: the reason is written, never colour alone; "HUD bar slot=K range=out" under --autorun) when the target is out of range
/// (<see cref="Keyrun.Content.AbilitySlotLook"/>, lane ABILITYBAR-RANGE-TINT). Above it: the Warden's health and
/// Momentum. The run-over state (result, rewards, the Enter hint) is the run-end panel's (<see cref="RunEndPanel"/>).
/// Each ability slot draws its CC0 icon (<see cref="Keyrun.Content.AbilityIcons"/>, lane ABILITY-ICONS) in place of the
/// name text; a slot whose ability has no icon row keeps the name. Under --autorun it prints one "HUD bar slot=K
/// ability=ID icon=PATH tex=WxH" line per slot when a run's setup arrives (HudSceneTest check 40). Lane CHARGE-COOLDOWN:
/// a multi-charge ability shows its ready charges in the corner and reads on cooldown only with none ready, counting
/// down to the NEXT charge (<see cref="ChargeReadout"/>, the sim's own charge timestamp), not to the last one.
/// </summary>
public partial class AbilityBar : Control
{
    private const float Slot = 78;
    private const float IconSize = 46;
    private const float CooldownIconAlpha = 0.35f; // the icon fades under the cooldown sweep so the seconds stay readable
    private static readonly Color OutOfRangeTint = ToColor(Keyrun.Content.AbilitySlotLook.OutOfRangeTint);
    private static readonly Color KeyShadow = new(0f, 0f, 0f, 0.85f);
    private const float Gap = 8;
    private const float BottomMargin = 24;
    private const int KeyFont = 18;
    private const int NameFont = 13;
    private const int CaptionFont = 12;

    private static readonly Color SlotBack = new(0.1f, 0.11f, 0.14f, 0.92f);
    private static readonly Color SlotEdge = ToColor(Keyrun.Content.AbilitySlotLook.SlotEdge);
    private static readonly Color CooldownShade = new(0f, 0f, 0f, 0.62f);
    private static readonly Color GcdShade = new(1f, 1f, 1f, 0.16f);
    private static readonly Color HealthFill = new(0.25f, 0.75f, 0.35f, 1f);
    private static readonly Color MomentumFill = new(0.95f, 0.65f, 0.15f, 1f);

    /// <summary>The driver to read state, setup and names from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../SimDriver");

    /// <summary>The input node (key labels come from its bindings).</summary>
    [Export]
    public NodePath InputPath { get; set; } = new("../../PlayerInput");

    private SimDriver? Driver { get; set; }

    private PlayerInput? KeyInput { get; set; }

    // The setup the icons below were resolved for, and one texture (or null) per setup slot.
    private HostRunSetup? _iconSetup;
    private Texture2D?[] _icons = Array.Empty<Texture2D?>();

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        KeyInput = GetNodeOrNull<PlayerInput>(InputPath);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        RefreshIcons();
        if (Driver?.Options.Autorun == true)
        {
            PrintRangeTint();
        }
        if (Driver?.Options is { } o && (o.Autorun || o.ReplayInput is not null))
        {
            PrintCharges();
        }
        QueueRedraw();
    }

    // The ready-charge count last printed per multi-charge ability, so a "HUD bar charge" line prints only when that
    // count changes (lane CHARGE-COOLDOWN), not on every tick of the countdown.
    private readonly System.Collections.Generic.Dictionary<string, long> _lastChargeCount = new();

    // Under --autorun or --replay-input: "HUD bar charge ability=ID charges=C/M shown=S tick=T" for each slot whose
    // ability has more than one charge, S the seconds the slot draws at that tick ("none" while a charge is ready).
    // Printed when the ready-charge count changes.
    private void PrintCharges()
    {
        if (Driver?.Setup is not { } setup || Driver.Run is not { } run || !run.State.Actors.TryGetValue(setup.HumanActorId, out var me))
        {
            return;
        }
        foreach (var slot in setup.Slots)
        {
            if (!run.Content.Abilities.TryGetValue(slot.AbilityId, out var def) || def.Charges < 2)
            {
                continue;
            }
            var readout = ChargeReadout.For(me, def, run.State.Tick);
            if (_lastChargeCount.TryGetValue(def.Id, out var last) && last == readout.Charges)
            {
                continue;
            }
            _lastChargeCount[def.Id] = readout.Charges;
            var shown = readout.OnCooldown ? CooldownSeconds(readout) : "none";
            GD.Print(FormattableString.Invariant($"HUD bar charge ability={def.Id} charges={readout.Charges}/{readout.MaxCharges} shown={shown} tick={run.State.Tick}"));
        }
    }

    // The seconds a slot on cooldown draws: the wait until the NEXT charge, so a two-charge ability spent twice reads
    // the time until it can be cast again, not until both charges are back (lane CHARGE-COOLDOWN).
    private static string CooldownSeconds(ChargeReadout readout) =>
        FormattableString.Invariant($"{readout.TicksToNextCharge / (float)Tick.TickHz:0.0}");

    // The last "HUD bar range" line printed, so the line prints only when the tinted-slot count or the run-over state
    // changes (lane TINT-RUNOVER, HudSceneTest check 41).
    private string? _lastRangeLine;

    // The slot keys last printed as out of range (finding f-4e8c87b7), so each slot logs only when its state flips.
    private readonly System.Collections.Generic.HashSet<string> _slotOutOfRange = new();

    // Under --autorun: "HUD bar range tinted=N over=0|1 tick=T", N the slots the bar draws with the out-of-range tint,
    // over 1 once the run has ended for any result. Printed on change only.
    private void PrintRangeTint()
    {
        if (Driver?.Setup is not { } setup || Driver.Run is not { } run || !run.State.Actors.TryGetValue(setup.HumanActorId, out var me))
        {
            return;
        }
        var tinted = 0;
        foreach (var slot in setup.Slots)
        {
            var look = run.Content.Abilities.TryGetValue(slot.AbilityId, out var def) ? RangeLook(run, me, def) : default;
            if (look.OutOfRange)
            {
                tinted++;
            }
            // Finding f-4e8c87b7: one line per slot whose range state flips, naming the caption the slot draws, so a
            // capture can be matched to the reason on screen ("HUD bar slot=K range=out caption=\"out of range\"").
            var was = _slotOutOfRange.Contains(slot.Key);
            if (look.OutOfRange != was)
            {
                if (look.OutOfRange)
                {
                    _slotOutOfRange.Add(slot.Key);
                    GD.Print(FormattableString.Invariant($"HUD bar slot={slot.Key} range=out caption=\"{look.Caption}\" tick={run.State.Tick}"));
                }
                else
                {
                    _slotOutOfRange.Remove(slot.Key);
                    GD.Print(FormattableString.Invariant($"HUD bar slot={slot.Key} range=in tick={run.State.Tick}"));
                }
            }
        }
        var line = FormattableString.Invariant($"HUD bar range tinted={tinted} over={(run.RunEnd.Result is not null ? 1 : 0)}");
        if (line == _lastRangeLine)
        {
            return;
        }
        _lastRangeLine = line;
        GD.Print(FormattableString.Invariant($"{line} tick={run.State.Tick}"));
    }

    // Loads each slot's icon once per run setup (not per frame) and, under --autorun, prints what each slot resolved.
    private void RefreshIcons()
    {
        if (Driver?.Setup is not { } setup || ReferenceEquals(setup, _iconSetup))
        {
            return;
        }
        _iconSetup = setup;
        _icons = new Texture2D?[setup.Slots.Count];
        for (var i = 0; i < setup.Slots.Count; i++)
        {
            var slot = setup.Slots[i];
            var path = Keyrun.Content.AbilityIcons.Default.ResPathFor(slot.AbilityId);
            var tex = path is not null && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            _icons[i] = tex;
            if (Driver.Options.Autorun)
            {
                var size = tex is null ? "none" : $"{tex.GetWidth()}x{tex.GetHeight()}";
                GD.Print($"HUD bar slot={slot.Key} ability={slot.AbilityId} icon={path ?? "none"} tex={size}");
            }
        }
    }

    /// <summary>
    /// The screen rect the bar draws in (its health and momentum meters on top, the slot row below), pixels, for a
    /// control of <paramref name="size"/> holding <paramref name="count"/> slots (the setup's slots plus the G ping).
    /// The 3D views use it to keep world geometry from standing in front of it (GATE-FOREGROUND).
    /// </summary>
    /// <param name="size">The control's size (the viewport: the bar is a full-rect control).</param>
    /// <param name="count">Slots drawn, the ping included.</param>
    /// <returns>The bar's footprint.</returns>
    public static Rect2 Footprint(Vector2 size, int count)
    {
        var width = (count * Slot) + ((count - 1) * Gap);
        var top = size.Y - BottomMargin - Slot - 44;
        return new Rect2((size.X - width) / 2, top, width, 44 + Slot);
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (Driver?.Setup is not { } setup || Driver.Run is not { } run || !run.State.Actors.TryGetValue(setup.HumanActorId, out var me))
        {
            return;
        }
        var font = ThemeDB.FallbackFont;
        var count = setup.Slots.Count + 1; // + the G ping
        var bar = Footprint(Size, count);
        var width = bar.Size.X;
        var origin = new Vector2(bar.Position.X, bar.End.Y - Slot);
        var now = run.State.Tick;

        DrawMeter(font, new Rect2(origin.X, origin.Y - 44, width, 16), me.Health, me.MaxHealth, HealthFill, $"{Driver.DisplayName(me)}  {me.Health} / {me.MaxHealth}");
        DrawMeter(font, new Rect2(origin.X, origin.Y - 24, width, 14), me.Resource, me.MaxResource, MomentumFill, $"{Keyrun.Content.ResourceLabel.For(Driver.Registry!.Classes[me.ClassId].Loop.Resource)} {me.Resource}");

        for (var i = 0; i < setup.Slots.Count; i++)
        {
            var slot = setup.Slots[i];
            var rect = new Rect2(origin.X + (i * (Slot + Gap)), origin.Y, Slot, Slot);
            var key = KeyInput is not null ? KeyBinds.Label(KeyInput.Binds.KeyFor(slot.Action)) : slot.Key;
            var icon = ReferenceEquals(setup, _iconSetup) && i < _icons.Length ? _icons[i] : null;
            if (!run.Content.Abilities.TryGetValue(slot.AbilityId, out var def))
            {
                DrawSlot(font, rect, key, slot.AbilityId, 1f, InRange, false, icon);
                continue;
            }
            var cdFraction = 0f;
            string? cdText = null;
            var charges = ChargeReadout.For(me, def, now);
            if (charges.OnCooldown)
            {
                cdFraction = Mathf.Clamp((float)charges.TicksToNextCharge / charges.CooldownTicks, 0f, 1f);
                cdText = CooldownSeconds(charges);
            }
            var gcd = def.GcdTicks > 0 && me.GcdReadyTick > now ? Mathf.Clamp((float)(me.GcdReadyTick - now) / def.GcdTicks, 0f, 1f) : 0f;
            var shortResource = def.Cost.Resource != "none" && me.Resource < def.Cost.Amount;
            var range = RangeLook(run, me, def);
            DrawSlot(font, rect, key, Driver.AbilityName(def.Id), shortResource ? 0.45f : 1f, range, false, icon, cdFraction > 0f ? CooldownIconAlpha : 1f);
            if (cdFraction > 0f)
            {
                DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * cdFraction)), CooldownShade);
                DrawCentered(font, rect, cdText!, 22, Colors.White);
            }
            else if (gcd > 0f)
            {
                DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * gcd)), GcdShade);
            }
            if (charges.MaxCharges > 1)
            {
                // WoW style: a multi-charge ability shows its ready charges in the bottom-right corner.
                var ready = FormattableString.Invariant($"{charges.Charges}");
                var w = font.GetStringSize(ready, HorizontalAlignment.Left, -1, KeyFont).X;
                var at = new Vector2(rect.End.X - w - 6, rect.End.Y - 6);
                DrawStringOutline(font, at, ready, HorizontalAlignment.Left, -1, KeyFont, 4, Colors.Black);
                DrawString(font, at, ready, HorizontalAlignment.Left, -1, KeyFont, Colors.White);
            }
        }

        var pingRect = new Rect2(origin.X + (setup.Slots.Count * (Slot + Gap)), origin.Y, Slot, Slot);
        var pingKey = KeyInput is not null ? KeyBinds.Label(KeyInput.Binds.KeyFor(KeyAction.Ping)) : "G";
        DrawSlot(font, pingRect, pingKey, "Ping", 0.8f, InRange, true);
    }

    // Lane RANGE-TINT-NOTARGET: the tint reads off the player's own selected target, never the cast's nearest-enemy
    // fallback (CastTargeting.For), so no target or a dead or friendly one leaves every slot untinted. Lane TINT-RUNOVER:
    // once the run has ended for any result the tint is off too, since a depleted run can end with the player's target
    // alive and out of range. The bar only reads that the run ended; the run-end panel draws the result (HudSceneTest 25).
    // Finding f-4e8c87b7: the one range decision (AbilitySlotLook.ForRange) every part of the slot reads - the icon's
    // red modulate, the edge and the "out of range" caption - so the red never shows without its written reason.
    private static Keyrun.Content.SlotRangeLook RangeLook(Run run, ActorState me, AbilityDef def)
    {
        Keyrun.Content.RangeTarget? target = me.TargetId is { } id && id != me.Id && run.State.Actors.TryGetValue(id, out var t)
            ? new Keyrun.Content.RangeTarget(t.Alive, t.Kind != me.Kind, Casting.RangeMm(me, t))
            : null;
        var hostileAbility = def.Shape.Kind == ShapeKind.Target && Affinities.Of(def, run.Content.Effects) == Affinity.Hostile;
        return Keyrun.Content.AbilitySlotLook.ForRange(target, hostileAbility, def.Range, runOver: run.RunEnd.Result is not null);
    }

    // The range look of a slot with no range state (an unknown ability, the G ping).
    private static readonly Keyrun.Content.SlotRangeLook InRange = Keyrun.Content.AbilitySlotLook.ForRange(null, false, 0);

    private void DrawSlot(Font font, Rect2 rect, string key, string name, float alpha, Keyrun.Content.SlotRangeLook range, bool dashed, Texture2D? icon = null, float iconAlpha = 1f)
    {
        var outOfRange = range.OutOfRange;
        DrawRect(rect, new Color(SlotBack, SlotBack.A * alpha));
        if (icon is not null)
        {
            // The icon sits centred, nudged down so the key label keeps the top-left corner clear.
            var iconRect = new Rect2(rect.Position + new Vector2((rect.Size.X - IconSize) / 2, ((rect.Size.Y - IconSize) / 2) + 4), new Vector2(IconSize, IconSize));
            var tint = ToColor(range.IconModulate); // red when out of range, WoW style
            DrawTextureRect(icon, iconRect, false, new Color(tint, tint.A * alpha * iconAlpha));
        }
        var edge = ToColor(range.Edge);
        DrawRect(rect, new Color(edge, dashed ? 0.5f : 1f), filled: false, width: range.EdgeWidth);
        // The key label has a dark drop shadow so it stays readable over the white icon glyph.
        DrawString(font, rect.Position + new Vector2(7, 21), key, HorizontalAlignment.Left, -1, KeyFont, new Color(KeyShadow, KeyShadow.A * alpha));
        DrawString(font, rect.Position + new Vector2(6, 20), key, HorizontalAlignment.Left, -1, KeyFont, new Color(1f, 0.9f, 0.55f, alpha));
        if (range.Caption is { } caption)
        {
            // The written reason for the red icon (shape plus colour, BIBLE s3): a small outlined caption along the
            // slot's bottom edge, over the icon's lower margin, shrunk to fit the slot width.
            var size = CaptionFont;
            while (size > 8 && font.GetStringSize(caption, HorizontalAlignment.Left, -1, size).X > rect.Size.X - 6)
            {
                size--;
            }
            var w = font.GetStringSize(caption, HorizontalAlignment.Left, -1, size).X;
            var at = new Vector2(rect.Position.X + ((rect.Size.X - w) / 2), rect.End.Y - 4);
            DrawStringOutline(font, at, caption, HorizontalAlignment.Left, -1, size, 4, new Color(0f, 0f, 0f, alpha));
            DrawString(font, at, caption, HorizontalAlignment.Left, -1, size, new Color(1f, 0.55f, 0.55f, alpha));
        }
        if (icon is not null)
        {
            return; // the icon replaces the name text (reduce screen clutter); the name stays for an unmapped ability
        }
        // Name wraps onto two lines at the bottom of the slot.
        var words = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var lines = words.Length == 2 && font.GetStringSize(name, HorizontalAlignment.Left, -1, NameFont).X > rect.Size.X - 8
            ? words
            : new[] { name };
        for (var i = 0; i < lines.Length; i++)
        {
            var lift = range.Caption is not null ? CaptionFont + 2 : 0; // the name sits above the caption, never under it
            var y = rect.End.Y - 8 - lift - ((lines.Length - 1 - i) * (NameFont + 2));
            var w = font.GetStringSize(lines[i], HorizontalAlignment.Left, -1, NameFont).X;
            DrawString(font, new Vector2(rect.Position.X + ((rect.Size.X - w) / 2), y), lines[i], HorizontalAlignment.Left, -1, NameFont, outOfRange ? new Color(OutOfRangeTint, alpha) : new Color(1, 1, 1, alpha));
        }
    }

    private static Color ToColor(Keyrun.Content.Rgba8 c) => Color.Color8((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);

    private void DrawMeter(Font font, Rect2 rect, long value, long max, Color fill, string label)
    {
        var f = max <= 0 ? 0f : Mathf.Clamp((float)value / max, 0f, 1f);
        DrawRect(rect, SlotBack);
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X * f, rect.Size.Y)), fill);
        DrawRect(rect, SlotEdge, filled: false, width: 1f);
        DrawString(font, new Vector2(rect.Position.X + 6, rect.End.Y - 3), label, HorizontalAlignment.Left, -1, (int)Mathf.Min(NameFont, rect.Size.Y - 2), Colors.White);
    }

    private void DrawCentered(Font font, Rect2 rect, string text, int size, Color color)
    {
        var s = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var p = rect.Position + new Vector2((rect.Size.X - s.X) / 2, (rect.Size.Y + (size * 0.7f)) / 2);
        DrawStringOutline(font, p, text, HorizontalAlignment.Left, -1, size, 5, Colors.Black);
        DrawString(font, p, text, HorizontalAlignment.Left, -1, size, color);
    }
}
