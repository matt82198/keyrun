using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

// Godot-free half of PartyFrames.cs (moved unchanged so tools/Keyrun.Playtest.Tests can link it: StatusChipsEchoTests
// drives the ECHO chip over real runs, finding f-454a45a6). PartyFrames.cs draws these chips and logs them.
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

    /// <summary>
    /// The chip as one proof line, printed when it first appears on a frame (PartyFrames.LogChips):
    /// "HUD chip seat=2 id=chorusEcho label=ECHO colour=7dffb0 at=60".
    /// </summary>
    /// <param name="seat">The frame's actor id.</param>
    /// <param name="tick">The sim tick the chip appeared at.</param>
    /// <returns>The line.</returns>
    public string ProofLine(int seat, int tick) =>
        FormattableString.Invariant($"HUD chip seat={seat} id={Id} label={Label} colour={Rgb:x6} at={tick}");
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

    /// <summary>The buff palette colour (status-chips.ts BUFF, #7dffb0): HASTE, GLIDE, SPEED, HOT and ECHO.</summary>
    public const int BuffRgb = Buff;

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
