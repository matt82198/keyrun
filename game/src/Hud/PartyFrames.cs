using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>One status chip on a party frame (client/src/render/status-chips.ts StatusChip).</summary>
/// <param name="Id">The effect id, or "downed".</param>
/// <param name="Label">The short upper-case label, e.g. "STUN" or "DOWNED".</param>
/// <param name="TicksLeft">Ticks until the effect ends; 0 for DOWNED (no timer).</param>
/// <param name="Rgb">The chip colour as 0xRRGGBB.</param>
public sealed record StatusChip(string Id, string Label, int TicksLeft, int Rgb)
{
    /// <summary>"LABEL 1.5s", or the bare label when there is no timer.</summary>
    public string Text => TicksLeft > 0
        ? FormattableString.Invariant($"{Label} {TicksLeft / (double)Tick.TickHz:0.0}s")
        : Label;
}

/// <summary>
/// The status chips of one actor, Godot-free (status-chips.ts plus snapshot-codec.ts toEffects): one chip per live
/// effect id that content defines (the longest instance wins), sorted by id, at most <see cref="MaxChips"/>, and a
/// DOWNED chip first when the actor is dead. Display only; it never decides a rule.
/// </summary>
public static class StatusChips
{
    /// <summary>snapshot.ts MAX_ACTOR_EFFECTS.</summary>
    public const int MaxChips = 6;

    /// <summary>status-chips.ts DOWNED_COLOR (#9aa0a6).</summary>
    public const int DownedRgb = 0x9aa0a6;

    private const int Cc = 0xff5a5a;
    private const int Dot = 0xff9a3c;
    private const int Ice = 0x8fd6ff;
    private const int Guard = 0xffd166;
    private const int Buff = 0x7dffb0;

    private static readonly Dictionary<string, (string Label, int Rgb)> Chip = new(StringComparer.Ordinal)
    {
        ["stunned"] = ("STUN", Cc), ["silenced"] = ("SILENCE", Cc), ["blinded"] = ("BLIND", Cc),
        ["slowed"] = ("SLOW", Ice), ["chill"] = ("CHILL", Ice), ["burning"] = ("BURN", Dot),
        ["braced"] = ("BRACED", Guard), ["braceSelf"] = ("BRACED", Guard), ["bracedSelf"] = ("BRACED", Guard),
        ["ashenSkin"] = ("SHIELD", Guard), ["aegisShield"] = ("SHIELD", Guard), ["wardShield"] = ("SHIELD", Guard),
        ["wardPulse"] = ("SHIELD", Guard),
        ["hasted"] = ("HASTE", Buff), ["gliding"] = ("GLIDE", Buff), ["hymnSpeed"] = ("SPEED", Buff),
        ["mendingHot"] = ("HOT", Buff), ["hymnHeal"] = ("HOT", Buff), ["consecrateHeal"] = ("HOT", Buff),
        ["chorusEcho"] = ("ECHO", Buff),
        ["lockShield"] = ("LOCK", 0xc77dff), [PingCalls.SpreadEffectId] = ("SPREAD", 0xcc79a7),
        [PingCalls.StackEffectId] = ("STACK", 0x56b4e9),
    };

    /// <summary>The chips for <paramref name="actor"/> at tick <paramref name="now"/>.</summary>
    /// <param name="actor">The actor.</param>
    /// <param name="now">The current sim tick.</param>
    /// <param name="knownEffects">The run's effect ids (content); effects outside it are sim bookkeeping, not shown.</param>
    /// <returns>The chips, DOWNED first.</returns>
    public static IReadOnlyList<StatusChip> For(ActorState actor, int now, ICollection<string> knownEffects)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(knownEffects);
        var chips = new List<StatusChip>();
        if (!actor.Alive)
        {
            chips.Add(new StatusChip("downed", "DOWNED", 0, DownedRgb));
            return chips;
        }
        var left = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in actor.Effects)
        {
            if (e.EndsTick > now && knownEffects.Contains(e.EffectId))
            {
                left[e.EffectId] = Math.Max(left.TryGetValue(e.EffectId, out var t) ? t : 0, e.EndsTick - now);
            }
        }
        // Two effect ids can share a label (braced and braceSelf both read BRACED): one chip per label, the longer.
        var byLabel = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, ticks) in left)
        {
            var (label, rgb) = Chip.TryGetValue(id, out var c) ? c : (Fallback(id), 0xc9ccd1);
            if (byLabel.TryGetValue(label, out var at))
            {
                if (ticks > chips[at].TicksLeft)
                {
                    chips[at] = new StatusChip(id, label, ticks, rgb);
                }
                continue;
            }
            if (chips.Count >= MaxChips)
            {
                continue;
            }
            byLabel[label] = chips.Count;
            chips.Add(new StatusChip(id, label, ticks, rgb));
        }
        return chips;
    }

    // status-chips.ts chipFor fallback: humanizeId upper-cased, two words, ten characters.
    private static string Fallback(string id)
    {
        var tail = id.Split('.').Last();
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in tail)
        {
            if (char.IsUpper(ch) && current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
            if (ch is '_' or '-' or ' ')
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }
        var text = string.Join(' ', words.Take(2)).ToUpperInvariant();
        return text.Length > 10 ? text[..10] : text;
    }
}

