using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/damage.ts, the damage state machine: kick the priority cast, flee a hostile floor patch,
/// close another class's open Window with a Payoff, open one with a Setup when a Payoff-capable ally is near, else
/// cycle the cooldown- and resource-aware rotation; with nothing ready, a melee bot re-approaches its target and a bot
/// walks into a friendly floor Window it can close.
/// </summary>
public sealed class DamageRole : IRole
{
    /// <summary>damage.ts PAYOFF_ALLY_RANGE_MM: 30 m.</summary>
    public const long PayoffAllyRangeMm = 30 * 1000;

    /// <summary>The shared instance.</summary>
    public static DamageRole Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<Command> Decide(BotView view, ActorState self) => DamageDecide(view, self);

    private static int? PickAnyEnemyTarget(SortedDictionary<int, ActorState> actors) =>
        actors.Values.FirstOrDefault(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle)?.Id;

    private static List<Command> PayoffOnWindowCommand(BotView view, ActorState self, EffectiveKit kit)
    {
        var tick = view.State.Tick;
        var commands = new List<Command>();
        if (kit.PayoffAbilityIds.Count == 0)
        {
            return commands;
        }
        var win = BotWindows.FindClosableWindow(view.State.Actors, tick, self.ClassId);
        if (win is null)
        {
            return commands;
        }
        var payoffId = kit.PayoffAbilityIds.FirstOrDefault(id => RoleShared.ReadyAndAffordable(self, id, tick, kit) && RoleShared.FitsOwedKick(view, self, id));
        if (payoffId is null)
        {
            return commands;
        }
        if (self.TargetId != win.TargetId)
        {
            commands.Add(new TargetCommand(tick, self.Id, win.TargetId));
        }
        commands.Add(new CastCommand(tick, self.Id, payoffId, win.TargetId));
        return commands;
    }

    private static bool PayoffAllyInRange(BotView view, ActorState self) =>
        view.State.Actors.Values.Any(a =>
        {
            if (a.Kind != ActorKind.Player || !a.Alive || a.Id == self.Id || string.Equals(a.ClassId, self.ClassId, StringComparison.Ordinal))
            {
                return false;
            }
            if (RoleShared.KitOf(view, a.ClassId).PayoffAbilityIds.Count == 0)
            {
                return false;
            }
            return RoleShared.Dist(a, self) <= PayoffAllyRangeMm;
        });

    private static List<Command> SetupForAllyCommand(BotView view, ActorState self, EffectiveKit kit)
    {
        var tick = view.State.Tick;
        var commands = new List<Command>();
        if (kit.SetupAbilityIds.Count == 0 || BotWindows.OpenWindowsOn(view.State.Actors, tick).Count > 0 || !PayoffAllyInRange(view, self))
        {
            return commands;
        }
        var setupId = kit.SetupAbilityIds.FirstOrDefault(id => RoleShared.ReadyAndAffordable(self, id, tick, kit) && RoleShared.FitsOwedKick(view, self, id));
        if (setupId is null)
        {
            return commands;
        }
        var targetId = self.TargetId ?? PickAnyEnemyTarget(view.State.Actors);
        if (targetId is null)
        {
            return commands;
        }
        if (self.TargetId != targetId)
        {
            commands.Add(new TargetCommand(tick, self.Id, targetId));
        }
        commands.Add(new CastCommand(tick, self.Id, setupId, targetId));
        return commands;
    }

