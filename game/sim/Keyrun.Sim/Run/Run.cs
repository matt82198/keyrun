using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The composition root (sim/index.ts): wires the core tick loop, the combat StepRules and the encounter step into one
/// <see cref="CreateRun"/> / <see cref="StepRun"/> pair, so a host (the Godot SimDriver, tests, the replay tool) never
/// hand-wires the lanes. Every <see cref="StepRun"/> is addressed to the run's current tick, in this order:
/// <list type="number">
/// <item><see cref="EncounterStep.Step"/> on the pre-combat state: enemy casts, telegraphs, break-bar drains off this
/// tick's caller commands, pull advance.</item>
/// <item>Caller commands then enemy commands through <see cref="Tick.Step"/> with <see cref="Rules.Make"/> built from the
/// content and the run's Clause (<see cref="RunWiring.RulesConfigOf"/>).</item>
/// <item>Break-bar onEmpty patches; the next pull's spawn (with <c>pull_start</c>) when the index advanced.</item>
/// <item>Echo spawns, targeting fixup, Slack spend on player deaths and wipe, run-end detection and <c>run_end</c>.</item>
/// </list>
/// </summary>
public static class RunRoot
{
    /// <summary>
    /// index.ts:72 createRun: a fresh state with the party as player actors and pull 0 spawned (its pull_start is
    /// dropped: createRun has no event sink), loadouts merged, the Clause folded into the starting rules, a full
    /// Slack bank for the key, and targeting already resolved.
    /// </summary>
    /// <param name="seed">Run seed (the PRNG is seeded from its ToUint32).</param>
    /// <param name="party">Party members, in order.</param>
    /// <param name="dungeon">The dungeon.</param>
    /// <param name="content">The run content.</param>
    /// <param name="options">Clause pick and loadouts; null = none.</param>
    /// <returns>The new run.</returns>
    /// <exception cref="UnknownClauseException">An unknown Clause id.</exception>
    /// <exception cref="ClauseAlreadyActiveException">The content's base rules already carry a Clause.</exception>
    /// <exception cref="UnknownLoadoutActorException">A loadout for an actor not in the party.</exception>
    /// <exception cref="UnknownLoadoutAbilityException">A loadout patching an unknown ability.</exception>
    public static Run CreateRun(long seed, IReadOnlyList<PartyMemberSpec> party, DungeonDef dungeon, RunContent content, RunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(content);

        var state = SimState.Create(seed);
        var players = party
            .Select(m => Actors.Create(m.Id, ActorKind.Player, m.ClassId, m.Role, m.Pos, m.MaxHealth, m.MaxResource, m.Yaw ?? 0))
            .ToList();
        state = RunWiring.MergeActors(state, players);

        var keyLevel = content.KeyLevel ?? 1;
        if (content.Layout is { } layout)
        {
            // Layout run (STATE.md D18, D27): pull 0 spawns only when the living party already stands in its room (a
            // --pull N start placed inside the room), never on top of a party that starts in the Entrance.
            layout.RequireTriggers(dungeon);
            if (dungeon.Pulls.Count > 0 && NavLayout.PartyInside(state, layout.RoomFor(dungeon.Pulls[0].Id)!.Box))
            {
                state = RunWiring.MergeActors(state, Pulls.SpawnPull(dungeon, 0, state.Tick, static _ => { }));
            }
        }
        else
        {
            state = RunWiring.MergeActors(state, Pulls.SpawnPull(dungeon, 0, state.Tick, static _ => { }));
        }

        var abilities = RunOptions.MergeLoadoutAbilities(content.Abilities, party, options?.Loadouts);
        var effective = ReferenceEquals(abilities, content.Abilities) ? content : content with { Abilities = abilities };
        var rules = RunOptions.ResolveEncounterRules(content, keyLevel, options?.ClauseId);
        var encounter = EncounterState.Create(keyLevel, content.ParTicks ?? 24000, rules);

        RunWiring.FixupTargeting(state);
        return new Run(state, encounter, dungeon, effective, RunEndState.Initial);
    }

    /// <summary>
    /// index.ts:136 stepRun: advances <paramref name="run"/> by exactly one tick. <paramref name="commands"/> must be
    /// addressed to <c>run.State.Tick</c> (anything else is dropped by <see cref="Tick.Step"/>). The input run is not
    /// modified.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="commands">This tick's caller commands (players and bots).</param>
    /// <param name="opts">Host options (abandon); null = none.</param>
    /// <returns>The advanced run and the tick's events.</returns>
    public static RunStepResult StepRun(Run run, IReadOnlyList<Command> commands, StepRunOpts? opts = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(commands);
        var events = new List<SimEvent>();
        Action<SimEvent> emit = events.Add;
        var content = run.Content;

        var wiring = new BreakBarWiring(
            content.BreakBarSources ?? Array.Empty<BreakBarSource>(),
            commands,
            RunWiring.AbilityTagsOf(content.Abilities));
        var layout = content.Layout;
        var enc = EncounterStep.Step(
            run.State,
            run.Encounter,
            layout is null ? run.Dungeon : UnspawnedAsEmpty(run.State, run.Dungeon, run.Encounter.PullIndex),
            content.EnemyScripts,
            emit,
            wiring,
            RunWiring.ScriptAbilitiesOf(content.Abilities));

        // The run's Clause rides in its rules for exactly this step (clause-hooks.ts withEncounterRules).
        var rules = Rules.Make(RunWiring.RulesConfigOf(content, run.Encounter.Rules));
        var merged = commands.Concat(enc.Commands).ToList();
        var stepped = Tick.Step(run.State, merged, rules);
        events.AddRange(stepped.Events);

        var nextState = RunWiring.ApplyBreakBarPatches(stepped.State, enc.EffectPatches, content.Effects);
        var nextEncounter = enc.Encounter;
        if (layout is null)
        {
            if (nextEncounter.PullIndex != run.Encounter.PullIndex)
            {
                nextState = RunWiring.MergeActors(nextState, Pulls.SpawnPull(run.Dungeon, nextEncounter.PullIndex, nextState.Tick, emit));
            }
        }
        else
        {
            nextState = StepLayout(layout, run, nextState, nextEncounter, emit, opts?.OpenSeats);
        }
        var echo = EchoSpawns.Step(nextEncounter, nextState, events, nextState.Tick, emit);
        nextEncounter = echo.Encounter;
        nextState = RunWiring.MergeActors(nextState, echo.Spawned);

        RunWiring.FixupTargeting(nextState);
        nextEncounter = RunWiring.ApplyDeathsAndWipe(nextState, nextEncounter, events, nextState.Tick, emit);

        var runEnd = RunWiring.CheckRunEnd(run.RunEnd, nextState, nextEncounter, run.Dungeon, nextState.Tick, opts?.AbandonRequested ?? false, emit);
        if (runEnd.Result is RunResult result && run.RunEnd.Result is null)
        {
            if (result == RunResult.Depleted)
            {
                nextEncounter = nextEncounter with { Depleted = true };
            }
            emit(new SimEvent(nextState.Tick, "run_end", ("result", JsValue.Str(result.ToJs()))));
        }

        return new RunStepResult(new Run(nextState, nextEncounter, run.Dungeon, run.Content, runEnd), events);
    }

