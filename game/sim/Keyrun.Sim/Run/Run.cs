using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The composition root (sim/index.ts): wires the core tick loop, the combat StepRules and the encounter step into one
/// <see cref="CreateRun"/> / <see cref="StepRun"/> pair, so a host (the Godot SimDriver, tests, the replay tool) never
/// hand-wires the lanes. Every <see cref="StepRun"/> is addressed to the run's current tick and runs the phases of
/// <see cref="RunPipeline"/> (one file each under <c>Run/Phases/</c>), in this order:
/// <list type="number">
/// <item>RUN-END-FREEZE: a finished run only advances its clock.</item>
/// <item><see cref="EncounterStep.Step"/> on the pre-combat state: enemy casts, telegraphs, break-bar drains off this
/// tick's caller commands, pull advance.</item>
/// <item>Caller commands then enemy commands through <see cref="Tick.Step"/> with <see cref="Rules.Make"/> built from the
/// content and the run's Clause (<see cref="RunWiring.RulesConfigOf"/>), cached per content in <see cref="RunTables"/>.</item>
/// <item>Break-bar onEmpty patches, immune-stun refusals, control stops, stagger stops, telegraph cancels.</item>
/// <item>The next pull's spawn (with <c>pull_start</c>) when the index advanced, or the layout step.</item>
/// <item>Echo spawns, the armed boss's death clearing its pull, releases (a layout run's dead seats respawn at the
/// entrance, <see cref="Release"/>), targeting fixup, run-clock time for player deaths and wipe, run-end detection and
/// <c>run_end</c>, the Seed loot award.</item>
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
                actor.ArmorBp = m.ArmorBp;
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
            // MPLUS-PATROLS: every patrol's members stand idle on its loop from tick 0 too.
            state = RunWiring.MergeActors(state, Patrols.SpawnIdle(content.Patrols));
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
        return new Run(state, encounter, dungeon, effective, RunEndState.Initial)
        {
            SeedRule = SeedRuleOf(effective),
            Spawns = Release.SpawnsOf(state),
        };
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
        var groups = Aggro.GroupsOf(run.Dungeon, state.Tick, run.Content.Packs, run.Content.Layout, run.Content.Patrols);
        // R-DOOR-2: the Hollow Door gates only the forward route (the Triggered path below), never a fight start: a run that
        // begins in the boss pull engages it through a shut door, as main's wipe-is-release rule (#446) has it.
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
        var ctx = new RunStepCtx(run, commands, opts, RunTables.Of(run.Content));
        RunPipeline.Run(ctx);
        return ctx.Result();
    }

    // SEED-PULL-TEACHES (STATE.md D27) and PRESPAWN-AGGRO (BIBLE section 9.1, "Scripts start at engage"): an engaged
    // group's scripts run on the group's own clock from its engage tick, so its first cast and that cast's telegraph fall
    // inside the fight whatever tick the party arrived on. SWING-PHASE (ruling R-307-C item 3): within each group the
    // members' auto-attacks are then spread over the swing period (Scripts.SwingPhased, members in ascending id), so a
    // pack's swings never land in lockstep. Every other actor's script is kept as it is. An engaging member's script is
    // shifted from its content script (baseScripts), never from an already-shifted one, so a pack that leashed home and is
    // pulled again starts its clock afresh (AGGRO-CHASE); on a first engage the two are the same script.
    internal static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> OnEngageClock(
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> baseScripts,
        IReadOnlyList<IReadOnlyList<int>> groups, int startTick, IReadOnlyDictionary<string, AbilityDef> abilities)
    {
        var place = new Dictionary<int, (int Member, int Members)>();
        foreach (var group in groups)
        {
            var scripted = group.Where(scripts.ContainsKey).Distinct().OrderBy(id => id).ToList();
            for (var k = 0; k < scripted.Count; k++)
            {
                place.TryAdd(scripted[k], (k, scripted.Count));
            }
        }
        if (place.Count == 0)
        {
            return scripts;
        }
        var shifted = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        foreach (var (actorId, script) in scripts)
        {
            shifted[actorId] = place.TryGetValue(actorId, out var at)
                ? baseScripts.GetValueOrDefault(actorId, script).Select(s => Scripts.SwingPhased(Scripts.StartingAt(s, startTick), abilities.GetValueOrDefault(s.Cast), at.Member, at.Members)).ToList()
                : script;
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
    internal static (SimState State, EncounterState Encounter) StepLayout(NavLayout layout, Run run, SimState next, EncounterState encounter, Action<SimEvent> emit, IReadOnlyList<int>? openSeats)
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
            actor.Pos = NavFollow.SlideOffObstacles(pre, Movement.ClampToWalkable(pre, actor.Pos, open), layout.Obstacles);
        }

        foreach (var actor in next.Actors.Values)
        {
            if (actor.Kind != ActorKind.Player || !actor.Alive || !run.State.Actors.TryGetValue(actor.Id, out var was))
            {
                continue;
            }
            foreach (var box in layout.AllBoxes)
            {
                if (box.IsRoom && !Movement.Contains(box.Box, was.Pos) && Movement.Contains(box.Box, actor.Pos))
                {
                    emit(new SimEvent(next.Tick, "room_enter", ("actorId", JsValue.Int(actor.Id)), ("room", JsValue.Str(box.Id))));
                }
            }
        }

        // MPLUS-PATROLS: idle patrol members walk their loop to this tick before the aggro check reads their rings.
        Patrols.Walk(next, run.Content.Patrols, encounter);
        // R-DOOR-3 / STATE.md D44 ("no gates, just path to boss with packs"): nothing gates the boss pull. The Hollow Door
        // (BossDoor.Admit) is not applied on the forward route; the Forces bar is a run-completion condition (plan #536 S8).
        var groups = Aggro.GroupsOf(run.Dungeon, next.Tick, run.Content.Packs, layout, run.Content.Patrols);
        if (run.Content.IdleWander)
        {
            // IDLE-WANDER: un-engaged packs and pulls (never a patrol, which keeps its route) walk inside their own ring.
            var patrolIds = (run.Content.Patrols ?? Array.Empty<PatrolPack>()).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            IdleWander.Step(next, groups.Where(g => !patrolIds.Contains(g.Id)).ToList(), encounter, NavGrid.Of(layout));
        }
        // AGGRO-CHASE leash: evaders that reached home reset, and a pack dragged past its leash gives up, before the aggro
        // check (an evading pack engages on nothing).
        encounter = Leash.Step(next, encounter, groups, run.Content.BaseSpeedMmPerTick, emit, openSeats);
        var triggered = Aggro.Triggered(run.State, next, groups, encounter, openSeats);
        encounter = Aggro.Engage(next, encounter, groups, triggered, next.Tick, emit);
        // G2.5 L4 (R-SYNERGY-1): the skip record. A pack the party has walked past, never engaged, is a log fact, not a bug.
        encounter = Skips.Step(next, encounter, groups, layout, run.Dungeon.ForcesGoal, openSeats, emit);
        return (next, encounter);
    }
}
