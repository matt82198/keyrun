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

        var preferred = (closable ? fillers.FirstOrDefault(id => kit.PayoffAbilityIds.Contains(id)) : null)
            ?? (windows.Count == 0 ? fillers.FirstOrDefault(IsSetup) : fillers.FirstOrDefault(id => !IsSetup(id)))
            ?? fillers[0];
        var ordered = new List<string> { preferred };
        ordered.AddRange(fillers.Where(id => !string.Equals(id, preferred, StringComparison.Ordinal)));

        var output = new List<string>(rotation.Count);
        var next = 0;
        foreach (var id in rotation)
        {
            output.Add(fillers.Contains(id) ? ordered[next++] : id);
        }
        return output;
    }
}
