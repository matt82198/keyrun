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
/// class resource is short, a red edge when the cast's target is out of range. Above it: the Warden's health and
/// Momentum. The run-over state (result, rewards, the Enter hint) is the run-end panel's (<see cref="RunEndPanel"/>).
/// </summary>
public partial class AbilityBar : Control
{
    private const float Slot = 78;
    private const float Gap = 8;
    private const float BottomMargin = 24;
    private const int KeyFont = 18;
    private const int NameFont = 13;

    private static readonly Color SlotBack = new(0.1f, 0.11f, 0.14f, 0.92f);
    private static readonly Color SlotEdge = new(0.45f, 0.47f, 0.55f, 1f);
    private static readonly Color CooldownShade = new(0f, 0f, 0f, 0.62f);
    private static readonly Color GcdShade = new(1f, 1f, 1f, 0.16f);
    private static readonly Color OutOfRangeEdge = new(0.95f, 0.2f, 0.15f, 1f);
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

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Driver = GetNodeOrNull<SimDriver>(DriverPath);
        KeyInput = GetNodeOrNull<PlayerInput>(InputPath);
    }

    /// <inheritdoc />
    public override void _Process(double delta) => QueueRedraw();

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
            if (!run.Content.Abilities.TryGetValue(slot.AbilityId, out var def))
            {
                DrawSlot(font, rect, key, slot.AbilityId, 1f, false, false);
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
            var outOfRange = def.Shape.Kind == ShapeKind.Target && def.Range > 0 && OutOfRange(run, me, def);
            DrawSlot(font, rect, key, Driver.AbilityName(def.Id), shortResource ? 0.45f : 1f, outOfRange, false);
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

    private static bool OutOfRange(Run run, ActorState me, AbilityDef def)
    {
        var target = CastTargeting.For(run, me.Id, def.Id);
        return target != me.Id && run.State.Actors.TryGetValue(target, out var t) && Casting.RangeMm(me, t) > def.Range;
    }

    private void DrawSlot(Font font, Rect2 rect, string key, string name, float alpha, bool outOfRange, bool dashed)
    {
        DrawRect(rect, new Color(SlotBack, SlotBack.A * alpha));
        DrawRect(rect, outOfRange ? OutOfRangeEdge : new Color(SlotEdge, dashed ? 0.5f : 1f), filled: false, width: outOfRange ? 3f : 2f);
        DrawString(font, rect.Position + new Vector2(6, 20), key, HorizontalAlignment.Left, -1, KeyFont, new Color(1f, 0.9f, 0.55f, alpha));
        // Name wraps onto two lines at the bottom of the slot.
        var words = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var lines = words.Length == 2 && font.GetStringSize(name, HorizontalAlignment.Left, -1, NameFont).X > rect.Size.X - 8
            ? words
            : new[] { name };
        for (var i = 0; i < lines.Length; i++)
        {
            var y = rect.End.Y - 8 - ((lines.Length - 1 - i) * (NameFont + 2));
            var w = font.GetStringSize(lines[i], HorizontalAlignment.Left, -1, NameFont).X;
            DrawString(font, new Vector2(rect.Position.X + ((rect.Size.X - w) / 2), y), lines[i], HorizontalAlignment.Left, -1, NameFont, new Color(1, 1, 1, alpha));
        }
    }

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
        DrawString(font, p + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, size, Colors.Black);
        DrawString(font, p, text, HorizontalAlignment.Left, -1, size, color);
    }
}
