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
            .Select(m =>
            {
                var actor = Actors.Create(m.Id, ActorKind.Player, m.ClassId, m.Role, m.Pos, m.MaxHealth, m.MaxResource, m.Yaw ?? 0);
                actor.Resource = Actors.StartingResource(m.ClassId, m.MaxResource, content.Overheat);
                return actor;
            })
            .ToList();
        if (options?.Gear is { } gear)
        {
            foreach (var (actorId, stats) in gear.OrderBy(kv => kv.Key))
            {
                var wearer = players.FirstOrDefault(a => a.Id == actorId)
                    ?? throw new ArgumentException(FormattableString.Invariant($"createRun: options.gear has an entry for actorId {actorId}, which is not in party"), nameof(options));
                GearPower.Apply(wearer, stats);
            }
        }
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
        return new Run(state, encounter, dungeon, effective, RunEndState.Initial) { SeedRule = SeedRuleOf(effective) };
    }

    /// <summary>
    /// The Seed's loot rule (BIBLE s9 item 3): <see cref="SeedLoot.RuleFor"/> over the enemy scripts, lowest actor id
    /// first, with an ability's windowId taken from its first <see cref="WindowOp"/>; null when none opens a Window.
    /// </summary>
    /// <param name="content">The run content.</param>
    /// <returns>The rule, or null.</returns>
    public static SeedLootRule? SeedRuleOf(RunContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        string? WindowIdOf(string abilityId) => content.Abilities.TryGetValue(abilityId, out var ability)
            ? ability.Effects.OfType<WindowOp>().Select(w => w.WindowId).FirstOrDefault()
            : null;
        foreach (var (actorId, script) in content.EnemyScripts.OrderBy(kv => kv.Key))
        {
            if (SeedLoot.RuleFor(actorId, script, WindowIdOf) is { } rule)
            {
                return rule;
            }
        }
        return null;
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

        // RUN-END-FREEZE: once run_end has fired the run is over. No rule, script, cast or death resolves any more, so
        // an add left standing (a Sundered Twin when the boss dies) cannot take damage or die after the run record is
        // written. The clock still advances exactly as Tick.Step advances it, so a host that keeps stepping stays in step.
        if (run.RunEnd.Result is not null)
        {
            var frozen = run.State.Clone();
            frozen.Tick += 1;
            frozen.RunTimerTicks += 1;
            return new RunStepResult(run with { State = frozen }, events);
        }

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
            run.ActiveScripts ?? content.EnemyScripts,
            emit,
            wiring,
            RunWiring.ScriptAbilitiesOf(content.Abilities));

        // The run's Clause rides in its rules for exactly this step (clause-hooks.ts withEncounterRules).
        var rules = Rules.Make(RunWiring.RulesConfigOf(content, run.Encounter.Rules));
        var merged = commands.Concat(enc.Commands).ToList();
        // BIBLE.md:156 Tide: floor patches drift with the telegraphs, before combat reads them this tick.
        var stepped = Tick.Step(Clauses.DriftFloorWindows(run.State, run.Encounter.Rules), merged, rules);
        events.AddRange(stepped.Events);

        var nextState = RunWiring.ApplyBreakBarPatches(stepped.State, enc.EffectPatches, content.Effects);
        var nextEncounter = enc.Encounter;
        var activeScripts = run.ActiveScripts;
        if (layout is null)
        {
            if (nextEncounter.PullIndex != run.Encounter.PullIndex)
            {
                nextState = RunWiring.MergeActors(nextState, Pulls.SpawnPull(run.Dungeon, nextEncounter.PullIndex, nextState.Tick, emit));
            }
        }
        else
        {
            var index = nextEncounter.PullIndex;
            var unspawned = index < run.Dungeon.Pulls.Count && !Pulls.IsSpawned(nextState, run.Dungeon, index);
            nextState = StepLayout(layout, run, nextState, nextEncounter, emit, opts?.OpenSeats);
            if (unspawned && Pulls.IsSpawned(nextState, run.Dungeon, index))
            {
                activeScripts = OnPullClock(activeScripts ?? content.EnemyScripts, run.Dungeon.Pulls[index], nextState.Tick);
            }
        }
        var echo = EchoSpawns.Step(nextEncounter, nextState, events, nextState.Tick, emit);
        nextEncounter = echo.Encounter;
        nextState = RunWiring.MergeActors(nextState, echo.Spawned);

        RunWiring.FixupTargeting(nextState);
        // GATED-WIPE (f-3cc5aeee): with the active pull's gate shut, the party has wiped once nobody alive is inside it.
        var gatedRoom = layout is null ? null : GatedRoom(layout, run.Dungeon, nextState, nextEncounter)?.Box;
        nextEncounter = RunWiring.ApplyDeathsAndWipe(nextState, nextEncounter, events, nextState.Tick, emit, gatedRoom);

        var runEnd = RunWiring.CheckRunEnd(run.RunEnd, nextState, nextEncounter, run.Dungeon, nextState.Tick, opts?.AbandonRequested ?? false, emit, gatedRoom);
        if (runEnd.Result is RunResult result && run.RunEnd.Result is null)
        {
            if (result == RunResult.Depleted)
            {
                nextEncounter = nextEncounter with { Depleted = true };
            }
            emit(new SimEvent(nextState.Tick, "run_end", ("result", JsValue.Str(result.ToJs()))));
        }

        // SEED-LOOT-AWARD: the run root awards the Seed bonus itself (SeedLoot.cs summary: "the run root calls Observe
        // once per tick with that tick's events").
        var rewards = run.SeedRule is { } seedRule ? SeedLoot.Observe(run.Rewards, seedRule, events) : run.Rewards;
        var next = new Run(nextState, nextEncounter, run.Dungeon, run.Content, runEnd)
        {
            Rewards = rewards,
            SeedRule = run.SeedRule,
            ActiveScripts = activeScripts,
        };
        return new RunStepResult(next, events);
    }

    // SEED-PULL-TEACHES (STATE.md D27, design/BIBLE.md sections 3 and 9): a room-entry pull's scripts run on the pull's
    // own clock from its pull_start tick, so its first cast and that cast's telegraph fall inside the fight whatever tick
    // the party walked in on. Every other actor's script is kept as it is.
    private static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> OnPullClock(
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts, PullDef pull, int startTick)
    {
        var ids = pull.Spawns.Select(s => s.ActorId).ToHashSet();
        if (startTick == 0 || !scripts.Keys.Any(ids.Contains))
        {
            return scripts;
        }
        var shifted = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        foreach (var (actorId, script) in scripts)
        {
            shifted[actorId] = ids.Contains(actorId) ? script.Select(s => Scripts.StartingAt(s, startTick)).ToList() : script;
        }
        return shifted;
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
    /// The active pull's room while its gate is shut (ROOM-LOCK-IN, STATE.md D27): the pull is being fought (its actors
    /// are spawned) and has not cleared. Null when every pull is cleared, the pull is not fought yet, or the layout has no
    /// room for it. The one definition of "the gate is shut": <see cref="StepLayout"/>'s gate clamp and the GATED-WIPE
    /// rule (<see cref="RunWiring.IsWipe"/>) both read it, so they cannot drift apart.
    /// </summary>
    private static NavBox? GatedRoom(NavLayout layout, DungeonDef dungeon, SimState state, EncounterState encounter)
    {
        var index = encounter.PullIndex;
        return index < dungeon.Pulls.Count && Pulls.IsSpawned(state, dungeon, index) ? layout.RoomFor(dungeon.Pulls[index].Id) : null;
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
        var priorFought = GatedRoom(layout, run.Dungeon, run.State, run.Encounter) is not null;
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
