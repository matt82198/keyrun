using System;
using System.Linq;

namespace Keyrun.Sim.Encounter;

/// <summary>The Break bar an enemy's unit frame draws (BREAK-BAR).</summary>
/// <param name="BarId">The bar's id in <see cref="EncounterState.BreakBars"/>.</param>
/// <param name="Bar">The sim's bar this tick (drains from <c>Max</c> to 0; 0 is staggered until it refills).</param>
public sealed record BossBreakBar(string BarId, BreakBarState Bar);

/// <summary>
/// Read-only selector for the HUD (BREAK-BAR; BIBLE s9 mechanic 2, the Twins "share one Break bar"). Nothing in the
/// sim steps through here: it only names which live bar a boss frame shows. Display only.
/// </summary>
public static class BossBreakBarReadout
{
    /// <summary>
    /// The bar <paramref name="actorId"/>'s frame shows: for the armed boss, the Twins' one shared bar
    /// (<see cref="BossConfig.TwinsBar"/>) once the encounter has created it (the step both twins are alive); for any
    /// other enemy, the first bar (in JS key order) that enemy itself shares. Null when there is no such bar.
    /// </summary>
    /// <param name="encounter">The encounter state.</param>
    /// <param name="actorId">The framed enemy.</param>
    /// <returns>The bar, or null.</returns>
    public static BossBreakBar? For(EncounterState encounter, int actorId)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        if (encounter.Boss is { } boss && boss.Config.BossActorId == actorId)
        {
            var id = boss.Config.TwinsBar.Id;
            return encounter.BreakBars.TryGetValue(id, out var twins) ? new BossBreakBar(id, twins) : null;
        }
        foreach (var kv in encounter.BreakBars)
        {
            if (kv.Value.ActorIds.Contains(actorId))
            {
                return new BossBreakBar(kv.Key, kv.Value);
            }
        }
        return null;
    }
}
