using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>Which half of a Combo pair failed.</summary>
public enum ComboHalf
{
    /// <summary>The Setup half (opened the Window).</summary>
    Setup,

    /// <summary>The Payoff half (tried to close it and was denied).</summary>
    Payoff,
}

/// <summary>
/// One <c>combo_denied</c> sim event (game/sim/Keyrun.Sim/Combat/Payoff.cs), reduced to plain values so the blame rule
/// stays Godot- and sim-free.
/// </summary>
/// <param name="PayoffActorId">The actor whose Payoff was denied.</param>
/// <param name="SetupActorId">The actor that opened the Window (for a Lock, the Window's carrier source).</param>
/// <param name="WindowId">The Window id.</param>
/// <param name="Reason">"self_combo" or "lock_alternate".</param>
public readonly record struct DeniedCombo(int PayoffActorId, int SetupActorId, string WindowId, string Reason);

/// <summary>One private "which half was late" line on the run-end panel.</summary>
/// <param name="Seat">The seat it belongs to (always the local player).</param>
/// <param name="WindowId">The Window id.</param>
/// <param name="Reason">The denial reason.</param>
/// <param name="LateHalf">The half that failed (always the seat's own half).</param>
/// <param name="Count">How many times this denial happened in the run.</param>
public sealed record ComboBlameLine(int Seat, string WindowId, string Reason, ComboHalf LateHalf, int Count)
{
    /// <summary>"HUD runend combo seat=1 window=sundered reason=self_combo half=payoff count=2".</summary>
    /// <returns>The autorun proof line.</returns>
    public string LogLine() => FormattableString.Invariant(
        $"HUD runend combo seat={Seat} window={WindowId} reason={Reason} half={(LateHalf == ComboHalf.Payoff ? "payoff" : "setup")} count={Count}");

    /// <summary>The panel text, e.g. "Sundered: your Payoff was the late half -- your class opened it, another class closes it (x2)".</summary>
    /// <returns>The line.</returns>
    public string Text()
    {
        var window = WindowId.Length > 0 ? char.ToUpperInvariant(WindowId[0]) + WindowId[1..] : WindowId;
        var why = Reason == "lock_alternate"
            ? "you closed the last Lock, the other class had to close this one"
            : "your class opened it, another class closes it";
        var times = Count == 1 ? string.Empty : FormattableString.Invariant($" (x{Count})");
        return $"{window}: your Payoff was the late half -- {why}{times}";
    }
}

/// <summary>
/// Lane COMBO-BLAME (BIBLE s4: "when the party fails a Combo, the game shows which half of the pair was late, in the
/// run log, privately"): the local seat's own denied Payoffs, one line per (Window, reason), most frequent first then
/// first seen, at most <see cref="MaxLines"/> (the panel shows three causes the same way). Only denials whose Payoff
/// actor is the seat are kept: a seat never sees another seat's blame, even on a Window it opened.
/// </summary>
public static class ComboBlame
{
    /// <summary>The most lines the panel shows (the cause list's cap).</summary>
    public const int MaxLines = 3;

    /// <summary>The seat's own lines.</summary>
    /// <param name="seat">The local player's actor id.</param>
    /// <param name="denied">The run's denials, in emission order.</param>
    /// <returns>The lines, capped.</returns>
    public static IReadOnlyList<ComboBlameLine> For(int seat, IEnumerable<DeniedCombo> denied)
    {
        ArgumentNullException.ThrowIfNull(denied);
        return denied
            .Where(d => d.PayoffActorId == seat)
            .Select((d, i) => (d, i))
            .GroupBy(x => (x.d.WindowId, x.d.Reason))
            .Select(g => (Line: new ComboBlameLine(seat, g.Key.WindowId, g.Key.Reason, ComboHalf.Payoff, g.Count()), First: g.Min(x => x.i)))
            .OrderByDescending(x => x.Line.Count)
            .ThenBy(x => x.First)
            .Take(MaxLines)
            .Select(x => x.Line)
            .ToList();
    }
}
