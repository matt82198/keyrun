using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// What Windows and Payoffs resolve against (sim/combat/windows.ts:46-60 WindowsContext / createWindowsContext),
/// plus the step's Clause hooks. Omitted tables are empty; an omitted registry is a detached one.
/// </summary>
public sealed class WindowsContext
{
    /// <summary>Builds a context.</summary>
    /// <param name="effects">Effect definitions.</param>
    /// <param name="combos">Burst ComboTable, or null for an empty one.</param>
    /// <param name="fieldCombos">Field ComboTable, or null for an empty one.</param>
    /// <param name="floor">Floor patch registry, or null for a detached one.</param>
    /// <param name="hooks">Clause hooks, or null for no Clause.</param>
    public WindowsContext(
        IReadOnlyDictionary<string, EffectDef> effects,
        ComboTable? combos = null,
        ComboTable? fieldCombos = null,
        FloorWindowRegistry? floor = null,
        ClauseHooks? hooks = null)
    {
        Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        Combos = combos ?? new ComboTable();
        FieldCombos = fieldCombos ?? new ComboTable();
        Floor = floor ?? new FloorWindowRegistry();
        Hooks = hooks ?? new ClauseHooks(null);
    }

    /// <summary>Effect definitions.</summary>
    public IReadOnlyDictionary<string, EffectDef> Effects { get; }

    /// <summary>Burst combos (target/self and Lock Windows).</summary>
    public ComboTable Combos { get; }

    /// <summary>Field combos (floor Windows).</summary>
    public ComboTable FieldCombos { get; }

    /// <summary>The floor patch registry.</summary>
    public FloorWindowRegistry Floor { get; }

    /// <summary>The step's Clause hooks.</summary>
    public ClauseHooks Hooks { get; }

    /// <summary>
    /// Class id to its Heat/Overheat rule, or null for none (every oracle run; <see cref="RulesConfig.Overheat"/>).
    /// A burst Combo's removeEffect of the Payoff actor's own Chill clears the Payoff actor (Flashfire, f-9efbee75).
    /// </summary>
    public IReadOnlyDictionary<string, OverheatRule>? Overheat { get; init; }
}

