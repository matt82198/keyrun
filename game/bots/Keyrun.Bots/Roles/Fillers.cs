using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/fillers.ts (BOTS-FIVE-BUTTONS; design/BIBLE.md section 0 test 1, "Classes with real decisions
/// on five buttons"): the filler decision. A filler is a rotational ability that costs nothing and has no cooldown
/// (Warden's Strike and Guard Break, Cinder's Ember Bolt and Flare), so the base tier order alone would cast the same
/// one forever and leave the other button dead. Among the fillers, on the cast's target: a Payoff filler first when the
/// target carries a Window this class may close (The Lock), then a Setup filler whose Window is not yet open on the
/// target (keep the debuff up), then the first filler whose Setup Window is not already open. Every other rotation
/// entry keeps its place; the fillers fill the filler positions in that preference.
/// </summary>
public static class Fillers
{
    /// <summary>
    /// fillers.ts arrangeFillers: <paramref name="rotation"/> with its fillers reordered for <paramref name="targetId"/>;
    /// unchanged with no content registry, no target, or fewer than two fillers.
    /// </summary>
    /// <param name="view">Bot view (its registry and actor table).</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="kit">Its kit.</param>
    /// <param name="rotation">The rotation to arrange (the kit's, or the tank's pressure rotation).</param>
    /// <param name="targetId">The cast's target, or null.</param>
    /// <returns>The arranged rotation.</returns>
    public static IReadOnlyList<string> ArrangeFillers(BotView view, ActorState self, EffectiveKit kit, IReadOnlyList<string> rotation, int? targetId)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(rotation);
        if (view.Kits.Content is not { } reg || targetId is not { } tid)
        {
            return rotation;
        }
        var fillers = rotation
            .Where(id => RoleShared.Cost(kit, id) == 0 && reg.Abilities.TryGetValue(id, out var def) && def.CooldownTicks == 0)
            .ToList();
        if (fillers.Count < 2)
        {
            return rotation;
        }

        var windows = BotWindows.OpenWindowsOn(view.State.Actors, view.State.Tick).Where(w => w.TargetId == tid).ToList();
        var closable = windows.Any(w => BotWindows.CanClose(w.SetupClass, self.ClassId));
        bool IsSetup(string id) => kit.WindowIdBySetupAbility.ContainsKey(id);

        // f-c356528f: every filler is ready on every global, so the window preference alone cast the same one forever.
        // The least recently cast filler (never cast first) goes first; the window preference below breaks ties.
        int Age(string id)
        {
            for (var i = view.FillerHistory.Count - 1; i >= 0; i--)
            {
                if (string.Equals(view.FillerHistory[i], id, StringComparison.Ordinal))
                {
                    return view.FillerHistory.Count - i;
                }
            }
            return int.MaxValue;
        }
        var oldest = fillers.Max(Age);
        var candidates = fillers.Where(id => Age(id) == oldest).ToList();

        var preferred = (closable ? fillers.FirstOrDefault(id => kit.PayoffAbilityIds.Contains(id)) : null)
            ?? (windows.Count == 0 ? candidates.FirstOrDefault(IsSetup) : candidates.FirstOrDefault(id => !IsSetup(id)))
            ?? candidates[0];
        var ordered = new List<string> { preferred };
        ordered.AddRange(fillers.Where(id => !string.Equals(id, preferred, StringComparison.Ordinal)).OrderByDescending(Age));

        var output = new List<string>(rotation.Count);
        var next = 0;
        foreach (var id in rotation)
        {
            output.Add(fillers.Contains(id) ? ordered[next++] : id);
        }
        return output;
    }

    /// <summary>
    /// The seat's filler history after <paramref name="decided"/>: each filler cast in it moves to the most recent end.
    /// Unchanged when the decision casts no filler.
    /// </summary>
    /// <param name="view">Bot view (its registry).</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="history">History before this decision, least recently cast first.</param>
    /// <param name="decided">The role's commands this tick.</param>
    /// <returns>The next history.</returns>
    public static IReadOnlyList<string> RecordCasts(BotView view, ActorState self, IReadOnlyList<string> history, IReadOnlyList<Command> decided)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(decided);
        if (view.Kits.Content is not { } reg)
        {
            return history;
        }
        var kit = RoleShared.KitOf(view, self.ClassId);
        var next = history;
        foreach (var cast in decided.OfType<CastCommand>())
        {
            if (RoleShared.Cost(kit, cast.AbilityId) != 0 || !reg.Abilities.TryGetValue(cast.AbilityId, out var def) || def.CooldownTicks != 0)
            {
                continue;
            }
            next = next.Where(id => !string.Equals(id, cast.AbilityId, StringComparison.Ordinal)).Append(cast.AbilityId).ToList();
        }
        return next;
    }
}
