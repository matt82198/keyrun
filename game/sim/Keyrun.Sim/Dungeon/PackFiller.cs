using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Dungeon;

/// <summary>One pack placed in a generated dungeon.</summary>
/// <param name="RoomId">Its room.</param>
/// <param name="Slot">Its slot index in the room.</param>
/// <param name="PackId">The roster pack id.</param>
/// <param name="Points">Its forces points.</param>
/// <param name="Center">Its aggro ring centre, mm.</param>
public sealed record PackPlacement(int RoomId, int Slot, string PackId, int Points, Vec3 Center);

/// <summary>
/// Stage 4, fill the slots from the pack roster (stub: a seeded draw, no themes, links or enemy kits yet). Each room
/// draws packs slot by slot until its quota is met; when a draw would leave a deficit the remaining slots cannot cover
/// even with the biggest pack, the biggest pack is placed instead, so every quota is met by construction. Pure over
/// (seed, quotas, roster).
/// </summary>
public static class PackFiller
{
    /// <summary>Fills every room's quota from <paramref name="roster"/>.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="quotas">Stage 3's output.</param>
    /// <param name="roster">The pack roster.</param>
    /// <returns>Placements in room then slot order.</returns>
    /// <exception cref="ArgumentException">A bad roster.</exception>
    /// <exception cref="InvalidOperationException">A quota the room's slots cannot meet (stage 3 should prevent it).</exception>
    public static IReadOnlyList<PackPlacement> Fill(long seed, IReadOnlyList<RoomQuota> quotas, IReadOnlyList<RosterPack> roster)
    {
        ArgumentNullException.ThrowIfNull(quotas);
        PackSlots.ValidateRoster(roster);
        var biggest = roster.Aggregate((best, r) => r.Points > best.Points ? r : best);
        var rng = DungeonPipeline.StageRng(seed, 4);
        var placed = new List<PackPlacement>();
        foreach (var q in quotas)
        {
            long deficit = q.QuotaPoints;
            for (var slot = 0; slot < q.Slots.Count && deficit > 0; slot++)
            {
                var (index, next) = Prng.NextInt(rng, roster.Count);
                rng = next;
                var pick = roster[(int)index];
                var slotsAfter = q.Slots.Count - slot - 1;
                if (deficit - pick.Points > (long)slotsAfter * biggest.Points)
                {
                    pick = biggest;
                }
                placed.Add(new PackPlacement(q.RoomId, slot, pick.Id, pick.Points, q.Slots[slot]));
                deficit -= pick.Points;
            }
            if (deficit > 0)
            {
                throw new InvalidOperationException($"pack filler: room {q.RoomId} is {deficit} points short of its quota");
            }
        }
        return placed;
    }
}
