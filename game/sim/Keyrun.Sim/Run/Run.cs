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
        var engaged = new List<string>();
        if (content.Layout is { } layout)
        {
            // PRESPAWN-AGGRO (BIBLE section 9.1): on a layout run every group, the boss included, stands idle in
            // formation from tick 0, and each engages on its own aggro radius (StepLayout), never on room entry.
            layout.RequireTriggers(dungeon);
            state = RunWiring.MergeActors(state, Pulls.SpawnAllIdle(dungeon, content.ActorRadiusMm));
            // PATH-PACKS: every layout pack, required and optional, stands idle on the path from tick 0 too.
            state = RunWiring.MergeActors(state, LayoutPacks.SpawnAllIdle(content.Packs, content.ActorRadiusMm));
        }
        else
        {
            state = RunWiring.MergeActors(state, Pulls.SpawnPull(dungeon, 0, state.Tick, static _ => { }));
            if (dungeon.Pulls.Count > 0)
            {
                engaged.Add(dungeon.Pulls[0].Id);
            }
        }

        var abilities = RunOptions.MergeLoadoutAbilities(content.Abilities, party, options?.Loadouts);
        var effective = ReferenceEquals(abilities, content.Abilities) ? content : content with { Abilities = abilities };
        var rules = RunOptions.ResolveEncounterRules(content, keyLevel, options?.ClauseId);
        var encounter = EncounterState.Create(keyLevel, content.ParTicks ?? 24000, rules) with { Engaged = engaged };

        RunWiring.FixupTargeting(state);
        return new Run(state, encounter, dungeon, effective, RunEndState.Initial) { SeedRule = SeedRuleOf(effective) };
    }

    /// <summary>
    /// A host start that begins in its fight (the <c>--pull N</c> test start: NavLayouts.PlaceParty "a --pull N run
    /// starts in its fight"; STATE.md D27 spawned that pull at creation). PRESPAWN-AGGRO (BIBLE section 9.1) leaves every
    /// layout group idle at creation, even with the party standing in the active pull's room outside its aggro ring, so a
    /// host that asks for a fight start engages the active pull here, exactly as an aggro-ring engage would (its actors
    /// stop being idle, <see cref="EncounterState.Engaged"/> records it, enemies target the tank). No event is emitted
    /// (createRun has no event sink).
    /// The input run is not modified. Unchanged for a run without a layout, a finished dungeon or an engaged pull.
    /// </summary>
    /// <param name="run">A freshly created run.</param>
    /// <returns>The run with its active pull engaged.</returns>
    public static Run EngageStartPull(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var index = run.Encounter.PullIndex;
        if (run.Content.Layout is null || index < 0 || index >= run.Dungeon.Pulls.Count || Pulls.IsEngaged(run.Encounter, run.Dungeon, index))
        {
            return run;
        }
        var state = run.State.Clone();
        var groups = Aggro.GroupsOf(run.Dungeon, state.Tick, run.Content.Packs);
        var encounter = Aggro.Engage(state, run.Encounter, groups, new[] { run.Dungeon.Pulls[index].Id }, state.Tick, static _ => { });
        RunWiring.FixupTargeting(state);
        return run with { State = state, Encounter = encounter };
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
            run.Dungeon,
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

        // STUN-STOPS-CAST (BIBLE s9.3): a stun, freeze or silence that landed this tick (in combat, or the Break bar's
        // stagger just above) ends an interruptible cast, before the telegraph check below reads the casts.
        nextState = ControlStops.Apply(run.State, nextState, content.StopEffectIds, content.Abilities, run.State.Tick, emit);

        // TELEGRAPH-CANCEL (BIBLE s3): a floor telegraph whose cast this tick's combat kicked, stopped or replaced is
        // cancelled on this tick and never resolves (no telegraph_hit for a hit that cannot come).
        var cancel = Telegraphs.CancelUnlanded(nextState, enc.Encounter.Telegraphs, run.State.Tick, emit);
        var nextEncounter = cancel.Cancelled.Count == 0 ? enc.Encounter : enc.Encounter with { Telegraphs = cancel.Telegraphs };
        var activeScripts = run.ActiveScripts;
        if (layout is null)
        {
            if (nextEncounter.PullIndex != run.Encounter.PullIndex)
            {
                nextState = RunWiring.MergeActors(nextState, Pulls.SpawnPull(run.Dungeon, nextEncounter.PullIndex, nextState.Tick, emit));
                if (nextEncounter.PullIndex < run.Dungeon.Pulls.Count)
                {
                    nextEncounter = nextEncounter with { Engaged = nextEncounter.Engaged.Append(run.Dungeon.Pulls[nextEncounter.PullIndex].Id).ToList() };
                }
            }
        }
        else
        {
            var before = nextEncounter.Engaged.Count;
            (nextState, nextEncounter) = StepLayout(layout, run, nextState, nextEncounter, emit, opts?.OpenSeats);
            var newly = nextEncounter.Engaged.Skip(before).ToList();
            if (newly.Count > 0)
            {
                // A pull or a pack (PATH-PACKS) runs its scripts on its own clock from its engage tick.
                var ids = Aggro.GroupsOf(run.Dungeon, nextState.Tick, content.Packs).Where(g => newly.Contains(g.Id)).SelectMany(g => g.ActorIds).ToHashSet();
                activeScripts = OnEngageClock(activeScripts ?? content.EnemyScripts, ids, nextState.Tick);
            }
        }
        var echo = EchoSpawns.Step(nextEncounter, nextState, events, nextState.Tick, emit);
        nextEncounter = echo.Encounter;
        nextState = RunWiring.MergeActors(nextState, echo.Spawned);

        RunWiring.FixupTargeting(nextState);
        // PATH-TO-BOSS (STATE.md D44): an open (abandoned or unfilled) seat is not in the run, so it never holds a wipe open.
        var openSeats = opts?.OpenSeats;
        nextEncounter = RunWiring.ApplyDeathsAndWipe(nextState, nextEncounter, events, nextState.Tick, emit, openSeats);

        var runEnd = RunWiring.CheckRunEnd(run.RunEnd, nextState, nextEncounter, run.Dungeon, nextState.Tick, opts?.AbandonRequested ?? false, emit, openSeats);
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

    // SEED-PULL-TEACHES (STATE.md D27) and PRESPAWN-AGGRO (BIBLE section 9.1, "Scripts start at engage"): an engaged
    // group's scripts run on the group's own clock from its engage tick, so its first cast and that cast's telegraph fall
    // inside the fight whatever tick the party arrived on. Every other actor's script is kept as it is.
    private static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> OnEngageClock(
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts, IReadOnlySet<int> ids, int startTick)
    {
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

    /// <summary>
    /// The layout rules after combat (STATE.md D18; no TS counterpart): (1) every actor that moved off the open boxes is
    /// put back by <see cref="Movement.ClampToWalkable"/> against its pre-step position; no room gate holds anyone in or
    /// out of an engaged room (PATH-TO-BOSS, STATE.md D44, which removed the D27 gate: the party walks one forward path
    /// and the packs sit along it); (2) <c>room_enter</c> for each living player that stepped into a room box;
    /// (3) PRESPAWN-AGGRO (BIBLE section 9.1): every group whose aggro ring holds a living party member, or one of whose
    /// members took damage, engages (<see cref="Aggro"/>).
    /// </summary>
    private static (SimState State, EncounterState Encounter) StepLayout(NavLayout layout, Run run, SimState next, EncounterState encounter, Action<SimEvent> emit, IReadOnlyList<int>? openSeats)
    {
        var open = layout.OpenBoxes(run.Dungeon, encounter.PullIndex);
        foreach (var actor in next.Actors.Values)
        {
            if (actor.Idle)
            {
                // PRESPAWN-AGGRO: an idle group stands in its own room, which a layout may still hold shut (not an open
                // box). It runs no script and never chases, so it is not put back: clamping would drag it into the open boxes.
                continue;
            }
            var pre = run.State.Actors.TryGetValue(actor.Id, out var was) ? was.Pos : actor.Pos;
            actor.Pos = Movement.ClampToWalkable(pre, actor.Pos, open);
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

        var groups = Aggro.GroupsOf(run.Dungeon, next.Tick, run.Content.Packs);
        var triggered = Aggro.Triggered(run.State, next, groups, encounter, openSeats);
        encounter = Aggro.Engage(next, encounter, groups, triggered, next.Tick, emit);
        return (next, encounter);
    }
}
