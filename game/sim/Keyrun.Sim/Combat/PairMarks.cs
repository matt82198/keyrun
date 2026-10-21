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
            var hits = new List<(int Id, long Amount)>();
            foreach (var victim in SideWithin(state, holder, rules.SpreadRadiusMm).Where(a => a.Id != holder.Id).ToList())
            {
                if (Hit(state, sourceId, victim, rules.SpreadDamage, effectDefs, emit, weighting) is { } dealt)
                {
                    hits.Add((victim.Id, dealt));
                }
            }
            EmitDetonated(state, "spread", holder.Id, hits, emit);
        }

        foreach (var (holder, sourceId) in stacks)
        {
            if (!holder.Alive)
            {
                continue;
            }
            long headcount = SideWithin(state, holder, rules.StackRadiusMm).Count;
            var hits = new List<(int Id, long Amount)>();
            if (Hit(state, sourceId, holder, IntMath.FloorDiv(rules.StackDamage, Math.Max(1, headcount)), effectDefs, emit, weighting) is { } dealt)
            {
                hits.Add((holder.Id, dealt));
            }
            EmitDetonated(state, "stack", holder.Id, hits, emit);
        }
    }

    /// <summary>
    /// The mark_applied receipt (BIBLE s9 The Pair; the demo.log proof a capture reads): one event on the tick a mark cast
    /// lands, naming the cast (<c>def.Id</c>), the kind, the caster, the actors it marked (ascending), the mark's radius
    /// and the tick it detonates (<c>landing + duration</c>, the instance's <see cref="EffectInstance.EndsTick"/>).
    /// Called once per completed cast by <c>Casting.CompleteCastOn</c>. A cast whose ability applies no mark, or whose
    /// targets took none, emits nothing. Null <paramref name="rules"/> (every oracle run) emits nothing.
    /// </summary>
    /// <param name="state">The sim state (its tick is the landing tick).</param>
    /// <param name="def">The ability that completed.</param>
    /// <param name="targets">Its resolved targets.</param>
    /// <param name="sourceId">The caster's id.</param>
    /// <param name="rules">The run's mark rules, or null.</param>
    /// <param name="emit">Event sink.</param>
    public static void EmitApplied(
        SimState state,
        AbilityDef def,
        IReadOnlyList<ActorState> targets,
        int sourceId,
        MarkRules? rules,
        Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(targets);
        if (rules is null || !MarkOf(def, rules, out var kind, out var radius, out var effectId))
        {
            return;
        }
        var marked = targets
            .Select(t => (Actor: t, Instance: t.Effects.Find(e => e.EffectId == effectId && e.SourceId == sourceId)))
            .Where(m => m.Instance is not null)
            .OrderBy(m => m.Actor.Id)
            .ToList();
        if (marked.Count == 0)
        {
            return;
        }
        emit(new SimEvent(
            state.Tick,
            "mark_applied",
            ("cast", JsValue.Str(def.Id)),
            ("kind", JsValue.Str(kind)),
            ("sourceId", JsValue.Int(sourceId)),
            ("targetIds", JsValue.Arr(marked.Select(m => JsValue.Int(m.Actor.Id)))),
            ("radius", JsValue.Int(radius)),
            ("detonatesTick", JsValue.Int(marked[0].Instance!.EndsTick))));
    }

    // Which mark an ability applies: its applyEffect op names the spread or the stack effect.
    private static bool MarkOf(AbilityDef def, MarkRules rules, out string kind, out long radius, out string effectId)
    {
        foreach (var op in def.Effects)
        {
            if (op is ApplyEffectOp a && a.EffectId == rules.SpreadEffectId)
            {
                kind = "spread";
                radius = rules.SpreadRadiusMm;
                effectId = a.EffectId;
                return true;
            }
            if (op is ApplyEffectOp b && b.EffectId == rules.StackEffectId)
            {
                kind = "stack";
                radius = rules.StackRadiusMm;
                effectId = b.EffectId;
                return true;
            }
        }
        kind = string.Empty;
        radius = 0;
        effectId = string.Empty;
        return false;
    }

    // The mark_detonated receipt: once per resolved mark, on the tick it runs out. hits lists each actor the detonation
    // dealt damage to (in resolution order: ascending id), with the health it took; empty when nobody was hit.
    private static void EmitDetonated(SimState state, string kind, int markedId, List<(int Id, long Amount)> hits, Action<SimEvent> emit)
    {
        emit(new SimEvent(
            state.Tick,
            "mark_detonated",
            ("kind", JsValue.Str(kind)),
            ("markedId", JsValue.Int(markedId)),
            ("hitIds", JsValue.Arr(hits.Select(h => JsValue.Int(h.Id)))),
            ("damage", JsValue.Arr(hits.Select(h => JsValue.Obj(
                ("actorId", JsValue.Int(h.Id)),
                ("amount", JsValue.Int(h.Amount))))))));
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
        var rings = new List<MarkRing>();
        if (rules is null)
        {
            return rings;
        }
        var spreadStrokes = SpreadStrokes(rules.SpreadRadiusMm);
        var stackStrokes = new[]
        {
            new MarkStroke(rules.StackRadiusMm, 0, 1000),
            new MarkStroke(IntMath.FloorDiv(rules.StackRadiusMm, 2), 0, 1000),
        };
        foreach (var actor in state.Actors.Values)
        {
            if (!actor.Alive)
            {
                continue;
            }
            if (NextEnd(actor, rules.SpreadEffectId, state.Tick) is int spreadEnds)
            {
                rings.Add(new MarkRing(actor.Id, MarkKind.Spread, rules.SpreadRadiusMm, spreadEnds, spreadStrokes));
            }
            if (NextEnd(actor, rules.StackEffectId, state.Tick) is int stackEnds)
            {
                rings.Add(new MarkRing(actor.Id, MarkKind.Stack, rules.StackRadiusMm, stackEnds, stackStrokes));
            }
        }
        return rings;
    }

    // The earliest end tick among the actor's live instances of the effect (EndsTick >= now), or null for none.
    private static int? NextEnd(ActorState actor, string effectId, int now)
    {
        int? end = null;
        foreach (var e in actor.Effects)
        {
            if (e.EffectId == effectId && e.EndsTick >= now && (end is null || e.EndsTick < end))
            {
                end = e.EndsTick;
            }
        }
        return end;
    }

    // SpreadDashes equal dashes, each half its 1/SpreadDashes slot, the first starting at 12 o'clock.
    private static IReadOnlyList<MarkStroke> SpreadStrokes(long radiusMm)
    {
        // 8 dashes: a 125-permille slot each, the dash 62 of it (no signed division in the sim).
        const int slot = 125;
        const int dash = 62;
        var strokes = new List<MarkStroke>(SpreadDashes);
        for (var i = 0; i < SpreadDashes; i++)
        {
            strokes.Add(new MarkStroke(radiusMm, i * slot, (i * slot) + dash));
        }
        return strokes;
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

    // Returns the health the hit took, or null when no hit was made (nothing to deal, or the target already dead).
    private static long? Hit(
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
            return null;
        }
        state.Actors.TryGetValue(sourceId, out var source);
        return Mitigation.DealDamage(state.Tick, source, target, amount, effectDefs, emit, sourceId, weighting).HealthDamage;
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