    // A layout run's active pull before its room is entered has no actors in the state, and pulls.ts counts a missing
    // actor as dead: shown as it is, the encounter would clear it at once and cascade through every pull. The encounter
    // step sees the unspawned pull with no spawns, which never advances (pulls.ts:82), so it clears only once fought.
    private static DungeonDef UnspawnedAsEmpty(SimState state, DungeonDef dungeon, int pullIndex)
    {
        if (pullIndex < 0 || pullIndex >= dungeon.Pulls.Count || Pulls.IsSpawned(state, dungeon, pullIndex))
        {
            return dungeon;
        }
        var pulls = dungeon.Pulls.ToList();
        pulls[pullIndex] = pulls[pullIndex] with { Spawns = Array.Empty<PullSpawn>() };
        return dungeon with { Pulls = pulls };
    }

    /// <summary>
    /// The layout rules after combat (STATE.md D18; no TS counterpart): (1) every actor that moved off the open boxes is
    /// put back by <see cref="Movement.ClampToWalkable"/> against its pre-step position (a gated box opens on the tick its
    /// pull clears) and, while the active pull is fought, by its room's closed gate (STATE.md D27,
    /// <see cref="NavLayout.GateClamp"/>; <c>gate_open {room}</c> on the tick the pull clears); (2) <c>room_enter</c> for
    /// each living player that stepped into a room box; (3) the active pull spawns, with its <c>pull_start</c> and
    /// <c>gate_closed {room}</c>, on the first tick every living player stands in its room box.
    /// </summary>
    private static SimState StepLayout(NavLayout layout, Run run, SimState next, EncounterState encounter, Action<SimEvent> emit, IReadOnlyList<int>? openSeats)
    {
        // ROOM-LOCK-IN (STATE.md D27): the active pull's room is gated from its pull_start until its pull_clear. The gate
        // holds this tick when the pull was already spawned before the step and did not clear during it.
        var priorIndex = run.Encounter.PullIndex;
        var priorFought = priorIndex < run.Dungeon.Pulls.Count && Pulls.IsSpawned(run.State, run.Dungeon, priorIndex);
        var locked = priorFought && priorIndex == encounter.PullIndex ? layout.RoomFor(run.Dungeon.Pulls[priorIndex].Id) : null;

        var open = layout.OpenBoxes(run.Dungeon, encounter.PullIndex);
        foreach (var actor in next.Actors.Values)
        {
            var pre = run.State.Actors.TryGetValue(actor.Id, out var was) ? was.Pos : actor.Pos;
            actor.Pos = Movement.ClampToWalkable(pre, actor.Pos, open);
            if (locked is not null)
            {
                actor.Pos = NavLayout.GateClamp(locked.Box, pre, actor.Pos);
            }
        }

        if (priorFought && priorIndex != encounter.PullIndex && layout.RoomFor(run.Dungeon.Pulls[priorIndex].Id) is { } cleared)
        {
            // Stamped like the pull_clear it follows (the encounter step's tick).
            emit(new SimEvent(run.State.Tick, "gate_open", ("room", JsValue.Str(cleared.Id))));
        }

        foreach (var actor in next.Actors.Values)
        {
            if (actor.Kind != ActorKind.Player || !actor.Alive || !run.State.Actors.TryGetValue(actor.Id, out var was))
            {
                continue;
            }
            foreach (var box in layout.Boxes)
            {
                if (box.IsRoom && !Movement.Contains(box.Box, was.Pos) && Movement.Contains(box.Box, actor.Pos))
                {
                    emit(new SimEvent(next.Tick, "room_enter", ("actorId", JsValue.Int(actor.Id)), ("room", JsValue.Str(box.Id))));
                }
            }
        }

        var index = encounter.PullIndex;
        if (index < run.Dungeon.Pulls.Count && !Pulls.IsSpawned(next, run.Dungeon, index) &&
            layout.RoomFor(run.Dungeon.Pulls[index].Id) is { } room && NavLayout.PartyInside(next, room.Box, openSeats))
        {
            next = RunWiring.MergeActors(next, Pulls.SpawnPull(run.Dungeon, index, next.Tick, emit));
            emit(new SimEvent(next.Tick, "gate_closed", ("room", JsValue.Str(room.Id))));
        }
        return next;
    }
}
