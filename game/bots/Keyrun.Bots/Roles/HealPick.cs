using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/heal-pick.ts: shape-aware heal choice. Given the triage target, the cast that actually
/// reaches it (self heal on itself; group heal inside its radius; the second group heal, walking into its radius
/// first; the targeted heal within range), else a move toward the target.
/// </summary>
public static class HealPick
{
    /// <summary>heal-pick.ts HEAL_RANGE_MM: fallback ally-heal reach, 30 m.</summary>
    public const long HealRangeMm = 30 * 1000;

    /// <summary>heal-pick.ts HOT_POOL_MARGIN_MM: stand this far inside a heal-over-time pool's radius.</summary>
    private const long HotPoolMarginMm = 500;

    /// <summary>
    /// heal-pick.ts CHARGE_EMERGENCY_PCT (R-PILE-7d): under this health fraction the heal target is an emergency, the
    /// one trigger that spends a banked charge while another heal that reaches it is ready.
    /// </summary>
    public const double ChargeEmergencyPct = 0.35;

    /// <summary>
    /// CHARGE-SPEND (ruling R-PILE-7d, finding f-f44791bc; the WoW Mythic+ rule for a charged ability): whether the
    /// healer spends a charge of <paramref name="abilityId"/> now. A one-charge ability always does (its decision is
    /// unchanged). A multi-charge ability (the kit's ChargedAbilities) does when every charge is full, so the recharge
    /// is never wasted (the sim's Cooldowns.ChargesAvailable is full once <c>cooldowns[id] &lt;= tick</c>); otherwise
    /// the spare is banked and spent only on a real trigger: the target under <see cref="ChargeEmergencyPct"/>, or no
    /// other heal that reaches the target ready this GCD. So a banked charge is never pressed back to back on a target
    /// that was just healed.
    /// </summary>
    /// <param name="self">The healer.</param>
    /// <param name="abilityId">The heal about to be pressed (already ready and affordable).</param>
    /// <param name="tick">Current tick.</param>
    /// <param name="kit">The healer's kit.</param>
    /// <param name="target">The heal target.</param>
    /// <param name="otherHealReady">True when another heal that reaches <paramref name="target"/> is ready now.</param>
    /// <returns>True to press it.</returns>
    public static bool SpendsCharge(ActorState self, string abilityId, int tick, EffectiveKit kit, ActorState target, Func<bool> otherHealReady)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(otherHealReady);
        if (!kit.ChargedAbilities.TryGetValue(abilityId, out var charges)
            || charges.CooldownTicks <= 0
            || Readiness.CooldownReadyTick(self, abilityId) <= tick)
        {
            return true;
        }
        return (double)target.Health / target.MaxHealth < ChargeEmergencyPct || !otherHealReady();
    }

    private static double RangeOf(BotView view, string abilityId) =>
        view.Kits.Content is { } reg && reg.Abilities.TryGetValue(abilityId, out var def) ? def.Range : HealRangeMm;

    /// <summary>
    /// A heal that is a verse swap playing no heal from the healer's current verse (Chanter Refrain cast from Mend) is
    /// not a heal: casting it would flip the Chanter out of its verse for a shield (<see cref="KitHeals.SwapsVerseWithoutHealing"/>).
    /// </summary>
    private static bool LeavesVerseUnhealed(BotView view, ActorState self, string abilityId) =>
        view.Kits.Content is { } reg
        && reg.Abilities.TryGetValue(abilityId, out var def)
        && KitHeals.SwapsVerseWithoutHealing(def, Keyrun.Sim.Combat.Actors.Verse(self), reg.Effects);

    /// <summary>heal-pick.ts healCommands.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The healer.</param>
    /// <param name="kit">Its kit.</param>
    /// <param name="target">Triage target.</param>
    /// <returns>Commands, possibly empty.</returns>
    public static IReadOnlyList<Command> HealCommands(BotView view, ActorState self, EffectiveKit kit, ActorState target)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(target);
        var tick = view.State.Tick;
        bool Ready(string? id) => id is not null && RoleShared.ReadyAndAffordable(self, id, tick, kit) && !LeavesVerseUnhealed(view, self, id);
        var heals = kit.Heals;
        var d = RoleShared.Dist(self, target);
        bool Reaches(string? id) => id is not null
            && ((id == heals.SelfHealAbilityId && target.Id == self.Id)
                || (id == heals.GroupHealAbilityId && d <= heals.GroupHealRadiusMm)
                || (heals.SecondGroupHeal is { } sg && id == sg.AbilityId && d <= sg.RadiusMm - HotPoolMarginMm)
                || (id == heals.TargetHealAbilityId && d <= RangeOf(view, id)));
        var healIds = new[] { heals.SelfHealAbilityId, heals.GroupHealAbilityId, heals.SecondGroupHeal?.AbilityId, heals.TargetHealAbilityId };
        bool Press(string? id) => Ready(id)
            && SpendsCharge(self, id!, tick, kit, target, () => healIds.Any(o => o is not null && o != id && Reaches(o) && Ready(o)));

        if (target.Id == self.Id && Press(heals.SelfHealAbilityId))
        {
            return new Command[] { new CastCommand(tick, self.Id, heals.SelfHealAbilityId!) };
        }
        if (d <= heals.GroupHealRadiusMm && Press(heals.GroupHealAbilityId))
        {
            return new Command[] { new CastCommand(tick, self.Id, heals.GroupHealAbilityId!) };
        }
        if (heals.SecondGroupHeal is { } second && Ready(second.AbilityId))
        {
            if (d <= second.RadiusMm - HotPoolMarginMm && Press(second.AbilityId))
            {
                return new Command[] { new CastCommand(tick, self.Id, second.AbilityId) };
            }
            if (self.CastId is null && target.Id != self.Id && Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(target.Pos)) is { } walk)
            {
                return new Command[] { walk };
            }
        }
        if (heals.TargetHealAbilityId is { } targeted && d <= RangeOf(view, targeted))
        {
            return Press(targeted) ? new Command[] { new CastCommand(tick, self.Id, targeted, target.Id) } : Array.Empty<Command>();
        }
        if (target.Id == self.Id)
        {
            return Array.Empty<Command>();
        }
        return Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(target.Pos)) is { } move ? new Command[] { move } : Array.Empty<Command>();
    }
}
