using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>Which tagged input drains a bar (breakbar.ts:41 <c>"control" | "interrupt"</c>).</summary>
public enum BreakBarTag
{
    /// <summary>A control-tagged cast.</summary>
    Control,

    /// <summary>An interrupt-tagged cast (takes priority when an ability carries both).</summary>
    Interrupt,
}

/// <summary>A shared Break bar (breakbar.ts:31 BreakBarState).</summary>
/// <param name="Current">Remaining bar; at or below 0 it is staggered.</param>
/// <param name="Max">Full bar.</param>
/// <param name="ActorIds">The actors sharing it.</param>
/// <param name="CooldownReadyTick">When a staggered bar refills (0 = not staggered).</param>
public sealed record BreakBarState(long Current, long Max, IReadOnlyList<int> ActorIds, int CooldownReadyTick);

/// <summary>One drain input (breakbar.ts:38 BreakBarInput).</summary>
/// <param name="Tick">The tick it applies to; other ticks are ignored.</param>
/// <param name="ActorId">Who cast it.</param>
/// <param name="Tag">Control or interrupt.</param>
/// <param name="Amount">How much it drains.</param>
public sealed record BreakBarInput(int Tick, int ActorId, BreakBarTag Tag, long Amount);

/// <summary>An onEmpty effect (breakbar.ts:81; only "applyEffect" ops are represented).</summary>
/// <param name="EffectId">Effect id.</param>
/// <param name="DurationTicks">Duration.</param>
/// <param name="Stacks">Stacks.</param>
public sealed record BreakBarOnEmptyEffect(string EffectId, int DurationTicks, long Stacks);

/// <summary>A content-agnostic shared bar definition (breakbar.ts:88 BreakBarSource).</summary>
/// <param name="Id">Bar id.</param>
/// <param name="ActorIds">Actors carrying it.</param>
/// <param name="Max">Full bar.</param>
/// <param name="DrainPerInterrupt">Drain of one interrupt-tagged cast.</param>
/// <param name="DrainPerControl">Drain of one control-tagged cast.</param>
/// <param name="OnEmpty">Effects applied to every bar actor when it empties.</param>
public sealed record BreakBarSource(string Id, IReadOnlyList<int> ActorIds, long Max, long DrainPerInterrupt, long DrainPerControl, IReadOnlyList<BreakBarOnEmptyEffect> OnEmpty);

/// <summary>An effect the caller applies after combat resolves (breakbar.ts:99 BreakBarEffectPatch).</summary>
/// <param name="ActorId">Target actor.</param>
/// <param name="EffectId">Effect id.</param>
/// <param name="DurationTicks">Duration.</param>
/// <param name="Stacks">Stacks.</param>
public sealed record BreakBarEffectPatch(int ActorId, string EffectId, int DurationTicks, long Stacks);

/// <summary>
/// A shared bar drained by control/interrupt-tagged casts (sim/encounter/breakbar.ts; BIBLE s9 The Sundered Twins).
/// The instant it reaches 0 it emits exactly one <c>stagger</c> and locks until the reset cooldown elapses, then
/// refills to max. Drain reads a cast COMMAND's declared target (not a confirmed hit), as the TS does.
/// </summary>
public static class BreakBar
{
    /// <summary>200 ticks (10 s) before a drained bar refills (breakbar.ts:46).</summary>
    public const int ResetCooldownTicks = 200;

    /// <summary>breakbar.ts:48 createBreakBar: full, not staggered.</summary>
    public static BreakBarState Create(long max, IReadOnlyList<int> actorIds) => new(max, max, actorIds, 0);

    /// <summary>
    /// breakbar.ts:52 drainBreakBar: an already-staggered bar is returned unchanged; otherwise this tick's
    /// inputs drain it in order, and on reaching 0 it emits <c>{tick, type:"stagger", barId, actorIds}</c> and
    /// locks until <c>tick + 200</c>.
    /// </summary>
    public static BreakBarState Drain(BreakBarState bar, string barId, IEnumerable<BreakBarInput> inputs, int tick, Action<SimEvent> emit)
    {
        if (bar.Current <= 0)
        {
            return bar;
        }
        var current = bar.Current;
        foreach (var input in inputs)
        {
            if (input.Tick != tick)
            {
                continue;
            }
            if (current <= 0)
            {
                break;
            }
            current -= input.Amount;
        }
        if (current > 0)
        {
            return bar with { Current = current };
        }
        emit(new SimEvent(tick, "stagger",
            ("barId", JsValue.Str(barId)),
            ("actorIds", JsValue.Arr(bar.ActorIds.Select(id => JsValue.Int(id))))));
        return new BreakBarState(0, bar.Max, bar.ActorIds, tick + ResetCooldownTicks);
    }

