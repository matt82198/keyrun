using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// What a tick needs to drain Break bars (index.ts:74 BreakBarWiring): the content's bar sources (in their JS key
/// order), this tick's submitted commands, and each ability's tags.
/// </summary>
/// <param name="Sources">Bar sources, in order.</param>
/// <param name="Commands">This tick's caller commands (players and bots).</param>
/// <param name="AbilityTags">Ability id to its tags (null = untagged).</param>
public sealed record BreakBarWiring(IReadOnlyList<BreakBarSource> Sources, IReadOnlyList<Command> Commands, IReadOnlyDictionary<string, IReadOnlyList<string>?> AbilityTags);

/// <summary>One encounter tick's output (index.ts:100 StepEncounterResult).</summary>
/// <param name="State">The input state, unchanged (the encounter never mutates ActorState).</param>
/// <param name="Encounter">The next encounter state.</param>
/// <param name="Commands">Enemy casts and chase moves for combat this tick.</param>
/// <param name="EffectPatches">onEmpty effects of any bar that staggered this tick, for the caller to apply.</param>
public sealed record StepEncounterResult(SimState State, EncounterState Encounter, IReadOnlyList<Command> Commands, IReadOnlyList<BreakBarEffectPatch> EffectPatches)
{
    /// <summary>
    /// C#-only (f-8b3fff18): the actors of the armed boss's Twins bar when it staggered this tick, in bar-actor order.
    /// The run root ends their in-progress casts when it applies the stun (<see cref="Runs.RunWiring.StopCasts"/>).
    /// Empty on a run with no armed boss; any other bar's stun follows the s9.3 interruptible-only rule (ControlStops).
    /// </summary>
    public IReadOnlyList<int> Staggered { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Composes the encounter lane for one tick (sim/encounter/index.ts:108 stepEncounter), run on the state BEFORE
/// combat (sim/index.ts:148-156). In order: (1) the scripts decide this tick's enemy casts and telegraphs, and the
/// "telegraph" events are emitted; (2) they join the registry with run-minted ids, Tide drifts every tracked
/// telegraph, the due ones resolve and emit <c>telegraph_hit</c> (who stood in it; no damage) and are dropped
/// (<see cref="Telegraphs.Advance"/>); (3) Break bars tick their reset, are lazily created the first tick all their
/// actors are alive, and drain off this tick's tagged casts; (4) the pull advances when cleared. The pull_start of the
/// next pull, Echo spawns and Slack are the run root's, after combat (sim/index.ts:165-175).
/// </summary>
public static class EncounterStep
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>?> NoTags = new Dictionary<string, IReadOnlyList<string>?>();

    /// <summary>index.ts:108 stepEncounter.</summary>
    /// <param name="state">The tick's state (read only).</param>
    /// <param name="encounter">The encounter state.</param>
    /// <param name="dungeon">The dungeon.</param>
    /// <param name="scripts">Actor id to enemy script.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="breakBarWiring">Break bar sources and this tick's commands; null = no bars (index.ts:114).</param>
    /// <param name="abilities">Ability views for script targeting and chase; null = none (index.ts:115).</param>
    public static StepEncounterResult Step(
        SimState state,
        EncounterState encounter,
        DungeonDef dungeon,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts,
        Action<SimEvent> emit,
        BreakBarWiring? breakBarWiring = null,
        IReadOnlyDictionary<string, ScriptAbility>? abilities = null)
    {
        var tick = state.Tick;

        // Lane 3F (no TS counterpart): an armed boss resolves its Lock and supplies the Twins' scripts and bar first.
        // Unarmed (Boss null, every oracle run) this is skipped and the step is exactly index.ts stepEncounter.
        // LOCK-TELEGRAPH: the ability views give a cast Lock its castTicks, and the registry holds the boss's own circles.
        BossPreStep? boss = encounter.Boss is { } armed ? BossRules.PreCombat(state, armed, scripts, emit, abilities, encounter.Telegraphs) : null;
        if (boss is not null)
        {
            scripts = boss.Scripts;
        }

        IReadOnlyList<BreakBarSource> sources = breakBarWiring?.Sources ?? Array.Empty<BreakBarSource>();
        if (boss is not null && !sources.Any(s => s.Id == boss.TwinsBar.Id))
        {
            sources = sources.Append(boss.TwinsBar).ToList();
        }

        // PRESPAWN-AGGRO (BIBLE section 9.1): an idle group runs no script, so it casts, telegraphs and chases nothing
        // until it engages. No actor of an oracle run is idle, so this keeps every script there.
        if (scripts.Keys.Any(id => state.Actors.TryGetValue(id, out var a) && a.Idle))
        {
            var awake = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
            foreach (var (id, script) in scripts)
            {
                if (!(state.Actors.TryGetValue(id, out var a) && a.Idle))
                {
                    awake[id] = script;
                }
            }
            scripts = awake;
        }

        // index.ts:119-123: scripts, emit their telegraph events, register them.
        var scripted = Scripts.StepEnemyScripts(state, scripts, encounter.FiredOnDeath, abilities, encounter.PendingSteps, encounter.DeferredCasts);
        foreach (var e in scripted.TelegraphEvents)
        {
            emit(e);
        }

        // f-8b3fff18 (BIBLE s9 Twins, s9.3 "Stops as well as kicks"; C#-only, no bar on any oracle run): a bar actor still
        // inside its stagger stun starts no cast, swing or chase move this tick. Telegraphs keep their schedule, so a cast
        // that falls after the stun still has its lead.
        var stunned = BreakBar.StunnedActors(sources, state.Actors, tick);
        var enemyCommands = stunned.Count == 0
            ? scripted.Commands
            : scripted.Commands.Where(c => !stunned.Contains(c.ActorId)).ToList();

        // index.ts:129-135: Tide drift, resolve, emit hits, drop the resolved.
        var advance = Telegraphs.Advance(state, encounter.Telegraphs, scripted.TelegraphEvents, encounter.NextTelegraphId, emit,
            all => Clauses.DriftTelegraphs(all, encounter.Rules), scripted.TelegraphCastStarts);

        // index.ts:137-141: reset cooldowns of existing bars.
        var breakBars = new JsRecord<BreakBarState>();
        foreach (var kv in encounter.BreakBars)
        {
            breakBars[kv.Key] = BreakBar.TickReset(kv.Value, tick);
        }

        // index.ts:143-157: lazy creation and drain.
        var effectPatches = new List<BreakBarEffectPatch>();
        var staggered = new List<int>();
        var commands = breakBarWiring?.Commands ?? Array.Empty<Command>();
        var abilityTags = breakBarWiring?.AbilityTags ?? NoTags;
        foreach (var source in sources)
        {
            if (!breakBars.ContainsKey(source.Id) &&
                source.ActorIds.All(id => state.Actors.TryGetValue(id, out var a) && a.Alive))
            {
                breakBars[source.Id] = BreakBar.Create(source.Max, source.ActorIds);
            }
            if (!breakBars.TryGetValue(source.Id, out var bar))
            {
                continue;
            }
            var wasStaggered = bar.Current <= 0;
            var inputs = BreakBar.DeriveInputs(commands, state.Actors, abilityTags, source);
            var drained = BreakBar.Drain(bar, source.Id, inputs, tick, emit);
            breakBars[source.Id] = drained;
            var justStaggered = !wasStaggered && drained.Current <= 0;
            effectPatches.AddRange(BreakBar.EffectPatches(source, justStaggered));
            // Only the armed boss's Twins bar (BIBLE s9 "at empty both are stunned for a burst window") ends the casts
            // of its actors outright. Any other bar's stun follows the s9.3 pack rule (ControlStops: an interruptible
            // cast only, StunStopsCastTests).
            if (justStaggered && boss is not null && source.Id == boss.TwinsBar.Id)
            {
                staggered.AddRange(source.ActorIds);
            }
        }

        // index.ts:159-160.
        var next = encounter with
        {
            Telegraphs = advance.Telegraphs,
            BreakBars = breakBars,
            FiredOnDeath = scripted.FiredOnDeath,
            PendingSteps = scripted.PendingSteps,
            DeferredCasts = scripted.DeferredCasts,
            NextTelegraphId = advance.NextTelegraphId,
        };
        if (boss is not null)
        {
            next = next with { Boss = boss.Boss };
            effectPatches.AddRange(boss.EffectPatches);
        }
        next = Pulls.CheckPullAdvance(state, dungeon, next, tick, emit);
        return new StepEncounterResult(state, next, enemyCommands, effectPatches) { Staggered = staggered };
    }
}
