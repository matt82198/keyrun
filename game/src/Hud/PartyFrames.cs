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
/// the class resource, and status chips; a downed member's frame greys out under a DOWNED chip. Reads the sim state
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

    private readonly Dictionary<int, string> _logged = new();
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
            DrawFrame(new Rect2(0, y, FrameWidth, FrameHeight), f);
            y += FrameHeight + Gap;
        }
    }

    private void DrawFrame(Rect2 rect, PartyFrame f)
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
        foreach (var chip in f.Chips)
        {
            var text = chip.Text;
            var w = HudNodes.Width(text, ChipFont) + 8;
            if (x + w > rect.End.X - 4)
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
        LogFrames();
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events) => LogFrames();

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
