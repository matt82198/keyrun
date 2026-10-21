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
        var tick = view.State.Tick;
        var actors = view.State.Actors;
        var loose = PickLooseEnemy(actors, self);
        ActorState? current = self.TargetId is { } cid && actors.TryGetValue(cid, out var c) ? c : null;
        var fallbackId = Priority.PickTankTarget(actors, self.Id);
        ActorState? target = loose;
        if (target is null)
        {
            if (current is not null && current.Alive && current.Kind == ActorKind.Enemy)
            {
                target = current;
            }
            else if (fallbackId is { } fid && actors.TryGetValue(fid, out var f))
            {
                target = f;
            }
        }
        if (target is null)
        {
            return null;
        }

        var output = new List<Command>();
        if (self.TargetId != target.Id)
        {
            output.Add(new TargetCommand(tick, self.Id, target.Id));
        }
        var d = RoleShared.Dist(self, target);

        var tauntRange = RangeOf(view, kit.TauntAbilityId) ?? DefaultReachMm;
        if (loose is not null && !string.IsNullOrEmpty(kit.TauntAbilityId) && d <= tauntRange && RoleShared.AbilityReady(self, kit.TauntAbilityId, tick, kit))
        {
            output.Add(new CastCommand(tick, self.Id, kit.TauntAbilityId, target.Id));
            return output;
        }

        if (d > ReachMm(view, kit) - ReachMarginMm)
        {
            if (self.CastId is not null)
            {
                return output;
            }
            if (Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(target.Pos)) is { } move)
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
        var rotationKit = kit with { Rotation = TankPressure.PressureRotation(view, self, kit) };
        if (DamageRole.PickResourceAwareRotation(rotationKit, self, tick) is { } abilityId)
        {
            output.Add(new CastCommand(tick, self.Id, abilityId, target.Id));
        }
        return output;
    }
}
