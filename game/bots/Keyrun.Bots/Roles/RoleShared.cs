using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
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
    /// Lull: at least one enemy is alive and no living enemy is engaged (every living enemy is idle). An engaged enemy
    /// is alive and not idle, whatever it is aimed at: a heal-only caster tending its own pack is still engaged, and the
    /// tank fights it (R-SEED-1a, f-244d102b). Lull is therefore decided by engagement, never by what an enemy targets.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <returns>True in a lull.</returns>
    public static bool InLull(BotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var enemies = view.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive).ToList();
        return enemies.Count > 0 && enemies.All(e => e.Idle);
    }

    /// <summary>shared.ts abilityReady (one definition, <see cref="Readiness.AbilityReady(ActorState, string, int, EffectiveKit)"/>).</summary>
    /// <param name="self">Actor.</param>
    /// <param name="abilityId">Ability.</param>
    /// <param name="tick">Current tick.</param>
    /// <param name="kit">Effective kit (its OffGcdAbilityIds skip the GCD wait).</param>
    /// <returns>True when off cooldown and off the GCD (or the ability is off the GCD).</returns>
    public static bool AbilityReady(ActorState self, string abilityId, int tick, EffectiveKit kit) => Readiness.AbilityReady(self, abilityId, tick, kit);

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
        AbilityReady(self, abilityId, tick, kit) && Affordable(self, abilityId, kit);

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
        var assignment = AssignedKick(view, self, kit);
        if (assignment is null)
        {
            return null;
        }
        // KICK-LEDGER-COOLDOWN-GAP: a layout run's ledger may name a seat that is not ready yet (its cooldown ends before
        // the cast resolves) or not in range yet (it walks in, LedgerKickApproachCommand); it presses the tick it can.
        // KICK-APPROACH-NO-LAYOUT (R-K4-3): a no-layout run (the oracle) refuses an out-of-range cast silently in the sim, so
        // it issues no cast out of range and walks in instead (LedgerKickApproachCommand). Its cooldown gap stays frozen.
        var target = view.State.Actors[assignment.TargetId];
        if (view.Layout is not null
                ? !CanPressInterrupt(view, self, interruptId, kit, target)
                : Casting.RangeMm(self, target) > InterruptRangeMm(view, interruptId))
        {
            return null;
        }
        return new CastCommand(tick, self.Id, interruptId, assignment.TargetId);
    }

    /// <summary>
    /// KICK-LEDGER-COOLDOWN-GAP (finding f-13b5c33a; the WoW kick rotation: a melee kicker walks in to reach the caster):
    /// when the ledger names <paramref name="self"/> to kick a cast but its interrupt cannot reach the caster yet, the step
    /// toward the caster along the sim's path (R-PATH-1). Null when it is not the named kicker, the kick is not open yet
    /// (BOT-KICK-TIMING), or the caster is already in range. In a no-layout run (the oracle) the step is straight at the caster
    /// (PathMoves.Toward), the same stop distance as the layout path (KICK-APPROACH-NO-LAYOUT, R-K4-3).
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">Deciding actor.</param>
    /// <param name="kit">Its kit.</param>
    /// <returns>The step, or null.</returns>
    public static MoveCommand? LedgerKickApproachCommand(BotView view, ActorState self, EffectiveKit kit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(kit);
        var interruptId = kit.InterruptAbilityId;
        if (string.IsNullOrEmpty(interruptId) || !self.Alive || AssignedKick(view, self, kit) is not { } assignment)
        {
            return null;
        }
        var target = view.State.Actors[assignment.TargetId];
        if (Casting.RangeMm(self, target) <= InterruptRangeMm(view, interruptId))
        {
            return null;
        }
        return PathMoves.Toward(view, view.State.Tick, self, target.Pos);
    }

    /// <summary>
    /// The ledger's assignment when it names <paramref name="self"/> as the kicker and the kick is open, else null. The oracle
    /// runner (no layout) keeps the frozen per-class TS ledger; a layout run uses the party's, across classes.
    /// </summary>
    private static InterruptAssignment? AssignedKick(BotView view, ActorState self, EffectiveKit kit)
    {
        var tick = view.State.Tick;
        // KICK-LEDGER-PARTY-WIDE (R-K4-5): the ledger is the party's in every setup, oracle runs included, so exactly one seat
        // is named per enemy cast across all interrupt-capable seats (Ledger.ResolvePartyInterrupt, same tiebreak as the
        // layout run). Each seat carries whether its own interrupt is off the GCD (main #370, f-80b5cf16: a gcdTicks-0
        // interrupt never waits for the GCD).
        // KICK-SEAT-ELIGIBILITY (R-K4-5, the per-class path's filters): a seat locked inside its own cast is never named, and a
        // seat whose kick would land at or after the target's cast end is never named (the cast completes first). A seat in
        // its spell-queue window (Readiness.IsCastLocked false) still counts, its landing being its own cast end.
        var priority = view.Kits.PriorityInterruptAbilityIds();
        var targetId = Ledger.SelectPriorityCastTarget(Ledger.CastingEnemies(view.State.Actors), priority);
        var targetEnds = targetId is { } tid && view.State.Actors[tid].CastEndsTick is { } ends ? ends : int.MaxValue;
        var seats = view.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Player && a.Alive && !Readiness.IsCastLocked(a, tick) && Readiness.LandingTick(a, tick) < targetEnds)
            .Select(a => (a.Id, Kit: KitOf(view, a.ClassId)))
            .Where(s => !string.IsNullOrEmpty(s.Kit.InterruptAbilityId))
            .Select(s => (s.Id, s.Kit.InterruptAbilityId!, InterruptRangeMm(view, s.Kit.InterruptAbilityId!), Readiness.IsOffGcd(s.Kit, s.Kit.InterruptAbilityId!)))
            .ToList();
        var assignment = Ledger.ResolvePartyInterrupt(view.State.Actors, seats, tick, priority, view.PullPlan);
        if (assignment is null || assignment.KickerId != self.Id)
        {
            return null;
        }
        // BOT-KICK-TIMING: in the dungeon (a layout run) the assigned kicker holds until Ledger.KickOpensTick, after a
        // human reaction and into the back half of the cast, so its bar is seen. Inert in the oracle runner (no layout).
        if (view.Layout is not null && tick < Ledger.KickOpensTick(view.State.Actors[assignment.TargetId]))
        {
            return null;
        }
        return assignment;
    }

    /// <summary>The tier-one test of <see cref="Ledger.ResolvePartyInterrupt"/>: cooldown ended, off the GCD, in range.</summary>
    private static bool CanPressInterrupt(BotView view, ActorState self, string interruptId, EffectiveKit kit, ActorState target) =>
        Readiness.CooldownReadyTick(self, interruptId) <= view.State.Tick
        && (Readiness.IsOffGcd(kit, interruptId) || self.GcdReadyTick <= view.State.Tick)
        && Casting.RangeMm(self, target) <= InterruptRangeMm(view, interruptId);

    /// <summary>The interrupt's content range, mm; unbounded when the view carries no content registry.</summary>
    /// <param name="view">Bot view.</param>
    /// <param name="abilityId">Interrupt ability.</param>
    /// <returns>Range, mm.</returns>
    private static long InterruptRangeMm(BotView view, string abilityId) =>
        view.Kits.Content is { } reg && reg.Abilities.TryGetValue(abilityId, out var def) ? def.Range : long.MaxValue;
}
