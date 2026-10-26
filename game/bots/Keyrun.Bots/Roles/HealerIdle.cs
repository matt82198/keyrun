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
/// drops, when <see cref="HealerRole"/> Refrains back to Mend). R-BOTS-17: in Mend the Payoff takes the fight's first
/// idle GCD only; after that the Refrain into Ward comes before any further press. Out of combat (no living enemy aimed
/// at a player) the healer does nothing, so it never pulls.
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

    /// <summary>
    /// R-BOTS-17: true when this idle GCD is NOT the fight's first, so the Payoff has already had its turn and the
    /// Refrain into Ward comes first. A fight's first idle GCD follows a lull: the healer's GCD has been free for more
    /// than a whole GCD (out of combat it presses nothing), or a damaging Payoff (it has a cooldown, so
    /// Combat/Cooldowns.cs ConsumeCharge leaves an entry) has never been pressed at all. Mid-fight the healer presses
    /// every GCD, so its GCD came off within the last one.
    /// </summary>
    /// <param name="self">The healer.</param>
    /// <param name="reg">Content registry.</param>
    /// <param name="damagingPayoffs">The kit's damaging Payoff ids.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>Whether the Payoff's first idle GCD is already spent in this fight.</returns>
    private static bool PayoffHadFirstIdleGcd(ActorState self, ContentRegistry reg, IReadOnlyList<string> damagingPayoffs, int tick)
    {
        if (damagingPayoffs.Count == 0 || !damagingPayoffs.Any(id => self.Cooldowns.ContainsKey(id)))
        {
            return false;
        }
        var gcd = damagingPayoffs.Max(id => reg.Abilities[id].GcdTicks);
        return tick - self.GcdReadyTick <= gcd;
    }

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
            // R-TARGET-1 (b): a shaped AoE whose shape holds an idle or dormant enemy at this target is withheld this tick, so
            // the press falls through to a non-AoE damaging Payoff or, failing that, the filler. The veto lives in AoeGate.
            var abilityId = damagingPayoffs.FirstOrDefault(id =>
                RoleShared.ReadyAndAffordable(self, id, tick, kit)
                && RoleShared.Dist(self, target) <= reg.Abilities[id].Range
                && !AoeGate.IdlePackVetoed(view, self, reg.Abilities[id], target.Id));
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
        // R-BOTS-17: in Mend the damaging Payoff takes the fight's FIRST idle GCD only; after that the Refrain into Ward
        // comes before the press again (the pre-R-BOTS-14 priority resumes).
        var refrainIntoWard = StanceSwapReady(self, kit, tick) && self.Resource == mendVerseValue;
        if (refrainIntoWard && PayoffHadFirstIdleGcd(self, reg, damagingPayoffs, tick))
        {
            return new Command[] { new CastCommand(tick, self.Id, kit.StanceSwapAbilityId!) };
        }
        var target = engaged.FirstOrDefault(e => e.Id == self.TargetId) ?? engaged[0];
        // R-AOE-1: a shaped AoE Payoff (Dissonance) goes at the engaged enemy with the most enemies around it, the current
        // target keeping its place on a tie, so the press lands on the pack and not on the straggler.
        if (damagingPayoffs.Select(id => reg.Abilities[id]).FirstOrDefault(AoeGate.IsShapedAoeDamage) is { } aoe)
        {
            var reachable = engaged.Where(e => RoleShared.Dist(self, e) <= aoe.Range).ToList();
            if (reachable.Remove(target))
            {
                reachable.Insert(0, target);
            }
            target = AoeGate.ClusterTarget(view, self, aoe, reachable) ?? target;
        }
        if (CastOn(target) is { } press)
        {
            return press;
        }
        if (refrainIntoWard)
        {
            return new Command[] { new CastCommand(tick, self.Id, kit.StanceSwapAbilityId!) };
        }
        return Array.Empty<Command>();
    }
}
