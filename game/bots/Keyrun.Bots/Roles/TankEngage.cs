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

    /// <summary>R-AOE-1: the tank spreads its filler only across a small pack; a big pack keeps the tank on its target.</summary>
    public const int TankSpreadMaxCluster = 3;

    private static long? RangeOf(BotView view, string? abilityId)
    {
        if (abilityId is null || view.Kits.Content is not { } reg || !reg.Abilities.TryGetValue(abilityId, out var def))
        {
            return null;
        }
        return def.Range;
    }

    /// <summary>The content definition of an ability (its damage effects and cooldown), or null when unknown.</summary>
    private static Keyrun.Content.AbilityDef? AbilityOf(BotView view, string? abilityId) =>
        abilityId is not null && view.Kits.Content is { } reg && reg.Abilities.TryGetValue(abilityId, out var def) ? def : null;

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
        // CHASE-HOLDER (f-2f6982e9): a locked enemy goes at its holder (HOLDER-LOCK, ScriptTarget), so the holder is who it is
        // aimed at. TargetId is only the sim's default (FixupTargeting parks every untargeted enemy on the tank), so a pack
        // the human body-pulled ahead of the Warden still read TargetId == tank, was never loose, and was never taunted.
        if ((enemy.HolderId ?? enemy.TargetId) is not { } tid || tid == tankId)
        {
            return false;
        }
        return actors.TryGetValue(tid, out var t) && t.Kind == ActorKind.Player;
    }

    /// <summary>True when <paramref name="enemy"/> stands within melee reach of the player it is aimed at (its holder, else its target): it is hurting that seat now.</summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="enemy">The enemy.</param>
    /// <returns>True when it is in melee reach of its seat.</returns>
    public static bool HurtsItsSeat(SortedDictionary<int, ActorState> actors, ActorState enemy) =>
        (enemy.HolderId ?? enemy.TargetId) is { } tid && actors.TryGetValue(tid, out var seat) && RoleShared.Dist(enemy, seat) <= DefaultReachMm;

    /// <summary>tank-engage.ts pickLooseEnemy: the nearest alive enemy the tank does not hold that is aimed at another player, ties to the lowest id.</summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="tank">The tank.</param>
    /// <returns>The enemy, or null.</returns>
    public static ActorState? PickLooseEnemy(SortedDictionary<int, ActorState> actors, ActorState tank) =>
        LooseEnemies(actors, tank).FirstOrDefault();

    /// <summary>The loose enemies in pickup order (R-TAUNT-4c seat HP, then R-TAUNT-3 row 1, distance, id); see <see cref="PickLooseEnemy"/>.</summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="tank">The tank.</param>
    /// <returns>Loose enemies, best first.</returns>
    public static IEnumerable<ActorState> LooseEnemies(SortedDictionary<int, ActorState> actors, ActorState tank)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(tank);
        return actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle && !HoldsThreat(a, tank.Id) && AimedAtOtherPlayer(actors, a, tank.Id))
            // R-TAUNT-3 row 1 (design/THREAT-MODEL.md section 4; LOOSE-PICKUP): the enemy hurting its seat now (in melee reach of
            // the seat it is aimed at) comes before a nearer one standing off from its holder and hurting nobody, so where an idle
            // caster stands (a wander, a held post) never decides which enemy the Taunt goes to while a seat dies.
            // R-TAUNT-4c: the enemy aimed at the lowest-HP non-tank seat goes first, so the opening taunt lands on it.
            .OrderBy(a => SeatHealthKey(actors, a, tank.Id))
            .ThenBy(a => HurtsItsSeat(actors, a) ? 0 : 1)
            .ThenBy(a => RoleShared.Dist(a, tank))
            .ThenBy(a => a.Id);
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
        // R-PATH-1: the tank closes on the pack along its computed path (straight when in sight).
        return PathMoves.Toward(view, view.State.Tick, self, target.Pos);
    }

    /// <summary>
    /// R-TAUNT-4c: the HP of the living non-tank player seat an enemy is aimed at (its holder, else its target), or
    /// <see cref="long.MaxValue"/> when it is aimed at no such seat. The lowest key goes first.
    /// </summary>
    private static long SeatHealthKey(SortedDictionary<int, ActorState> actors, ActorState enemy, int tankId) =>
        (enemy.HolderId ?? enemy.TargetId) is { } tid && tid != tankId && actors.TryGetValue(tid, out var seat) && seat.Kind == ActorKind.Player && seat.Alive
            ? seat.Health
            : long.MaxValue;

    /// <summary>
    /// R-TAUNT-4a: the damage the party commits to <paramref name="target"/> over <paramref name="windowTicks"/>: each living
    /// player aimed at it contributes its kit's best damage per tick (the largest summed DamageOp over its cooldown or GCD) times the window.
    /// The bot has no damage log, so this is the kit-derived estimate, not an observed rate.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="actors">Actor table.</param>
    /// <param name="target">The enemy the taunt would be cast on.</param>
    /// <param name="windowTicks">The taunt cooldown window in ticks.</param>
    /// <returns>Committed damage over the window.</returns>
    public static double CommittedDamage(BotView view, SortedDictionary<int, ActorState> actors, ActorState target, long windowTicks)
    {
        double total = 0;
        foreach (var seat in actors.Values)
        {
            if (seat.Kind != ActorKind.Player || !seat.Alive || seat.TargetId != target.Id)
            {
                continue;
            }
            var kit = view.Kits.ResolveKit(seat.ClassId, ClassKitView.Empty);
            double bestPerTick = 0;
            foreach (var abilityId in kit.Rotation)
            {
                if (AbilityOf(view, abilityId) is not { } def)
                {
                    continue;
                }
                long amount = def.Effects.OfType<Keyrun.Content.DamageOp>().Sum(e => (long)e.Amount);
                if (amount <= 0)
                {
                    continue;
                }
                bestPerTick = Math.Max(bestPerTick, (double)amount / Math.Max(1, Math.Max(def.CooldownTicks, def.GcdTicks)));
            }
            total += bestPerTick * windowTicks;
        }
        return total;
    }

    /// <summary>
    /// R-PACK-TAUNT-FIRST (held-pack approach): the taunt the tank casts this tick on a loose enemy inside taunt range, or null
    /// (the same choice <see cref="TankEngageCommands"/> makes). The taunt reaches past melee, so a tank walking at a held
    /// pack takes it from range instead of walking in while the pack's Taunt sits ready.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The tank.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The taunt cast, or null.</returns>
    public static CastCommand? TauntCommand(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        return TauntTarget(view, self, kit) is { } target ? new CastCommand(view.State.Tick, self.Id, kit.TauntAbilityId!, target.Id) : null;
    }

    /// <summary>
    /// The loose-enemy taunt target: the first in-range enemy in <see cref="LooseEnemies"/> order (seat HP, hurting its seat,
    /// distance, id). R-TAUNT-4a (doomed-enemy ordering) was removed by R-TAUNT-5 ship: it broke BotTankTakesHolderTests seed 3
    /// and the tutorial Cinder run. Null when the taunt is not ready, has no ability id, or no loose enemy is in taunt range.
    /// </summary>
    internal static ActorState? TauntTarget(BotView view, ActorState self, EffectiveKit kit)
    {
        if (string.IsNullOrEmpty(kit.TauntAbilityId) || !RoleShared.AbilityReady(self, kit.TauntAbilityId, view.State.Tick, kit))
        {
            return null;
        }
        var tauntRange = RangeOf(view, kit.TauntAbilityId) ?? DefaultReachMm;
        var inRange = LooseEnemies(view.State.Actors, self).Where(e => RoleShared.Dist(self, e) <= tauntRange).ToList();
        return inRange.FirstOrDefault();
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

        if (TauntTarget(view, self, kit) is { } tauntAt)
        {
            // TauntTarget is non-null only with a non-empty TauntAbilityId. DOOR-100 (R-DOOR-1): the taunt is off the GCD and
            // does not end the decision. A taunt the plan drops never goes on cooldown, so returning here on every tick
            // left an out-of-reach or in-reach tank with nothing but the taunt, standing on an engaged pack.
            output.Add(new CastCommand(tick, self.Id, kit.TauntAbilityId!, tauntAt.Id));
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
        var rotationKit = kit with { Rotation = AoeGate.Filter(view, self, Fillers.ArrangeFillers(view, self, kit, TankPressure.PressureRotation(view, self, kit), target.Id), target.Id) };
        if (DamageRole.PickResourceAwareRotation(rotationKit, self, tick) is { } abilityId)
        {
            // R-AOE-1: the filler (Strike) goes to the healthiest of the cluster; every other press stays on the tank's target.
            if (rotationKit.Rotation.Count > 0 && view.Kits.Content is { } reg && reg.Abilities.TryGetValue(abilityId, out var pressed)
                && AoeGate.IsFiller(pressed))
            {
                output.AddRange(AoeGate.PressCommands(view, self, abilityId, target.Id, TankSpreadMaxCluster));
            }
            else
            {
                output.Add(new CastCommand(tick, self.Id, abilityId, target.Id));
            }
        }
        return output;
    }
}
