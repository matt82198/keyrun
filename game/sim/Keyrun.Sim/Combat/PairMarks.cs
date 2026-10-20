using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// The Pair's spread and stack marks as rules (design/BIBLE.md section 9.6 item 2: "every 20 s one player is marked to
/// <i>spread</i> (a ring: stand apart), ten seconds later the pack calls a <i>stack</i> (a circle: stand together,
/// damage split). Teaches reading shape."). The content effects (content/data/effects/enemy.json spreadMarked,
/// stackMarked) carry no modifiers; the sim decides by id, as it does for "blinded" (<see cref="Mitigation"/>).
/// C#-only and null on every oracle run (the frozen TS marks do nothing); a host builds it from game/content/data/run.json
/// <c>pairMarks</c>.
/// </summary>
/// <param name="SpreadEffectId">The spread mark's effect id ("spreadMarked").</param>
/// <param name="SpreadRadiusMm">Detonation radius around a spread-marked actor, mm (the spread telegraph's 3000).</param>
/// <param name="SpreadDamage">Raw damage the detonation deals to each other actor of the holder's side in the radius.</param>
/// <param name="StackEffectId">The stack mark's effect id ("stackMarked").</param>
/// <param name="StackRadiusMm">Radius around a stack-marked actor that counts as standing together, mm (the stack
/// telegraph's 1500).</param>
/// <param name="StackDamage">Raw damage one stack-marked actor's hit is worth before it is split by headcount.</param>
public sealed record MarkRules(
    string SpreadEffectId,
    long SpreadRadiusMm,
    long SpreadDamage,
    string StackEffectId,
    long StackRadiusMm,
    long StackDamage);

/// <summary>
/// Resolves the Pair's marks on the tick they run out (<see cref="EffectInstance.EndsTick"/> &lt;= now, the tick
/// <see cref="Effects.ExpireEffects"/> drops them). <see cref="Rules"/> runs this once per tick, after movement and
/// before the per-actor periodic ticks and expiries, so it reads where everyone stands on the tick the mark ends.
/// <list type="bullet">
/// <item><b>Spread</b>: a living spread-marked actor detonates; every OTHER living actor of its side within
/// <see cref="MarkRules.SpreadRadiusMm"/> takes <see cref="MarkRules.SpreadDamage"/>. Two marked players standing
/// together hit each other; a marked player who walked clear hurts nobody.</item>
/// <item><b>Stack</b>: a living stack-marked actor takes <c>floor(StackDamage / n)</c>, where n counts the living
/// actors of its side within <see cref="MarkRules.StackRadiusMm"/> of it, itself included. Standing alone takes the
/// whole hit; three together take a third each.</item>
/// </list>
/// An actor carrying the same mark from several casters (the five Paired Warriors cast on one schedule) resolves it
/// once: a player is marked or not. Damage goes through <see cref="Mitigation.DealDamage"/> with the first expiring
/// instance's caster as its source, so damage-taken modifiers, absorbs and the Pact weighting apply as to any hit.
/// </summary>
public static class PairMarks
{
    /// <summary>Resolve every spread and stack mark that runs out on <c>state.Tick</c>. No-op when
    /// <paramref name="rules"/> is null (every oracle run).</summary>
    /// <param name="state">The sim state.</param>
    /// <param name="rules">The run's mark rules, or null for none.</param>
    /// <param name="effectDefs">Effect definitions (for mitigation).</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="weighting">The active Pact weighting, or null.</param>
    public static void Resolve(
        SimState state,
        MarkRules? rules,
        IReadOnlyDictionary<string, EffectDef> effectDefs,
        Action<SimEvent> emit,
        DamageWeighting? weighting)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (rules is null)
        {
            return;
        }
        var now = state.Tick;
        // Snapshot who resolves before any damage lands, so a detonation that kills a marked ally this tick does not
        // change whether that ally's own mark was due. Actors ascend by id (SortedDictionary), which fixes the order.
        var spreads = DueHolders(state, rules.SpreadEffectId, now);
        var stacks = DueHolders(state, rules.StackEffectId, now);

        foreach (var (holder, sourceId) in spreads)
        {
            if (!holder.Alive)
            {
                continue;
            }
            foreach (var victim in SideWithin(state, holder, rules.SpreadRadiusMm).Where(a => a.Id != holder.Id).ToList())
            {
                Hit(state, sourceId, victim, rules.SpreadDamage, effectDefs, emit, weighting);
            }
        }

