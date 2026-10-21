using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/healer-setup.ts, CADENCE-SETUP (finding f-b31e8f34; design/BIBLE.md section 4, "synergy is a
/// timing game between two people", and section 5, Chanter Cadence "Setup: applies Attuned Window"). With nobody
/// under the heal threshold, the healer opens its on-target Setup Window on an enemy a Payoff partner is about to hit,
/// the way a WoW Mythic+ healer adds damage setups during downtime. Before this, Cadence was cast only as a heal, so
/// Attuned always landed on an ally while its Payoffs (Anthem, Flashfire; content/data/combos.json) are cast at
/// enemies, and no Attuned Combo ever closed in bot play.
/// </summary>
/// <remarks>
/// The rule, in order:
/// <list type="number">
/// <item>Either verse: Cadence plays the caster's verse variant on its target, but its HoT (Mend) or shield (Ward) never
/// lands on an enemy (Casting.ApplyEffects HEAL-NEVER-HOSTILE, #390), so only the Attuned Window does.</item>
/// <item>The Setup is the first of the kit's Setup abilities that is target-shaped in content (a circle Setup such as
/// Hymn or Group Song resolves onto friendlies), ready and affordable.</item>
/// <item>The partner is the first living damage-role ally (actor id order) of another class, within
/// <see cref="DamageRole.PayoffAllyRangeMm"/> of the healer, with no Window it could already close, with no Setup of
/// its own ready (the healer fills the gaps in the partner's own Window chain, never takes the slot from it), and with
/// a Payoff ability affordable now and off cooldown by the tick the Setup lands (<see cref="Readiness.LandingTick"/>
/// plus the Setup's cast time): the readiness test the kick ledger uses for its kickers. Damage role only: it is the
/// role that hunts Windows to close (<see cref="DamageRole"/>'s Payoff-on-Window step); a tank's Payoff closes only
/// what sits on its own target, and its Reckoning is the party's Forge closer on Cinder's Kindled, so an Attuned on
/// the tank's target trades Forge away (measured before the own-Setup rule: hv-k2-s7 clear 1625 to 2226 with the tank
/// as a partner, 1764 without; 1730 with both rules).</item>
/// <item>The target is that partner's target: a living enemy (not idle) carrying no open Window (the sim's Payoff reads
/// the target's first open Window, so an older one would shadow Attuned) and in the Setup's range.</item>
/// </list>
/// </remarks>
public static class HealerSetup
{
    private const string LockWindowPrefix = "lockwindow:";

    private static bool HasOpenWindow(ActorState actor, int tick) =>
        actor.Effects.Any(e => e.EndsTick > tick
            && (e.EffectId.StartsWith(Pings.WindowEffectPrefix, StringComparison.Ordinal)
                || e.EffectId.StartsWith(LockWindowPrefix, StringComparison.Ordinal)));

    private static bool PayoffReadyBy(BotView view, ActorState partner, int landing)
    {
        var kit = RoleShared.KitOf(view, partner.ClassId);
        return kit.PayoffAbilityIds.Any(id => Readiness.CooldownReadyTick(partner, id) <= landing && RoleShared.Affordable(partner, id, kit));
    }

    private static bool OwnSetupReady(BotView view, ActorState partner, int tick)
    {
        var kit = RoleShared.KitOf(view, partner.ClassId);
        return kit.SetupAbilityIds.Any(id => RoleShared.ReadyAndAffordable(partner, id, tick, kit));
    }

    /// <summary>healer-setup.ts setupCommands.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The healer.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The Setup cast, or empty.</returns>
    public static IReadOnlyList<Command> SetupCommands(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var tick = view.State.Tick;
        var actors = view.State.Actors;
        if (view.Kits.Content is not { } reg)
        {
            return Array.Empty<Command>();
        }
        AbilityDef? setup = null;
        foreach (var id in kit.SetupAbilityIds)
        {
            if (reg.Abilities.TryGetValue(id, out var def) && def.Shape.Kind == ShapeKind.Target && RoleShared.ReadyAndAffordable(self, id, tick, kit))
            {
                setup = def;
                break;
            }
        }
        if (setup is null)
        {
            return Array.Empty<Command>();
        }
        var landing = Readiness.LandingTick(self, tick) + setup.CastTicks;
        foreach (var partner in actors.Values)
        {
            if (partner.Kind != ActorKind.Player || !partner.Alive || partner.Id == self.Id || partner.Role != ActorRole.Damage
                || string.Equals(partner.ClassId, self.ClassId, StringComparison.Ordinal)
                || RoleShared.Dist(partner, self) > DamageRole.PayoffAllyRangeMm
                || BotWindows.FindClosableWindow(actors, tick, partner.ClassId) is not null
                || OwnSetupReady(view, partner, tick)
                || !PayoffReadyBy(view, partner, landing))
            {
                continue;
            }
            if (partner.TargetId is not int tid || !actors.TryGetValue(tid, out var target)
                || target.Kind != ActorKind.Enemy || !target.Alive || target.Idle
                || HasOpenWindow(target, tick) || RoleShared.Dist(target, self) > setup.Range)
            {
                continue;
            }
            return new Command[] { new CastCommand(tick, self.Id, setup.Id, target.Id) };
        }
        return Array.Empty<Command>();
    }
}
