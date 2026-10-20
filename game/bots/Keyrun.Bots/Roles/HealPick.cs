using System;
using System.Collections.Generic;
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

        if (target.Id == self.Id && Ready(heals.SelfHealAbilityId))
        {
            return new Command[] { new CastCommand(tick, self.Id, heals.SelfHealAbilityId!) };
        }
        if (d <= heals.GroupHealRadiusMm && Ready(heals.GroupHealAbilityId))
        {
            return new Command[] { new CastCommand(tick, self.Id, heals.GroupHealAbilityId!) };
        }
        if (heals.SecondGroupHeal is { } second && Ready(second.AbilityId))
        {
            if (d <= second.RadiusMm - HotPoolMarginMm)
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
            return Ready(targeted) ? new Command[] { new CastCommand(tick, self.Id, targeted, target.Id) } : Array.Empty<Command>();
        }
        if (target.Id == self.Id)
        {
            return Array.Empty<Command>();
        }
        return Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(target.Pos)) is { } move ? new Command[] { move } : Array.Empty<Command>();
    }
}