    /// <summary>breakbar.ts:72 tickBreakBarReset: a staggered bar refills to max the tick its cooldown elapses.</summary>
    public static BreakBarState TickReset(BreakBarState bar, int tick)
    {
        var isStaggered = bar.Current <= 0 && bar.CooldownReadyTick > 0;
        if (isStaggered && tick >= bar.CooldownReadyTick)
        {
            return new BreakBarState(bar.Max, bar.Max, bar.ActorIds, 0);
        }
        return bar;
    }

    /// <summary>
    /// breakbar.ts:114 deriveBreakBarInputs: every cast command whose ability is tagged interrupt or control (interrupt
    /// wins when both) and whose target (the command's, else the caster's current target) is one of the bar's actors.
    /// </summary>
    public static IReadOnlyList<BreakBarInput> DeriveInputs(
        IEnumerable<Command> commands,
        IReadOnlyDictionary<int, ActorState> actors,
        IReadOnlyDictionary<string, IReadOnlyList<string>?> abilityTags,
        BreakBarSource source)
    {
        var barActors = new HashSet<int>(source.ActorIds);
        var inputs = new List<BreakBarInput>();
        foreach (var command in commands)
        {
            if (command is not CastCommand cast)
            {
                continue;
            }
            if (!abilityTags.TryGetValue(cast.AbilityId, out var tags) || tags is null)
            {
                continue;
            }
            var isInterrupt = tags.Contains("interrupt");
            var isControl = tags.Contains("control");
            if (!isInterrupt && !isControl)
            {
                continue;
            }
            var targetId = cast.TargetId ?? (actors.TryGetValue(cast.ActorId, out var caster) ? caster.TargetId : null);
            if (targetId is not { } t || !barActors.Contains(t))
            {
                continue;
            }
            inputs.Add(isInterrupt
                ? new BreakBarInput(cast.Tick, cast.ActorId, BreakBarTag.Interrupt, source.DrainPerInterrupt)
                : new BreakBarInput(cast.Tick, cast.ActorId, BreakBarTag.Control, source.DrainPerControl));
        }
        return inputs;
    }

    /// <summary>
    /// C#-only (f-8b3fff18, BIBLE s9 Twins and s9.3 "Stops as well as kicks"): the living bar actors that hold an
    /// active onEmpty effect of their bar at <paramref name="tick"/> (an instance ending after it), i.e. the Twins still
    /// inside their stagger stun. <see cref="EncounterStep"/> holds their scripts for the stun. Only bar actors count,
    /// so a run with no Break bar (every oracle run) is never affected.
    /// </summary>
    /// <param name="sources">The tick's bar sources.</param>
    /// <param name="actors">The tick's actors.</param>
    /// <param name="tick">The tick.</param>
    /// <returns>The held actor ids (empty when none).</returns>
    public static IReadOnlySet<int> StunnedActors(IEnumerable<BreakBarSource> sources, IReadOnlyDictionary<int, ActorState> actors, int tick)
    {
        var held = new HashSet<int>();
        foreach (var source in sources)
        {
            foreach (var actorId in source.ActorIds)
            {
                if (actors.TryGetValue(actorId, out var actor) && actor.Alive &&
                    actor.Effects.Exists(e => e.EndsTick > tick && source.OnEmpty.Any(o => o.EffectId == e.EffectId)))
                {
                    held.Add(actorId);
                }
            }
        }
        return held;
    }

    /// <summary>breakbar.ts:140 breakBarEffectPatches: one patch per actor per onEmpty effect, only when it just staggered.</summary>
    public static IReadOnlyList<BreakBarEffectPatch> EffectPatches(BreakBarSource source, bool justStaggered)
    {
        if (!justStaggered)
        {
            return Array.Empty<BreakBarEffectPatch>();
        }
        var patches = new List<BreakBarEffectPatch>();
        foreach (var actorId in source.ActorIds)
        {
            foreach (var eff in source.OnEmpty)
            {
                patches.Add(new BreakBarEffectPatch(actorId, eff.EffectId, eff.DurationTicks, eff.Stacks));
            }
        }
        return patches;
    }
}
