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
/// class resource is short, its icon tinted red (WoW style, no red border) when the cast's target is out of range
/// (<see cref="Keyrun.Content.AbilitySlotLook"/>, lane ABILITYBAR-RANGE-TINT). Above it: the Warden's health and
/// Momentum. The run-over state (result, rewards, the Enter hint) is the run-end panel's (<see cref="RunEndPanel"/>).
/// Each ability slot draws its CC0 icon (<see cref="Keyrun.Content.AbilityIcons"/>, lane ABILITY-ICONS) in place of the
/// name text; a slot whose ability has no icon row keeps the name. Under --autorun it prints one "HUD bar slot=K
/// ability=ID icon=PATH tex=WxH" line per slot when a run's setup arrives (HudSceneTest check 40).
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
        QueueRedraw();
    }

    // The last "HUD bar range" line printed, so the line prints only when the tinted-slot count or the run-over state
    // changes (lane TINT-RUNOVER, HudSceneTest check 41).
    private string? _lastRangeLine;

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
            if (run.Content.Abilities.TryGetValue(slot.AbilityId, out var def) && OutOfRange(run, me, def))
            {
                tinted++;
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
        DrawMeter(font, new Rect2(origin.X, origin.Y - 24, width, 14), me.Resource, me.MaxResource, MomentumFill, $"Momentum {me.Resource}");

        for (var i = 0; i < setup.Slots.Count; i++)
        {
            var slot = setup.Slots[i];
            var rect = new Rect2(origin.X + (i * (Slot + Gap)), origin.Y, Slot, Slot);
            var key = KeyInput is not null ? KeyBinds.Label(KeyInput.Binds.KeyFor(slot.Action)) : slot.Key;
            var icon = ReferenceEquals(setup, _iconSetup) && i < _icons.Length ? _icons[i] : null;
            if (!run.Content.Abilities.TryGetValue(slot.AbilityId, out var def))
            {
                DrawSlot(font, rect, key, slot.AbilityId, 1f, false, false, icon);
                continue;
            }
            var cdFraction = 0f;
            string? cdText = null;
            if (def.CooldownTicks > 0 && me.Cooldowns.TryGetValue(def.Id, out var readyTick) && readyTick > now &&
                Cooldowns.ChargesAvailable(me, def, now) < 1)
            {
                var left = readyTick - now;
                cdFraction = Mathf.Clamp((float)left / def.CooldownTicks, 0f, 1f);
                cdText = $"{left / (float)Tick.TickHz:0.0}";
            }
            var gcd = def.GcdTicks > 0 && me.GcdReadyTick > now ? Mathf.Clamp((float)(me.GcdReadyTick - now) / def.GcdTicks, 0f, 1f) : 0f;
            var shortResource = def.Cost.Resource != "none" && me.Resource < def.Cost.Amount;
            var outOfRange = OutOfRange(run, me, def);
            DrawSlot(font, rect, key, Driver.AbilityName(def.Id), shortResource ? 0.45f : 1f, outOfRange, false, icon, cdFraction > 0f ? CooldownIconAlpha : 1f);
            if (cdFraction > 0f)
            {
                DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * cdFraction)), CooldownShade);
                DrawCentered(font, rect, cdText!, 22, Colors.White);
            }
            else if (gcd > 0f)
            {
                DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * gcd)), GcdShade);
            }
        }

        var pingRect = new Rect2(origin.X + (setup.Slots.Count * (Slot + Gap)), origin.Y, Slot, Slot);
        var pingKey = KeyInput is not null ? KeyBinds.Label(KeyInput.Binds.KeyFor(KeyAction.Ping)) : "G";
        DrawSlot(font, pingRect, pingKey, "Ping", 0.8f, false, true);
    }

    // Lane RANGE-TINT-NOTARGET: the tint reads off the player's own selected target, never the cast's nearest-enemy
    // fallback (CastTargeting.For), so no target or a dead or friendly one leaves every slot untinted. Lane TINT-RUNOVER:
    // once the run has ended for any result the tint is off too, since a depleted run can end with the player's target
    // alive and out of range. The bar only reads that the run ended; the run-end panel draws the result (HudSceneTest 25).
    private static bool OutOfRange(Run run, ActorState me, AbilityDef def)
    {
        Keyrun.Content.RangeTarget? target = me.TargetId is { } id && id != me.Id && run.State.Actors.TryGetValue(id, out var t)
            ? new Keyrun.Content.RangeTarget(t.Alive, t.Kind != me.Kind, Casting.RangeMm(me, t))
            : null;
        var hostileAbility = def.Shape.Kind == ShapeKind.Target && Affinities.Of(def, run.Content.Effects) == Affinity.Hostile;
        return Keyrun.Content.AbilitySlotLook.ShowsOutOfRange(target, hostileAbility, def.Range, runOver: run.RunEnd.Result is not null);
    }

    private void DrawSlot(Font font, Rect2 rect, string key, string name, float alpha, bool outOfRange, bool dashed, Texture2D? icon = null, float iconAlpha = 1f)
    {
        DrawRect(rect, new Color(SlotBack, SlotBack.A * alpha));
        if (icon is not null)
        {
            // The icon sits centred, nudged down so the key label keeps the top-left corner clear.
            var iconRect = new Rect2(rect.Position + new Vector2((rect.Size.X - IconSize) / 2, ((rect.Size.Y - IconSize) / 2) + 4), new Vector2(IconSize, IconSize));
            var tint = ToColor(Keyrun.Content.AbilitySlotLook.IconModulate(outOfRange)); // red when out of range, WoW style
            DrawTextureRect(icon, iconRect, false, new Color(tint, tint.A * alpha * iconAlpha));
        }
        var edge = ToColor(Keyrun.Content.AbilitySlotLook.Edge(outOfRange));
        DrawRect(rect, new Color(edge, dashed ? 0.5f : 1f), filled: false, width: Keyrun.Content.AbilitySlotLook.EdgeWidth(outOfRange));
        // The key label has a dark drop shadow so it stays readable over the white icon glyph.
        DrawString(font, rect.Position + new Vector2(7, 21), key, HorizontalAlignment.Left, -1, KeyFont, new Color(KeyShadow, KeyShadow.A * alpha));
        DrawString(font, rect.Position + new Vector2(6, 20), key, HorizontalAlignment.Left, -1, KeyFont, new Color(1f, 0.9f, 0.55f, alpha));
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
            var y = rect.End.Y - 8 - ((lines.Length - 1 - i) * (NameFont + 2));
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