/// <summary>One party member's frame, read from the sim state (Godot-free).</summary>
/// <param name="Seat">The actor id.</param>
/// <param name="Name">The class display name.</param>
/// <param name="Health">Current health.</param>
/// <param name="MaxHealth">Maximum health.</param>
/// <param name="Resource">Current class resource.</param>
/// <param name="MaxResource">Resource cap.</param>
/// <param name="ResourceName">The class resource's display name (content ClassLoop.Resource).</param>
/// <param name="Downed">True once the sim marks the actor dead.</param>
/// <param name="IsLocal">True for the human's own seat.</param>
/// <param name="Chips">Status chips, DOWNED first.</param>
public sealed record PartyFrame(
    int Seat,
    string Name,
    long Health,
    long MaxHealth,
    long Resource,
    long MaxResource,
    string ResourceName,
    bool Downed,
    bool IsLocal,
    IReadOnlyList<StatusChip> Chips)
{
    /// <summary>The frame as one log line: "HUD party seat=1 name=Warden hp=400/400 res=0/5 chips=-".</summary>
    public string LogLine()
    {
        var chips = Chips.Count == 0 ? "-" : string.Join(",", Chips.Select(c => c.Label));
        return FormattableString.Invariant(
            $"HUD party seat={Seat} name={Name} hp={Health}/{MaxHealth} res={Resource}/{MaxResource} chips={chips}");
    }

    /// <summary>The part of the frame that changes what a player reads (resource ticks are left out of logging).</summary>
    public string ChangeKey() => FormattableString.Invariant($"{Seat}:{Health}:{Downed}:{string.Join(",", Chips.Select(c => c.Label))}");
}