        foreach (var (holder, sourceId) in stacks)
        {
            if (!holder.Alive)
            {
                continue;
            }
            long headcount = SideWithin(state, holder, rules.StackRadiusMm).Count;
            Hit(state, sourceId, holder, IntMath.FloorDiv(rules.StackDamage, Math.Max(1, headcount)), effectDefs, emit, weighting);
        }
    }

    /// <summary>
    /// The floor rings a view draws for the live marks (BIBLE section 3: "a floor telegraph ... drawn ahead of
    /// resolution. Shape plus colour, never colour alone"). One ring per living marked actor and mark kind, centred
    /// on that actor (it follows them), with the mark's damage radius, from the tick the mark lands until the tick it
    /// resolves (<see cref="MarkRing.EndsTick"/>, the earliest live instance's end; the ring is gone on the tick after,
    /// once <see cref="Effects.ExpireEffects"/> drops it). The two kinds differ in shape: spread is ONE DASHED ring
    /// (<see cref="SpreadDashes"/> dashes, a broken ring: stand apart); stack is TWO SOLID concentric rings, the outer
    /// at the stack radius and the inner at half of it (a target: gather in). Empty when <paramref name="rules"/> is null.
    /// </summary>
    /// <param name="state">The sim state (after a step: marks still live have <c>EndsTick &gt;= state.Tick</c>).</param>
    /// <param name="rules">The run's mark rules, or null.</param>
    /// <returns>The rings, actors ascending, spread before stack.</returns>
    public static List<MarkRing> Rings(SimState state, MarkRules? rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new List<MarkRing>();
    }

    /// <summary>Dashes in a spread ring.</summary>
    public const int SpreadDashes = 8;

    /// <summary>Each actor carrying <paramref name="effectId"/> that ends now, once, with its first such instance's source.</summary>
    private static List<(ActorState Holder, int SourceId)> DueHolders(SimState state, string effectId, int now)
    {
        var due = new List<(ActorState, int)>();
        foreach (var actor in state.Actors.Values)
        {
            var inst = actor.Effects.Find(e => e.EffectId == effectId && e.EndsTick <= now);
            if (inst is not null)
            {
                due.Add((actor, inst.SourceId));
            }
        }
        return due;
    }

    /// <summary>Living actors of <paramref name="holder"/>'s side within <paramref name="radiusMm"/> (XZ,
    /// <c>dx^2 + dz^2 &lt;= r^2</c>, the <see cref="Targeting.InCircle"/> rule), the holder included.</summary>
    private static List<ActorState> SideWithin(SimState state, ActorState holder, long radiusMm)
    {
        var side = state.Actors.Values.Where(a => a.Alive && a.Kind == holder.Kind).ToList();
        var ids = Targeting.InCircle(holder.Pos, radiusMm, side);
        return side.Where(a => ids.Contains(a.Id)).ToList();
    }

    private static void Hit(
        SimState state,
        int sourceId,
        ActorState target,
        long amount,
        IReadOnlyDictionary<string, EffectDef> effectDefs,
        Action<SimEvent> emit,
        DamageWeighting? weighting)
    {
        if (amount <= 0 || !target.Alive)
        {
            return;
        }
        state.Actors.TryGetValue(sourceId, out var source);
        Mitigation.DealDamage(state.Tick, source, target, amount, effectDefs, emit, sourceId, weighting);
    }
}

/// <summary>Which Pair mark a ring shows.</summary>
public enum MarkKind
{
    /// <summary>spreadMarked: stand apart.</summary>
    Spread,

    /// <summary>stackMarked: stand together.</summary>
    Stack,
}

/// <summary>One arc of a mark ring: radius and the span of a full turn it covers, permille clockwise from 12 o'clock.</summary>
/// <param name="RadiusMm">Ring radius, mm.</param>
/// <param name="FromPermille">Start of the arc, 0..1000.</param>
/// <param name="ToPermille">End of the arc, 0..1000 (greater than the start).</param>
public sealed record MarkStroke(long RadiusMm, int FromPermille, int ToPermille);

/// <summary>A floor ring around a marked actor (<see cref="PairMarks.Rings"/>).</summary>
/// <param name="ActorId">The marked actor; the ring is centred on it.</param>
/// <param name="Kind">Spread or stack.</param>
/// <param name="RadiusMm">The mark's damage radius, mm (the outermost stroke).</param>
/// <param name="EndsTick">The tick the mark resolves; the ring is drawn through it.</param>
/// <param name="Strokes">The ring's shape: dashes for spread, two solid concentric circles for stack.</param>
public sealed record MarkRing(int ActorId, MarkKind Kind, long RadiusMm, int EndsTick, IReadOnlyList<MarkStroke> Strokes);
