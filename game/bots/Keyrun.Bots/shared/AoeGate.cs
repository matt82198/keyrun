using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Core;
using S = Keyrun.Sim.Combat;

namespace Keyrun.Bots.Shared;

/// <summary>
/// R-AOE-1 (findings f-c8ed3a62, f-47b1f102): the bot's AoE rotation rule. A shaped AoE damage rotational (a circle, cone
/// or line ability that damages) is the correct button whenever two or more living enemies stand inside its shape, and
/// single-target stays correct at one. The rotation priority list therefore drops a shaped AoE while fewer than
/// <see cref="MinEnemies"/> enemies stand in the shape it would resolve on; the single-target buttons and the filler fill
/// that global, and the AoE returns to the list the moment the pack closes up. The shape is read with the sim's own
/// geometry (<see cref="S.Targeting"/>), centred as the sim centres it (<see cref="S.Targeting.AreaBurstTag"/> circles
/// burst at the target, every other circle at the caster, cones and lines ahead of the caster).
/// </summary>
public static class AoeGate
{
    /// <summary>The enemies a shaped AoE needs in its shape before it is the correct button.</summary>
    public const int MinEnemies = 2;

    /// <summary>True when <paramref name="def"/> is a circle, cone or line ability that deals damage (base or any verse variant).</summary>
    /// <param name="def">The ability.</param>
    /// <returns>Whether it is a shaped AoE damage ability.</returns>
    public static bool IsShapedAoeDamage(AbilityDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        return def.Shape.Kind is ShapeKind.Circle or ShapeKind.Cone or ShapeKind.Line
            && (def.Effects.Any(op => op is DamageOp) || (def.Variants?.Values.Any(ops => ops.Any(op => op is DamageOp)) ?? false));
    }

