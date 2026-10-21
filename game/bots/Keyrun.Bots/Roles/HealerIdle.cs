using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/healer-idle.ts (BOTS-FIVE-BUTTONS; design/BIBLE.md section 0 test 1, "Classes with real
/// decisions on five buttons"): what the healer does in combat with nobody to heal or dispel. Before this the healer
/// stood idle, so Chanter's Dissonance (its damaging Payoff) and Refrain (the verse swap, needed only from Ward) were
/// never cast. In order: close another class's open Window with a damaging Payoff (the Combo); the damaging Payoff on
/// an engaged enemy (HEALER-KEEPS-PRESSING: with nobody to heal the first GCD goes to a non-heal button, R-BOTS-14);
/// else, in Mend, Refrain into Ward (its Ward burst shields on the swap, and the healer pre-mitigates until someone
/// drops, when <see cref="HealerRole"/> Refrains back to Mend). Out of combat (no living enemy aimed at a player) the
/// healer does nothing, so it never pulls.
/// </summary>
public static class HealerIdle
{
    /// <summary>True when the kit has a stance swap that is ready and affordable now.</summary>
    /// <param name="self">The healer.</param>
    /// <param name="kit">Its kit.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>Whether Refrain can be cast.</returns>
    public static bool StanceSwapReady(ActorState self, EffectiveKit kit, int tick)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        return kit.LoopKind == LoopKind.Stance
            && !string.IsNullOrEmpty(kit.StanceSwapAbilityId)
            && RoleShared.ReadyAndAffordable(self, kit.StanceSwapAbilityId, tick, kit);
    }

    private static bool Damages(AbilityDef def) =>
        def.Effects.Any(op => op is DamageOp) || (def.Variants?.Values.Any(ops => ops.Any(op => op is DamageOp)) ?? false);

    /// <summary>healer-idle.ts idleCommands.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The healer.</param>
    /// <param name="kit">Its kit.</param>
    /// <param name="mendVerseValue">The resource value of the Mend verse.</param>
    /// <returns>Commands, possibly empty.</returns>
    public static IReadOnlyList<Command> IdleCommands(BotView view, ActorState self, EffectiveKit kit, long mendVerseValue)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var tick = view.State.Tick;
        var actors = view.State.Actors;
        var engaged = actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.Alive && a.TargetId is { } t && actors.TryGetValue(t, out var aimed) && aimed.Kind == ActorKind.Player)
            .ToList();
        if (engaged.Count == 0 || view.Kits.Content is not { } reg)
        {
            return Array.Empty<Command>();
        }

        var damagingPayoffs = kit.PayoffAbilityIds
            .Where(id => reg.Abilities.TryGetValue(id, out var def) && Damages(def))
            .ToList();
        List<Command>? CastOn(ActorState target)
        {
            var abilityId = damagingPayoffs.FirstOrDefault(id =>
                RoleShared.ReadyAndAffordable(self, id, tick, kit) && RoleShared.Dist(self, target) <= reg.Abilities[id].Range);
            if (abilityId is null)
            {
                return null;
            }
            var commands = new List<Command>();
            if (self.TargetId != target.Id)
            {
                commands.Add(new TargetCommand(tick, self.Id, target.Id));
            }
            commands.Add(new CastCommand(tick, self.Id, abilityId, target.Id));
            return commands;
        }

        if (BotWindows.FindClosableWindow(actors, tick, self.ClassId) is { } win
            && actors.TryGetValue(win.TargetId, out var windowTarget)
            && CastOn(windowTarget) is { } payoff)
        {
            return payoff;
        }
        var target = engaged.FirstOrDefault(e => e.Id == self.TargetId) ?? engaged[0];
        if (CastOn(target) is { } press)
        {
            return press;
        }
        if (StanceSwapReady(self, kit, tick) && self.Resource == mendVerseValue)
        {
            return new Command[] { new CastCommand(tick, self.Id, kit.StanceSwapAbilityId!) };
        }
        return Array.Empty<Command>();
    }
}