    /// <summary>
    /// damage.ts pickResourceAwareRotation: the first ready, affordable rotation ability; on a heat-overheat loop a
    /// builder that would reach the cap yields to a ready controlled dump when one exists.
    /// </summary>
    /// <param name="kit">Effective kit (its rotation is the priority list).</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>Ability id, or null.</returns>
    public static string? PickResourceAwareRotation(EffectiveKit kit, ActorState self, int tick)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(self);
        bool Ready(string id) => RoleShared.ReadyAndAffordable(self, id, tick, kit);
        var picked = Priority.PickRotationAbility(kit.Rotation, Ready);
        if (picked is null || kit.LoopKind != LoopKind.HeatOverheat)
        {
            return picked;
        }
        var gain = kit.ResourceGainByAbility.TryGetValue(picked, out var g) ? g : 0;
        // TS: kit.resourceMax ?? Infinity. A double keeps the comparison exact for any long cap.
        var max = kit.ResourceMax is { } m ? (double)m : double.PositiveInfinity;
        if (gain <= 0 || self.Resource + gain < max)
        {
            return picked;
        }
        return kit.ResourceDumpAbilityIds.FirstOrDefault(Ready) ?? picked;
    }

    /// <summary>damage.ts damageDecide.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <returns>Commands, possibly empty.</returns>
    public static IReadOnlyList<Command> DamageDecide(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var tick = view.State.Tick;
        var kit = RoleShared.KitOf(view, self.ClassId);

        if (RoleShared.LedgerKickCommand(view, self, kit) is { } kick)
        {
            return new Command[] { Reasons.Tag(kick, "damage:ledger-kick") };
        }
        if (Floor.FloorEscapeCommand(view, self) is { } escape)
        {
            return new Command[] { Reasons.Tag(escape, "damage:floor-escape") };
        }
        if (BreakBarKick.Command(view, self, kit) is { } barKick)
        {
            return new Command[] { Reasons.Tag(barKick, "damage:break-bar-kick") };
        }
        // BOSS-TEACHES (R-BOSS-1): the designated setter's Setup into the boss's Lock first; every other Setup on the
        // boss is held while the Lock is under way (LockAnswer). R-LOCK-SETTER (#573): ahead of a Payoff, which spends the
        // setter's global on some other Window while the Lock waits for its Setup (a Lock failed that way).
        var lockSetup = LockAnswer.SetupCommand(view, self, kit);
        if (lockSetup.Count > 0)
        {
            return Reasons.TagAll(lockSetup, "damage:lock-setup");
        }
        var payoff = PayoffOnWindowCommand(view, self, kit);
        if (payoff.Count > 0)
        {
            return Reasons.TagAll(payoff, "damage:payoff");
        }
        var setup = SetupForAllyCommand(view, self, kit);
        if (setup.Count > 0 && !LockAnswer.Holds(view, self, setup))
        {
            return Reasons.TagAll(setup, "damage:setup");
        }
        var arranged = AoeGate.Filter(view, self, Fillers.ArrangeFillers(view, self, kit, kit.Rotation, self.TargetId), self.TargetId);
        // R-KICKLOCK-1: a rotation ability (or resource dump) that would complete past an owed kick's deadline is skipped, so
        // the rotation picks the next ability that fits, or holds.
        var rotationKit = kit with
        {
            Rotation = arranged.Where(id => (!LockAnswer.HoldsSetup(view, self, self.TargetId, id) || !kit.SetupAbilityIds.Contains(id))
                && RoleShared.FitsOwedKick(view, self, id)).ToList(),
            ResourceDumpAbilityIds = kit.ResourceDumpAbilityIds.Where(id => RoleShared.FitsOwedKick(view, self, id)).ToList(),
        };
        if (PickResourceAwareRotation(rotationKit, self, tick) is { } abilityId)
        {
            return Reasons.TagAll(AoeGate.PressCommands(view, self, abilityId, self.TargetId), "damage:rotation");
        }
        if (Floor.MeleeReapproachCommand(view, self) is { } reapproach)
        {
            return new Command[] { Reasons.Tag(reapproach, "damage:melee-reapproach") };
        }
        if (Floor.FriendlyWindowApproachCommand(view, self) is { } approach)
        {
            return new Command[] { Reasons.Tag(approach, "damage:window-approach") };
        }
        return Array.Empty<Command>();
    }
}