    /// <summary>
    /// How many living, engaged enemies <paramref name="def"/> would hit if <paramref name="self"/> cast it at
    /// <paramref name="targetId"/> now. Idle and dormant enemies are not counted: a bot never counts a sleeping pack
    /// toward its own button.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The caster.</param>
    /// <param name="def">The shaped AoE ability.</param>
    /// <param name="targetId">The cast's target, or null.</param>
    /// <returns>The enemy count in the shape.</returns>
    public static int EnemiesInShape(BotView view, ActorState self, AbilityDef def, int? targetId)
    {
        ArgumentNullException.ThrowIfNull(view);
        var state = view.State;
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(def);
        var enemies = state.Actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle && !a.Dormant)
            .ToList();
        var shape = def.Shape;
        switch (shape.Kind)
        {
            case ShapeKind.Cone:
                return S.Targeting.InCone(self, shape.Angle1024 ?? 0, shape.Radius ?? 0, enemies).Count;
            case ShapeKind.Line:
                return S.Targeting.InLine(self, shape.Length ?? 0, shape.Radius ?? 0, enemies).Count;
            case ShapeKind.Circle:
                var centre = self.Pos;
                if (def.Tags is { } tags && tags.Contains(S.Targeting.AreaBurstTag)
                    && targetId is { } tid
                    && state.Actors.TryGetValue(tid, out var t) && t.Alive && t.Kind == ActorKind.Enemy)
                {
                    centre = t.Pos;
                }
                return S.Targeting.InCircle(centre, shape.Radius ?? 0, enemies).Count;
            default:
                return 0;
        }
    }

    /// <summary>
    /// The rotation without the shaped AoE damage abilities that fewer than <see cref="MinEnemies"/> enemies stand in.
    /// Unchanged with no content registry; non-AoE entries are never touched.
    /// </summary>
    /// <param name="view">Bot view (its registry).</param>
    /// <param name="self">The caster.</param>
    /// <param name="rotation">The rotation in priority order.</param>
    /// <param name="targetId">The cast's target, or null.</param>
    /// <returns>The rotation to pick from.</returns>
    public static IReadOnlyList<string> Filter(BotView view, ActorState self, IReadOnlyList<string> rotation, int? targetId)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(rotation);
        if (view.Kits.Content is not { } reg)
        {
            return rotation;
        }
        return rotation
            .Where(id => !reg.Abilities.TryGetValue(id, out var def)
                || !IsShapedAoeDamage(def)
                // R-AOE-2: a spammable Payoff builder (Guard Break) is also the button that closes a Window for a Combo, so it
                // stays in the rotation at one enemy; dropping it left the boss fight with no Payoff presses at all.
                || (def.CooldownTicks == 0 && KitPriority.HasTag(def, KitPriority.PayoffTag))
                || EnemiesInShape(view, self, def, targetId) >= MinEnemies)
            .ToList();
    }

    /// <summary>True for the rotation's filler: no cooldown and no cost, always ready (the button every global falls back to).</summary>
    /// <param name="def">The ability.</param>
    /// <returns>Whether it is a filler.</returns>
    public static bool IsFiller(AbilityDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        return def.CooldownTicks == 0 && def.Cost.Amount == 0 && !IsShapedAoeDamage(def);
    }

    /// <summary>Enemies within this distance of the current target are one cluster (a pack standing together), mm.</summary>
    public const long ClusterMm = 3000;

    /// <summary>A cluster-mate must out-health the current target by at least this much to take the single-target press, hp.</summary>
    public const long SpreadMarginHp = 250;

    /// <summary>
    /// R-AOE-1 "kill most of them at the same time": the enemy a single-target damage press should land on. Shaped AoE
    /// hits the whole cluster evenly, so the single-target buttons go to the healthiest enemy of the cluster (within
    /// <see cref="ClusterMm"/> of the current target and the ability's range), keeping the pack's health level and its
    /// deaths together. The current target keeps the press unless a cluster-mate has <see cref="SpreadMarginHp"/> more
    /// health; abilities that are not single-target damage never move.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The caster.</param>
    /// <param name="def">The ability about to be pressed.</param>
    /// <param name="targetId">The current target, or null.</param>
    /// <param name="maxCluster">A cluster bigger than this keeps the current target (a big pack is not spread over).</param>
    /// <returns>The target to press at (the current target when nothing better).</returns>
    public static int? SpreadTarget(BotView view, ActorState self, AbilityDef def, int? targetId, int maxCluster = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(def);
        if (def.Shape.Kind != ShapeKind.Target || !def.Effects.Any(op => op is DamageOp)
            || targetId is not { } tid || !view.State.Actors.TryGetValue(tid, out var current)
            || current.Kind != ActorKind.Enemy || !current.Alive)
        {
            return targetId;
        }
        var cluster = view.State.Actors.Values.Count(e => e.Kind == ActorKind.Enemy && e.Alive && !e.Idle && !e.Dormant && Dist(e, current) <= ClusterMm);
        if (cluster > maxCluster)
        {
            return targetId;
        }
        var best = current;
        foreach (var e in view.State.Actors.Values)
        {
            if (e.Kind != ActorKind.Enemy || !e.Alive || e.Idle || e.Dormant || e.Id == current.Id
                || Dist(e, current) > ClusterMm || Dist(e, self) > def.Range)
            {
                continue;
            }
            if (e.Health - best.Health >= SpreadMarginHp || (best.Id != current.Id && e.Health > best.Health))
            {
                best = e;
            }
        }
        return best.Id;
    }

    private static long Dist(ActorState a, ActorState b)
    {
        var dx = a.Pos.X - b.Pos.X;
        var dz = a.Pos.Z - b.Pos.Z;
        return S.Movement.Isqrt((dx * dx) + (dz * dz));
    }

    /// <summary>
    /// The commands for pressing <paramref name="abilityId"/> at <paramref name="targetId"/>: the cast, preceded by a
    /// retarget when <see cref="SpreadTarget"/> moves a single-target damage press to the cluster's healthiest enemy.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The caster.</param>
    /// <param name="abilityId">The ability to press.</param>
    /// <param name="targetId">The current target, or null.</param>
    /// <param name="maxCluster">A cluster bigger than this keeps the current target (a big pack is not spread over).</param>
    /// <returns>The commands, in order.</returns>
    public static IReadOnlyList<Command> PressCommands(BotView view, ActorState self, string abilityId, int? targetId, int maxCluster = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        var tick = view.State.Tick;
        var aimed = view.Kits.Content is { } reg && reg.Abilities.TryGetValue(abilityId, out var def)
            ? SpreadTarget(view, self, def, targetId, maxCluster)
            : targetId;
        // The cast resolves on the cluster-mate; the seat's own target is put back in the same tick (an instant press has already
        // landed, a cast under way keeps the target it started on, Casting.SetTarget), so assist, holder and threat logic that
        // read the seat's target see no change.
        return aimed != targetId && aimed is not null && targetId is not null
            ? new Command[] { new CastCommand(tick, self.Id, abilityId, aimed), new TargetCommand(tick, self.Id, targetId) }
            : new Command[] { new CastCommand(tick, self.Id, abilityId, targetId) };
    }

    /// <summary>
    /// The candidate a shaped AoE should be cast at: the one with the most enemies in its shape, the first listed on a
    /// tie (so a preferred candidate listed first keeps its place). Null when there are no candidates.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The caster.</param>
    /// <param name="def">The AoE ability.</param>
    /// <param name="candidates">Candidate targets, preferred first.</param>
    /// <returns>The best target, or null.</returns>
    public static ActorState? ClusterTarget(BotView view, ActorState self, AbilityDef def, IReadOnlyList<ActorState> candidates)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(candidates);
        ActorState? best = null;
        var bestCount = -1;
        foreach (var c in candidates)
        {
            var n = EnemiesInShape(view, self, def, c.Id);
            if (n > bestCount)
            {
                best = c;
                bestCount = n;
            }
        }
        return best;
    }
}