/// <summary>
/// Port of sim/combat/windows.ts: opening Windows (on a target, on the caster, or as a floor patch), Window and
/// floor-patch expiry with the Lock's unpaid counter and enrage, and the entry point rules call after each cast
/// completes. Payoff resolution is <see cref="Payoff"/>; the shared state encoding is <see cref="WindowState"/>.
/// </summary>
public static class Windows
{
    /// <summary>
    /// windows.ts:62-108 openWindow. On-target: onto the caster (self) or <paramref name="target"/> (none: no-op) as a
    /// synthetic instance, emitting "window_opened" with targetId. Floor: a patch at the caster's position keyed by
    /// windowId (replacing an open one with the same id), emitting "window_opened" with its telegraph.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="setupActor">The caster.</param>
    /// <param name="op">The window op.</param>
    /// <param name="floor">Floor registry.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="target">The ability's first resolved target, or null.</param>
    public static void OpenWindow(SimState state, ActorState setupActor, WindowOp op, FloorWindowRegistry floor, Action<SimEvent> emit, ActorState? target)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(floor);
        var endsTick = state.Tick + op.DurationTicks;
        var alternate = op.Alternate == true;
        if (op.OnTarget)
        {
            var dest = op.Self == true ? setupActor : target;
            if (dest is null)
            {
                return;
            }
            Effects.PushRawEffectInstance(dest, new EffectInstance
            {
                EffectId = WindowState.WindowEffectId(op.WindowId, op.SetupClass, alternate),
                SourceId = setupActor.Id,
                Stacks = 1,
                EndsTick = endsTick,
                NextPeriodicTick = null,
            });
            emit(new SimEvent(
                state.Tick,
                "window_opened",
                ("windowId", JsValue.Str(op.WindowId)),
                ("onTarget", JsValue.Bool(true)),
                ("setupActorId", JsValue.Int(setupActor.Id)),
                ("setupClass", JsValue.Str(op.SetupClass)),
                ("targetId", JsValue.Int(dest.Id)),
                ("endsTick", JsValue.Int(endsTick))));
            return;
        }
        var patch = WindowState.MakeFloorWindow(op.WindowId, op.SetupClass, setupActor.Id, setupActor.Pos, endsTick, op.RadiusMm);
        floor.Patches[op.WindowId] = patch;
        emit(new SimEvent(
            state.Tick,
            "window_opened",
            ("windowId", JsValue.Str(op.WindowId)),
            ("onTarget", JsValue.Bool(false)),
            ("setupActorId", JsValue.Int(setupActor.Id)),
            ("setupClass", JsValue.Str(op.SetupClass)),
            ("endsTick", JsValue.Int(endsTick)),
            ("telegraph", FloorTelegraphs.FloorWindowOpenedTelegraph(state, patch).ToJs())));
    }

    /// <summary>
    /// windows.ts:113-137 checkWindowExpiries, once per actor per tick before the generic expiry sweep: each Window
    /// instance ending by now emits "window_expired"; an expired Lock bumps the unpaid counter and, at
    /// <see cref="WindowState.LockEnrageThreshold"/>, resets it and emits "lock_enrage". The walk is by index over
    /// the list as it was at entry, re-reading its length each step: exactly the JS for-of over an array that the
    /// counter may push onto (a pushed counter is permanent, so it is skipped).
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The carrier.</param>
    /// <param name="emit">Event sink.</param>
    public static void CheckWindowExpiries(SimState state, ActorState actor, Action<SimEvent> emit)
    {
        var now = state.Tick;
        var list = actor.Effects;
        for (var i = 0; i < list.Count; i++)
        {
            var inst = list[i];
            if (inst.EndsTick > now)
            {
                continue;
            }
            var parsed = WindowState.ParseWindowEffectId(inst.EffectId);
            if (parsed is null)
            {
                continue;
            }
            emit(new SimEvent(
                now,
                "window_expired",
                ("windowId", JsValue.Str(parsed.WindowId)),
                ("onTarget", JsValue.Bool(true)),
                ("setupActorId", JsValue.Int(inst.SourceId)),
                ("setupClass", JsValue.Str(parsed.SetupClass)),
                ("targetId", JsValue.Int(actor.Id)),
                ("lock", JsValue.Bool(parsed.Alternate))));
            if (parsed.Alternate)
            {
                var fails = WindowState.IncrementLockFailCount(actor);
                if (fails >= WindowState.LockEnrageThreshold)
                {
                    WindowState.ResetLockFailCount(actor);
                    emit(new SimEvent(now, "lock_enrage", ("actorId", JsValue.Int(actor.Id))));
                }
            }
        }
    }

    /// <summary>windows.ts:140-154 checkFloorExpiries, once per tick: deletes each patch ending by now, emitting "window_expired".</summary>
    /// <param name="state">Sim state.</param>
    /// <param name="floor">Floor registry.</param>
    /// <param name="emit">Event sink.</param>
    public static void CheckFloorExpiries(SimState state, FloorWindowRegistry floor, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(floor);
        var now = state.Tick;
        foreach (var key in floor.Patches.Keys)
        {
            var patch = floor.Patches[key];
            if (patch.EndsTick > now)
            {
                continue;
            }
            floor.Patches.Remove(patch.WindowId);
            emit(new SimEvent(
                now,
                "window_expired",
                ("windowId", JsValue.Str(patch.WindowId)),
                ("onTarget", JsValue.Bool(false)),
                ("setupActorId", JsValue.Int(patch.SetupActorId)),
                ("setupClass", JsValue.Str(patch.SetupClass))));
        }
    }

    /// <summary>
    /// windows.ts:167-190 applyWindowsAndCombos, called right after a cast completes: when the ability has a window,
    /// payoff or combo op, its targets are re-resolved (same shape, same affinity) and
    /// <see cref="ApplyWindowsAndCombosTo"/> runs.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The completed ability.</param>
    /// <param name="ctx">Tables, registry and hooks.</param>
    /// <param name="emit">Event sink.</param>
    public static void ApplyWindowsAndCombos(SimState state, ActorState actor, AbilityDef def, WindowsContext ctx, Action<SimEvent> emit) =>
        ApplyWindowsAndCombosAt(state, actor, def, Casting.CastTargetOf(actor), actor.Cast, ctx, emit);

    /// <summary>
    /// <see cref="ApplyWindowsAndCombos(SimState, ActorState, AbilityDef, WindowsContext, Action{SimEvent})"/> for a
    /// cast that has already ended (R2): its targets resolve on <paramref name="ended"/>'s pinned start target
    /// (CAST-KEEPS-START-TARGET), so the pin no longer has to outlive the cast on the actor.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The completed ability.</param>
    /// <param name="ended">The cast <see cref="Casting.CompleteCast"/> ended, or null (resolves on the current target).</param>
    /// <param name="ctx">Tables, registry and hooks.</param>
    /// <param name="emit">Event sink.</param>
    public static void ApplyWindowsAndCombos(SimState state, ActorState actor, AbilityDef def, CastInFlight? ended, WindowsContext ctx, Action<SimEvent> emit) =>
        ApplyWindowsAndCombosAt(state, actor, def, Casting.TargetOfEnded(actor, ended), ended, ctx, emit);

    private static void ApplyWindowsAndCombosAt(SimState state, ActorState actor, AbilityDef def, int? castTargetId, CastInFlight? cast, WindowsContext ctx, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(ctx);
        if (!def.Effects.Any(op => op is WindowOp or PayoffOp or ComboOp))
        {
            return;
        }
        ApplyWindowsAndCombosOn(state, actor, def, Targeting.ResolveTargetsOn(state, actor, def, Affinities.Of(def, ctx.Effects), castTargetId, cast), castTargetId, ctx, emit);
    }

    /// <summary>
    /// The targets-resolved half of applyWindowsAndCombos (windows.ts:174-189): opens every window op against the
    /// first target (an on-target op of a <see cref="MultiTargetTag"/> ability: against each of
    /// <see cref="MultiTargetRecipients"/>, a C# deviation for Warcry), then, for a Payoff, tries the first target's Window and falls back to a floor patch unless that
    /// attempt closed or was denied. The burst table is <see cref="WindowsContext.Combos"/>, or the one a
    /// <see cref="ComboOp"/> names. Split out so the oracle cast ledger can drive it with traced targets.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The completed ability.</param>
    /// <param name="targets">Its resolved targets, in order.</param>
    /// <param name="ctx">Tables, registry and hooks.</param>
    /// <param name="emit">Event sink.</param>
    /// <exception cref="ArgumentException">A combo op names a table other than "combos" or "fieldCombos".</exception>
    public static void ApplyWindowsAndCombosTo(SimState state, ActorState actor, AbilityDef def, IReadOnlyList<ActorState> targets, WindowsContext ctx, Action<SimEvent> emit) =>
        ApplyWindowsAndCombosOn(state, actor, def, targets, Casting.CastTargetOf(actor), ctx, emit);

    private static void ApplyWindowsAndCombosOn(SimState state, ActorState actor, AbilityDef def, IReadOnlyList<ActorState> targets, int? castTargetId, WindowsContext ctx, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(ctx);
        var primary = targets.Count > 0 ? targets[0] : null;
        var multi = def.Tags?.Contains(MultiTargetTag) == true;
        foreach (var op in def.Effects)
        {
            if (op is not WindowOp w)
            {
                continue;
            }
            if (multi && w.OnTarget && w.Self != true)
            {
                foreach (var dest in MultiTargetRecipients(castTargetId, targets))
                {
                    OpenWindow(state, actor, w, ctx.Floor, emit, dest);
                }
                continue;
            }
            OpenWindow(state, actor, w, ctx.Floor, emit, primary);
        }
        var comboOp = def.Effects.OfType<ComboOp>().FirstOrDefault();
        if (comboOp is null && !def.Effects.Any(op => op is PayoffOp))
        {
            return;
        }
        var table = comboOp is null ? ctx.Combos : TableNamed(ctx, comboOp.ComboTable);
        if (primary is not null)
        {
            var outcome = Payoff.TryClosePayoff(state, primary, actor, table, ctx.Effects, emit, ctx.Hooks, ctx.Overheat);
            if (outcome != PayoffOutcome.NoWindow)
            {
                return;
            }
        }
        Payoff.TryClosePayoffOnFloor(state, actor, ctx.Floor, ctx.FieldCombos, ctx.Effects, emit, ctx.Hooks);
    }

    /// <summary>
    /// The ability tag (Warden's Warcry talent, content classes/warden.json "Strike applies Sundered to two adjacent
    /// enemies: its target and the nearest other enemy in the cone.") that spreads an on-target Window to <see cref="MultiTargetCount"/> of the resolved targets.
    /// </summary>
    public const string MultiTargetTag = "warcry:multi-target";

    /// <summary>How many targets a <see cref="MultiTargetTag"/> ability opens its on-target Window on.</summary>
    public const int MultiTargetCount = 2;

    /// <summary>
    /// The targets a <see cref="MultiTargetTag"/> ability opens its on-target Window on: the primary (the caster's
    /// target when it is among <paramref name="targets"/>, else the first), then the targets nearest the primary
    /// (squared XZ distance, ties to the lower id), up to <see cref="MultiTargetCount"/> in total. "Adjacent" means
    /// nearest to the primary; fewer targets give fewer recipients.
    /// </summary>
    /// <param name="castTarget">The target the caster's cast resolves on (<see cref="Casting.TargetOfEnded"/>), or null.</param>
    /// <param name="targets">Resolved targets, in id order.</param>
    /// <returns>Recipients, primary first.</returns>
    internal static IReadOnlyList<ActorState> MultiTargetRecipients(int? castTarget, IReadOnlyList<ActorState> targets)
    {
        if (targets.Count == 0)
        {
            return targets;
        }
        var primary = targets.FirstOrDefault(t => castTarget is int tid && t.Id == tid) ?? targets[0];
        var rest = targets
            .Where(t => t.Id != primary.Id)
            .OrderBy(t => Dist2(primary, t))
            .ThenBy(t => t.Id)
            .Take(MultiTargetCount - 1);
        return new[] { primary }.Concat(rest).ToList();
    }

    private static long Dist2(ActorState a, ActorState b)
    {
        var dx = a.Pos.X - b.Pos.X;
        var dz = a.Pos.Z - b.Pos.Z;
        return (dx * dx) + (dz * dz);
    }

    private static ComboTable TableNamed(WindowsContext ctx, string name) => name switch
    {
        Abilities.CombosTable => ctx.Combos,
        Abilities.FieldCombosTable => ctx.FieldCombos,
        _ => throw new ArgumentException($"combo op names unknown table \"{name}\"", nameof(name)),
    };
}