/// <summary>Builds the party frames from a run (Godot-free).</summary>
public static class PartyFrameModel
{
    /// <summary>
    /// Every player actor in the run, ascending by actor id (the sim's actor order), the local seat flagged.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="localSeat">The human's actor id.</param>
    /// <param name="registry">Content for display names and resource names, or null for ids.</param>
    /// <returns>The frames.</returns>
    public static IReadOnlyList<PartyFrame> Build(Run run, int localSeat, ContentRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(run);
        var known = run.Content.Effects.Keys.ToHashSet(StringComparer.Ordinal);
        var frames = new List<PartyFrame>();
        foreach (var actor in run.State.Actors.Values)
        {
            if (actor.Kind != ActorKind.Player)
            {
                continue;
            }
            var resourceName = registry is not null && registry.Classes.TryGetValue(actor.ClassId, out var cls)
                ? Capitalise(cls.Loop.Resource)
                : "Resource";
            frames.Add(new PartyFrame(
                actor.Id,
                Names.Actor(registry, actor.ClassId),
                actor.Health,
                actor.MaxHealth,
                actor.Resource,
                actor.MaxResource,
                resourceName,
                !actor.Alive,
                actor.Id == localSeat,
                StatusChips.For(actor, run.State.Tick, known)));
        }
        return frames;
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

/// <summary>
/// The pure kick-ready rule (BIBLE s10 kick-ready readout, Godot-free): a kick is ready at tick T when T has reached the
/// tick its cooldown ends. The ready pip and the draining arc both read this rule; neither computes kick order.
/// </summary>
public static class KickReadiness
{
    /// <summary>True when a kick whose cooldown ends at <paramref name="cooldownEndTick"/> is ready at <paramref name="tick"/>.</summary>
    /// <param name="tick">The current sim tick.</param>
    /// <param name="cooldownEndTick">The tick the kick's cooldown ends (sim: actor.Cooldowns[kickId], 0 when never used).</param>
    /// <returns>True at or after the end tick.</returns>
    public static bool IsReady(int tick, int cooldownEndTick) => tick >= cooldownEndTick;

    /// <summary>Ticks of cooldown still to run at <paramref name="tick"/>, never negative.</summary>
    /// <param name="tick">The current sim tick.</param>
    /// <param name="cooldownEndTick">The tick the cooldown ends.</param>
    /// <returns>Ticks left; 0 when ready.</returns>
    public static int TicksLeft(int tick, int cooldownEndTick) => Math.Max(0, cooldownEndTick - tick);

    /// <summary>The share of the full cooldown still to drain, in [0, 1] (the arc's sweep).</summary>
    /// <param name="tick">The current sim tick.</param>
    /// <param name="cooldownEndTick">The tick the cooldown ends.</param>
    /// <param name="cooldownTicks">The kick's full cooldown length.</param>
    /// <returns>1 at the start of a cooldown, 0 when ready or when the kick has no cooldown.</returns>
    public static double RemainingFraction(int tick, int cooldownEndTick, int cooldownTicks) =>
        cooldownTicks <= 0 ? 0d : Math.Clamp(TicksLeft(tick, cooldownEndTick) / (double)cooldownTicks, 0d, 1d);
}

/// <summary>
/// One party member's kick-ready readout: the kick ability in the member's class Interrupt slot and the sim's own cooldown
/// end tick for it, read at <see cref="Tick"/>.
/// </summary>
/// <param name="AbilityId">The kick ability id (the class Interrupt slot).</param>
/// <param name="Tick">The sim tick the readout was read at.</param>
/// <param name="CooldownEndTick">The tick the kick's cooldown ends.</param>
/// <param name="CooldownTicks">The kick's full cooldown length.</param>
public sealed record KickReadout(string AbilityId, int Tick, int CooldownEndTick, int CooldownTicks)
{
    /// <summary>True when the kick is ready now.</summary>
    public bool Ready => KickReadiness.IsReady(Tick, CooldownEndTick);

    /// <summary>Ticks of cooldown left; 0 when ready.</summary>
    public int TicksLeft => KickReadiness.TicksLeft(Tick, CooldownEndTick);

    /// <summary>Share of the cooldown still to drain, in [0, 1].</summary>
    public double RemainingFraction => KickReadiness.RemainingFraction(Tick, CooldownEndTick, CooldownTicks);
}

/// <summary>Reads each party member's kick-ready state from the sim (Godot-free). Read only: never a rule decision.</summary>
public static class PartyKick
{
    /// <summary>The kick readout for <paramref name="actor"/>, or null when the content or registry does not name a kick.</summary>
    /// <param name="run">The run.</param>
    /// <param name="actor">The player actor.</param>
    /// <param name="registry">Content for the class Interrupt slot, or null (no readout).</param>
    /// <returns>The readout, or null.</returns>
    public static KickReadout? For(Run run, ActorState actor, ContentRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(actor);
        if (registry is null || !registry.Classes.TryGetValue(actor.ClassId, out var cls))
        {
            return null;
        }
        if (!run.Content.Abilities.TryGetValue(cls.Abilities.Interrupt, out var def))
        {
            return null;
        }
        var end = actor.Cooldowns.TryGetValue(def.Id, out var readyTick) ? readyTick : 0;
        return new KickReadout(def.Id, run.State.Tick, end, def.CooldownTicks);
    }
}

/// <summary>Shared HUD plumbing: find the driver, print autorun proof lines, colours.</summary>
public static class HudNodes
{
    /// <summary>Panel background.</summary>
    public static readonly Color Back = new(0.1f, 0.11f, 0.14f, 0.88f);

    /// <summary>Panel edge.</summary>
    public static readonly Color Edge = new(0.45f, 0.47f, 0.55f, 1f);

    /// <summary>
    /// The SimDriver at <paramref name="path"/> from <paramref name="self"/>, else the nearest ancestor's child named
    /// "SimDriver" (so the HUD scene works wherever the run root mounts it).
    /// </summary>
    /// <param name="self">The HUD node.</param>
    /// <param name="path">The exported driver path.</param>
    /// <returns>The driver, or null.</returns>
    public static SimDriver? FindDriver(Node self, NodePath path)
    {
        ArgumentNullException.ThrowIfNull(self);
        if (self.GetNodeOrNull(path) is SimDriver direct)
        {
            return direct;
        }
        for (var n = self.GetParent(); n is not null; n = n.GetParent())
        {
            if (n.GetNodeOrNull("SimDriver") is SimDriver found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>Prints one "HUD ..." proof line when the driver autoruns (the scene test reads them).</summary>
    /// <param name="driver">The driver.</param>
    /// <param name="line">The line.</param>
    public static void Proof(SimDriver driver, string line)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (driver.Options.Autorun)
        {
            GD.Print(line);
        }
    }

    /// <summary>A 0xRRGGBB colour.</summary>
    /// <param name="rgb">The colour.</param>
    /// <param name="alpha">Alpha.</param>
    /// <returns>The Godot colour.</returns>
    public static Color Rgb(int rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, alpha);

    /// <summary>Draws text with a one-pixel shadow.</summary>
    /// <param name="canvas">The control.</param>
    /// <param name="pos">Baseline position.</param>
    /// <param name="text">The text.</param>
    /// <param name="size">Font size.</param>
    /// <param name="color">Colour.</param>
    public static void Text(CanvasItem canvas, Vector2 pos, string text, int size, Color color)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var font = ThemeDB.FallbackFont;
        canvas.DrawString(font, pos + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, size, Colors.Black);
        canvas.DrawString(font, pos, text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>Width of <paramref name="text"/> at <paramref name="size"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">Font size.</param>
    /// <returns>Pixels.</returns>
    public static float Width(string text, int size) =>
        ThemeDB.FallbackFont.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
}

/// <summary>
/// Party frames (BIBLE s10 party, ENGINE-PLAN 3C): one frame per player seat down the left edge with health,
/// the class resource, and status chips; a downed member's frame greys out under a DOWNED chip. A member's live cast
/// shows in its frame, right of the name (HUD-DECLUTTER: no cast bar over a party member's head). Reads the sim state
/// the SimDriver exposes and never writes it.
/// </summary>
public partial class PartyFrames : Control
{
    private const float FrameWidth = 240;
    private const float FrameHeight = 68;
    private const float Gap = 8;
    private const int NameFont = 15;
    private const int ChipFont = 11;

    private static readonly Color HealthFill = new(0.25f, 0.75f, 0.35f, 1f);
    private static readonly Color ResourceFill = new(0.95f, 0.65f, 0.15f, 1f);
    private static readonly Color DownedTint = new(0.6f, 0.62f, 0.65f, 1f);

    // The in-frame cast bar (HUD-DECLUTTER): CastBarView's fill and frame colours, right of the class name.
    private const float CastBarLeft = 96;
    private const int CastFont = 11;
    private static readonly Color CastFill = new(0.95f, 0.72f, 0.2f, 0.95f);
    private static readonly Color CastKickFrame = new(1f, 0.6f, 0.1f, 1f);
    private static readonly Color CastLockedFrame = new(0.55f, 0.55f, 0.6f, 1f);

    // The kick-ready readout (BIBLE s10): a filled pip with a ring when the kick is ready, a hollow ring with an
    // amber arc that shrinks as the cooldown drains otherwise. The shape differs by state, so colour is never the only cue.
    private const float KickPipRadius = 6f;
    private const float KickSlotWidth = 22f;
    private static readonly Color KickReadyFill = new(0.45f, 0.95f, 0.5f, 1f);
    private static readonly Color KickDimRing = new(0.45f, 0.47f, 0.52f, 1f);
    private static readonly Color KickArc = new(1f, 0.72f, 0.3f, 1f);

    private readonly Dictionary<int, string> _logged = new();
    private readonly Dictionary<int, int> _castLogged = new();
    private readonly Dictionary<int, string> _kickLogged = new();
    private SimDriver? _driver;

    /// <summary>The driver to read the run from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _driver = HudNodes.FindDriver(this, DriverPath);
        if (_driver is not null)
        {
            _driver.Stepped += OnStepped;
            _driver.RunStarted += OnRunStarted;
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

    /// <inheritdoc />
    public override void _Process(double delta) => QueueRedraw();

    /// <inheritdoc />
    public override void _Draw()
    {
        if (_driver?.Run is not { } run || _driver.Setup is not { } setup)
        {
            return;
        }
        var frames = PartyFrameModel.Build(run, setup.HumanActorId, _driver.Registry);
        var y = 0f;
        foreach (var f in frames)
        {
            var rect = new Rect2(0, y, FrameWidth, FrameHeight);
            KickReadout? kick = null;
            if (run.State.Actors.TryGetValue(f.Seat, out var member) && member.Alive)
            {
                kick = PartyKick.For(run, member, _driver.Registry);
            }
            DrawFrame(rect, f, kick is not null);
            if (kick is not null)
            {
                DrawKick(rect, kick);
            }
            if (run.State.Actors.TryGetValue(f.Seat, out member))
            {
                DrawCast(rect, member, run);
            }
            y += FrameHeight + Gap;
        }
    }

    // HUD-DECLUTTER (Matt 2026-10-19, "move friendly casts into party frames"): a member's live cast fills the right
    // of its name row, with the ability name and seconds left; amber frame when interruptible, grey when not.
    private void DrawCast(Rect2 frame, ActorState member, Run run)
    {
        if (_driver is null || !member.Alive || member.CastId is not { } castId || member.CastStartTick is not { } start ||
            member.CastEndsTick is not { } end)
        {
            return;
        }
        var now = run.State.Tick + _driver.Alpha;
        var progress = Mathf.Clamp((now - start) / Mathf.Max(1f, end - start), 0f, 1f);
        var secondsLeft = Mathf.Max(0f, (end - now) / Tick.TickHz);
        var bar = new Rect2(frame.Position.X + CastBarLeft, frame.Position.Y + 5, frame.Size.X - CastBarLeft - 8, 15);
        var interruptible = run.Content.Abilities.TryGetValue(castId, out var def) && def.Interruptible;
        DrawRect(bar, new Color(0.05f, 0.05f, 0.07f, 0.85f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * progress, bar.Size.Y)), CastFill);
        DrawRect(bar, interruptible ? CastKickFrame : CastLockedFrame, filled: false, width: 1f);
        var label = FormattableString.Invariant($"{_driver.AbilityName(castId)} {secondsLeft:0.0}s");
        var font = ThemeDB.FallbackFont;
        var room = bar.Size.X - 6;
        while (label.Length > 1 && HudNodes.Width(label, CastFont) > room)
        {
            label = label[..^1];
        }
        DrawString(font, new Vector2(bar.Position.X + 4, bar.End.Y - 3), label, HorizontalAlignment.Left, -1, CastFont, Colors.White);
    }

    // The kick-ready readout, drawn in the frame's bottom-right corner (the chips leave KickSlotWidth free there).
    private void DrawKick(Rect2 frame, KickReadout kick)
    {
        var c = new Vector2(frame.End.X - KickSlotWidth / 2 - 2, frame.End.Y - KickSlotWidth / 2 - 2);
        if (kick.Ready)
        {
            DrawCircle(c, KickPipRadius, KickReadyFill);
            DrawArc(c, KickPipRadius + 2.5f, 0f, Mathf.Tau, 32, Colors.White, 1.5f, true);
            return;
        }
        DrawArc(c, KickPipRadius, 0f, Mathf.Tau, 32, KickDimRing, 1.5f, true);
        var remaining = (float)kick.RemainingFraction;
        if (remaining > 0f)
        {
            DrawArc(c, KickPipRadius, -Mathf.Pi / 2, -Mathf.Pi / 2 + (Mathf.Tau * remaining), 32, KickArc, 3f, true);
        }
    }

    private void DrawFrame(Rect2 rect, PartyFrame f, bool kickSlot)
    {
        DrawRect(rect, HudNodes.Back);
        DrawRect(rect, f.IsLocal ? new Color(1f, 0.9f, 0.55f, 1f) : HudNodes.Edge, filled: false, width: f.IsLocal ? 2f : 1f);
        var nameColor = f.Downed ? DownedTint : Colors.White;
        HudNodes.Text(this, rect.Position + new Vector2(8, 18), f.Name, NameFont, nameColor);

        var bar = new Rect2(rect.Position.X + 8, rect.Position.Y + 24, rect.Size.X - 16, 12);
        Meter(bar, f.Health, f.MaxHealth, f.Downed ? DownedTint : HealthFill,
            FormattableString.Invariant($"{f.Health} / {f.MaxHealth}"));
        var res = new Rect2(bar.Position.X, bar.End.Y + 3, bar.Size.X, 8);
        Meter(res, f.Resource, f.MaxResource, f.Downed ? DownedTint : ResourceFill, null);

        // Chips under the bars, left to right.
        var x = rect.Position.X + 8;
        var cy = res.End.Y + 3;
        var chipRight = rect.End.X - 4 - (kickSlot ? KickSlotWidth : 0f);
        foreach (var chip in f.Chips)
        {
            var text = chip.Text;
            var w = HudNodes.Width(text, ChipFont) + 8;
            if (x + w > chipRight)
            {
                break;
            }
            DrawRect(new Rect2(x, cy, w, 13), HudNodes.Rgb(chip.Rgb, 0.85f));
            DrawString(ThemeDB.FallbackFont, new Vector2(x + 4, cy + 11), text, HorizontalAlignment.Left, -1, ChipFont, Colors.Black);
            x += w + 4;
        }
    }

    private void Meter(Rect2 rect, long value, long max, Color fill, string? label)
    {
        var frac = max <= 0 ? 0f : Mathf.Clamp((float)value / max, 0f, 1f);
        DrawRect(rect, new Color(0, 0, 0, 0.5f));
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X * frac, rect.Size.Y)), fill);
        if (label is not null)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(rect.Position.X + 4, rect.End.Y - 1), label, HorizontalAlignment.Left, -1, 11, Colors.White);
        }
    }

