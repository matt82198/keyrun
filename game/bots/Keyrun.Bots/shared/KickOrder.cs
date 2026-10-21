using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Bots.Shared;

/// <summary>A party seat with an interrupt: its seat id, and the tick its interrupt comes off cooldown.</summary>
/// <param name="SeatId">Seat (player actor id).</param>
/// <param name="ReadyTick">Tick the seat's kick is ready (its cooldown end). At or before the current tick it is ready now.</param>
public sealed record KickSeat(int SeatId, int ReadyTick);

/// <summary>One seat's place in the kick order.</summary>
/// <param name="SeatId">Seat.</param>
/// <param name="Order">1-based place: 1 kicks next.</param>
/// <param name="Ready">True when the seat's kick is off cooldown at the tick the order was taken.</param>
public sealed record KickSlot(int SeatId, int Order, bool Ready);

/// <summary>
/// KICK-ORDER (design/BIBLE.md section 3 "A suggested kick order", section 10 kick-ready readout): the fixed order the
/// party's kicks come off cooldown. Pure: no Godot, no state, no clock beyond the tick passed in.
/// Ordering is deterministic: seats ready at the tick come first, then soonest ready (the earliest ready tick),
/// then seat id ascending. A tie never depends on input order.
/// </summary>
public static class KickOrder
{
    /// <summary>The kick order for the seats at <paramref name="tick"/>, 1..N.</summary>
    /// <param name="seats">Seats with an interrupt (each seat once).</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>One slot per seat, Order 1 first.</returns>
    public static IReadOnlyList<KickSlot> Compute(IEnumerable<KickSeat> seats, int tick)
    {
        ArgumentNullException.ThrowIfNull(seats);
        return seats
            .OrderBy(s => s.ReadyTick > tick ? 1 : 0)
            .ThenBy(s => s.ReadyTick)
            .ThenBy(s => s.SeatId)
            .Select((s, i) => new KickSlot(s.SeatId, i + 1, s.ReadyTick <= tick))
            .ToList();
    }

    /// <summary>
    /// Numbers casters in the order they are given: caster i (0-based) gets the kick at place i+1 of <paramref name="order"/>.
    /// A caster with no kick place left (more casters than seats) gets no number.
    /// </summary>
    /// <param name="casterIds">Kickable casters, in the order they are to be numbered.</param>
    /// <param name="order">The kick order from <see cref="Compute"/>.</param>
    /// <returns>Caster id to its kick number (1-based).</returns>
    public static IReadOnlyDictionary<int, int> NumberCasters(IReadOnlyList<int> casterIds, IReadOnlyList<KickSlot> order)
    {
        ArgumentNullException.ThrowIfNull(casterIds);
        ArgumentNullException.ThrowIfNull(order);
        var numbers = new Dictionary<int, int>();
        for (var i = 0; i < casterIds.Count && i < order.Count; i++)
        {
            numbers[casterIds[i]] = i + 1;
        }
        return numbers;
    }
}
