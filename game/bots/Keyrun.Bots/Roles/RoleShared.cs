using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// Port of bots/src/roles/shared.ts: helpers every role state machine shares (readiness and affordability, the kick
/// ledger command each role tries first, and the party-wide lull signal), plus the two JS number operations the
/// roles reproduce (<c>Math.hypot</c> and <c>Math.round</c>).
/// </summary>
public static class RoleShared
{
    /// <summary>
    /// JS <c>Math.hypot(x, y)</c> as V8 computes it (math.tq MathHypot): scale by the larger magnitude, Kahan-sum the
    /// squared ratios, then <c>sqrt(sum) * max</c>. Matching the algorithm, not just the value, keeps distance ties and
    /// threshold comparisons bit-identical to the frozen TS.
    /// </summary>
    /// <param name="x">First component.</param>
    /// <param name="y">Second component.</param>
    /// <returns>The hypotenuse.</returns>
    public static double JsHypot(double x, double y)
    {
        var ax = Math.Abs(x);
        var ay = Math.Abs(y);
        var max = Math.Max(ax, ay);
        if (double.IsInfinity(max))
        {
            return double.PositiveInfinity;
        }
        if (double.IsNaN(ax) || double.IsNaN(ay))
        {
            return double.NaN;
        }
        if (max == 0)
        {
            return 0;
        }
        double sum = 0;
        double compensation = 0;
        foreach (var v in new[] { ax, ay })
        {
            var n = v / max;
            var summand = (n * n) - compensation;
            var preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
        }
        return Math.Sqrt(sum) * max;
    }

    /// <summary>The planar distance between two actors, as the TS <c>Math.hypot(a.pos.x - b.pos.x, a.pos.z - b.pos.z)</c>.</summary>
    /// <param name="a">First actor.</param>
    /// <param name="b">Second actor.</param>
    /// <returns>Distance, mm.</returns>
    public static double Dist(ActorState a, ActorState b) => JsHypot(a.Pos.X - b.Pos.X, a.Pos.Z - b.Pos.Z);

    /// <summary>JS <c>Math.round</c>: the nearest integer, halves toward +infinity.</summary>
    /// <param name="x">Value.</param>
    /// <returns>Rounded value.</returns>
    public static long JsRound(double x)
    {
        var f = Math.Floor(x);
        return (long)(x - f >= 0.5 ? f + 1 : f);
    }

    /// <summary>
    /// The integer centroid the tank and healer compute: <c>Math.round(sum / n)</c> per axis (an average of integer
    /// positions is not itself guaranteed integer). <paramref name="party"/> must be non-empty.
    /// </summary>
    /// <param name="party">Actors.</param>
    /// <returns>Rounded centroid.</returns>
    public static PointMm Centroid(IReadOnlyCollection<ActorState> party)
    {
        ArgumentNullException.ThrowIfNull(party);
        double sx = 0;
        double sz = 0;
        foreach (var a in party)
        {
            sx += a.Pos.X;
            sz += a.Pos.Z;
        }
        return new PointMm(JsRound(sx / party.Count), JsRound(sz / party.Count));
    }

    /// <summary>
    /// shared.ts inLull: at least one enemy is alive and every living enemy is aimed at another enemy (a heal-only
    /// caster tending its own pack). An enemy with no target is not a lull.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <returns>True in a lull.</returns>
    public static bool InLull(BotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var actors = view.State.Actors;
        var living = actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle).ToList();
        if (living.Count == 0)
        {
            return false;
        }
        return living.All(e => e.TargetId is { } tid && actors.TryGetValue(tid, out var t) && t.Kind == ActorKind.Enemy);
    }

    /// <summary>shared.ts abilityReady (one definition, <see cref="Readiness.AbilityReady"/>).</summary>
    /// <param name="self">Actor.</param>
    /// <param name="abilityId">Ability.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>True when off cooldown and off the GCD.</returns>
    public static bool AbilityReady(ActorState self, string abilityId, int tick) => Readiness.AbilityReady(self, abilityId, tick);

    /// <summary>shared.ts affordable: <c>self.resource &gt;= (kit.costByAbility[abilityId] ?? 0)</c>.</summary>
    /// <param name="self">Actor.</param>
    /// <param name="abilityId">Ability.</param>
    /// <param name="kit">Effective kit.</param>
    /// <returns>True when the cost is covered.</returns>
    public static bool Affordable(ActorState self, string abilityId, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        return self.Resource >= Cost(kit, abilityId);
    }

    /// <summary><c>kit.costByAbility[abilityId] ?? 0</c>.</summary>
    /// <param name="kit">Effective kit.</param>
    /// <param name="abilityId">Ability.</param>
    /// <returns>Cost.</returns>
    public static long Cost(EffectiveKit kit, string abilityId)
    {
        ArgumentNullException.ThrowIfNull(kit);
        return kit.CostByAbility.TryGetValue(abilityId, out var c) ? c : 0;
    }

    /// <summary>shared.ts readyAndAffordable.</summary>
    /// <param name="self">Actor.</param>
    /// <param name="abilityId">Ability.</param>
    /// <param name="tick">Current tick.</param>
    /// <param name="kit">Effective kit.</param>
    /// <returns>True when castable and affordable now.</returns>
    public static bool ReadyAndAffordable(ActorState self, string abilityId, int tick, EffectiveKit kit) =>
        AbilityReady(self, abilityId, tick) && Affordable(self, abilityId, kit);

    /// <summary>The kit resolved for <paramref name="classId"/> on this view (TS <c>resolveKit(classId, view.classKit)</c>).</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="classId">Class id.</param>
    /// <returns>Effective kit.</returns>
    public static EffectiveKit KitOf(BotView view, string classId)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Kits.ResolveKit(classId, view.ClassKit);
    }

    /// <summary>
    /// shared.ts ledgerKickCommand: when it is <paramref name="self"/>'s turn to kick the party's priority-cast target,
    /// that cast (and nothing else this tick). Candidates are the alive players whose resolved kit uses the same
    /// interrupt id; the lowest ready actorId kicks.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The kick, or null.</returns>
    public static CastCommand? LedgerKickCommand(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var tick = view.State.Tick;
        var interruptId = kit.InterruptAbilityId;
        if (string.IsNullOrEmpty(interruptId) || !self.Alive)
        {
            return null;
        }
        var eligible = view.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Player && a.Alive)
            .Where(a => string.Equals(KitOf(view, a.ClassId).InterruptAbilityId, interruptId, StringComparison.Ordinal))
            .Select(a => a.Id)
            .ToList();
        var assignment = Ledger.ResolveInterrupt(view.State.Actors, interruptId, eligible, tick, view.Kits.PriorityInterruptAbilityIds());
        if (assignment is null || assignment.KickerId != self.Id)
        {
            return null;
        }
        return new CastCommand(tick, self.Id, interruptId, assignment.TargetId);
    }
}