    private void OnRunStarted()
    {
        _logged.Clear();
        _castLogged.Clear();
        _kickLogged.Clear();
        LogFrames();
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        LogFrames();
        LogCasts();
        LogKicks();
    }

    // "HUD party kick seat=S ability=X state=ready" or "state=cooldown left=N", once per change of state, and once per
    // tenth of the cooldown while it drains (the arc's steps), so the drain reads from the log.
    private void LogKicks()
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        foreach (var actor in run.State.Actors.Values)
        {
            if (actor.Kind != ActorKind.Player || !actor.Alive)
            {
                continue;
            }
            var kick = PartyKick.For(run, actor, _driver.Registry);
            if (kick is null)
            {
                continue;
            }
            var key = kick.Ready
                ? "ready"
                : FormattableString.Invariant($"cooldown:{(int)Math.Ceiling(kick.RemainingFraction * 10)}");
            if (_kickLogged.TryGetValue(actor.Id, out var last) && last == key)
            {
                continue;
            }
            _kickLogged[actor.Id] = key;
            var state = kick.Ready
                ? "state=ready"
                : FormattableString.Invariant($"state=cooldown left={kick.TicksLeft}");
            HudNodes.Proof(_driver, FormattableString.Invariant($"HUD party kick seat={actor.Id} ability={kick.AbilityId} {state}"));
        }
    }

    // "HUD party cast seat=S ability=X" once per cast start of a party member: the cast DrawCast puts in that frame.
    private void LogCasts()
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        foreach (var actor in run.State.Actors.Values)
        {
            if (actor.Kind != ActorKind.Player || !actor.Alive || actor.CastId is not { } castId || actor.CastStartTick is not { } start ||
                actor.CastEndsTick is null)
            {
                continue;
            }
            if (_castLogged.TryGetValue(actor.Id, out var last) && last == start)
            {
                continue;
            }
            _castLogged[actor.Id] = start;
            HudNodes.Proof(_driver, FormattableString.Invariant($"HUD party cast seat={actor.Id} ability={castId}"));
        }
    }

    private void LogFrames()
    {
        if (_driver?.Run is not { } run || _driver.Setup is not { } setup)
        {
            return;
        }
        foreach (var f in PartyFrameModel.Build(run, setup.HumanActorId, _driver.Registry))
        {
            var key = f.ChangeKey();
            if (_logged.TryGetValue(f.Seat, out var last) && last == key)
            {
                continue;
            }
            _logged[f.Seat] = key;
            HudNodes.Proof(_driver, f.LogLine());
        }
    }
}
