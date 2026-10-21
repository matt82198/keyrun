using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/tank-engage.ts, the tank's ENGAGE behaviour: pick up every loose enemy (taunt one in range),
/// walk into melee, face the target and cast the pressure-aware rotation. The sim adds threat only through explicit
/// threat ops, so a tank "holds" an enemy iff its threat entry is at least as high as every other entry.
/// </summary>
public static class TankEngage
{
    /// <summary>tank-engage.ts DEFAULT_REACH_MM: fallback melee reach (Warden's 3000 mm swing).</summary>
    public const long DefaultReachMm = 3000;

    /// <summary>tank-engage.ts REACH_MARGIN_MM: walk this far inside reach.</summary>
    public const long ReachMarginMm = 500;

    private static long? RangeOf(BotView view, string? abilityId)
    {
        if (abilityId is null || view.Kits.Content is not { } reg || !reg.Abilities.TryGetValue(abilityId, out var def))
        {
            return null;
        }
        return def.Range;
    }

    /// <summary>tank-engage.ts reachMm: the shortest positive range among the rotation's abilities.</summary>
    private static long ReachMm(BotView view, EffectiveKit kit)
    {
        var ranges = kit.Rotation.Select(id => RangeOf(view, id)).Where(r => r is > 0).Select(r => r!.Value).ToList();
        return ranges.Count > 0 ? ranges.Min() : DefaultReachMm;
    }

    /// <summary>
    /// tank-engage.ts holdsThreat: an enemy with a locked holder (HOLDER-LOCK, STATE.md D30) is held exactly when the
    /// tank is its holder, whatever the threat table says; with no holder, <paramref name="tankId"/> has a threat entry
    /// at least as high as every other one.
    /// </summary>
    /// <param name="enemy">Enemy.</param>
    /// <param name="tankId">Tank.</param>
    /// <returns>True when held.</returns>
    public static bool HoldsThreat(ActorState enemy, int tankId)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        if (enemy.HolderId is { } holderId)
        {
            return holderId == tankId;
        }
        if (!enemy.Threat.TryGetValue(tankId, out var mine))
        {
            return false;
        }
        return enemy.Threat.Values.All(v => v <= mine);
    }

    private static bool AimedAtOtherPlayer(SortedDictionary<int, ActorState> actors, ActorState enemy, int tankId)
    {
        if (enemy.TargetId is not { } tid || tid == tankId)
        {
            return false;
        }
        return actors.TryGetValue(tid, out var t) && t.Kind == ActorKind.Player;
    }

    /// <summary>tank-engage.ts pickLooseEnemy: the nearest alive enemy the tank does not hold that is aimed at another player, ties to the lowest id.</summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="tank">The tank.</param>
    /// <returns>The enemy, or null.</returns>
    public static ActorState? PickLooseEnemy(SortedDictionary<int, ActorState> actors, ActorState tank)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(tank);
        return actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle && !HoldsThreat(a, tank.Id) && AimedAtOtherPlayer(actors, a, tank.Id))
            .OrderBy(a => RoleShared.Dist(a, tank))
            .ThenBy(a => a.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// The engage target: the loose enemy (<see cref="PickLooseEnemy"/>), else the tank's current enemy target, else the
    /// priority pick. Null when there is nothing to engage.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <returns>The target, or null.</returns>
    public static ActorState? EngageTarget(BotView view, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var actors = view.State.Actors;
        var loose = PickLooseEnemy(actors, self);
        if (loose is not null)
        {
            return loose;
        }
        ActorState? current = self.TargetId is { } cid && actors.TryGetValue(cid, out var c) ? c : null;
        if (current is not null && current.Alive && current.Kind == ActorKind.Enemy)
        {
            return current;
        }
        return Priority.PickTankTarget(actors, self.Id) is { } fid && actors.TryGetValue(fid, out var f) ? f : null;
    }

    /// <summary>
    /// The out-of-reach approach move <see cref="TankEngageCommands"/> issues: a <see cref="MoveCommand"/> toward
    /// <see cref="EngageTarget"/>, issued whenever the tank is not casting and the target is beyond reach less the margin.
    /// A firing taunt does not suppress it (DOOR-100, R-DOOR-1): the taunt reaches from 3000 mm, past melee reach, so a
    /// taunt the plan drops must not leave the tank standing 2800 mm off an engaged pack. Null otherwise. This is the
    /// single origin of the approach move; the playtest runner exempts exactly this command from its leader-travel strip
    /// (R-SEED-1a, f-244d102b).
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The approach move, or null.</returns>
    public static MoveCommand? ApproachMove(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var target = EngageTarget(view, self);
        if (target is null || self.CastId is not null)
        {
            return null;
        }
        var d = RoleShared.Dist(self, target);
        if (d <= ReachMm(view, kit) - ReachMarginMm)
        {
            return null;
        }
        return Moves.MoveToward(view.State.Tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(target.Pos));
    }

    // The loose-enemy taunt: a loose enemy inside taunt range with the taunt ready.
    private static bool TauntFires(BotView view, ActorState self, EffectiveKit kit, double d)
    {
        var tauntRange = RangeOf(view, kit.TauntAbilityId) ?? DefaultReachMm;
        return PickLooseEnemy(view.State.Actors, self) is not null && !string.IsNullOrEmpty(kit.TauntAbilityId) && d <= tauntRange && RoleShared.AbilityReady(self, kit.TauntAbilityId, view.State.Tick, kit);
    }

    /// <summary>tank-engage.ts tankEngageCommands: null when there is nothing to engage.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>Commands, or null.</returns>
    public static List<Command>? TankEngageCommands(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var target = EngageTarget(view, self);
        if (target is null)
        {
            return null;
        }
        var tick = view.State.Tick;

        var output = new List<Command>();
        if (self.TargetId != target.Id)
        {
            output.Add(new TargetCommand(tick, self.Id, target.Id));
        }
        var d = RoleShared.Dist(self, target);

        if (TauntFires(view, self, kit, d))
        {
            // TauntFires is true only with a non-empty TauntAbilityId. DOOR-100 (R-DOOR-1): the taunt is off the GCD and
            // does not end the decision. A taunt the plan drops never goes on cooldown, so returning here on every tick
            // left an out-of-reach or in-reach tank with nothing but the taunt, standing on an engaged pack.
            output.Add(new CastCommand(tick, self.Id, kit.TauntAbilityId!, target.Id));
        }

        if (d > ReachMm(view, kit) - ReachMarginMm)
        {
            if (self.CastId is not null)
            {
                return output;
            }
            if (ApproachMove(view, self, kit) is { } move)
            {
                output.Add(move);
            }
            return output;
        }

        var desiredYaw = Moves.YawToward(PointMm.Of(self.Pos), PointMm.Of(target.Pos));
        if (Moves.YawChanged(self.Yaw, desiredYaw))
        {
            output.Add(new MoveCommand(tick, self.Id, new Dir2(0, 0), desiredYaw));
        }
        var rotationKit = kit with { Rotation = Fillers.ArrangeFillers(view, self, kit, TankPressure.PressureRotation(view, self, kit), target.Id) };
        if (DamageRole.PickResourceAwareRotation(rotationKit, self, tick) is { } abilityId)
        {
            output.Add(new CastCommand(tick, self.Id, abilityId, target.Id));
        }
        return output;
    }
}
