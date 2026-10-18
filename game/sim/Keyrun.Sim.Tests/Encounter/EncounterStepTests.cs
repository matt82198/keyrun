using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>tests/sim/encounter/index.test.ts ported 1:1 (stepEncounter composes one encounter tick).</summary>
public sealed class EncounterStepTests
{
    private static readonly DungeonDef Choir = new("hollow-vault", new[] { new PullDef("choir", System.Array.Empty<PullSpawn>()) });

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> CleaveScripts = EncounterTestKit.Scripts(1,
        ScriptStep.Loop(100, "boss_cleave") with { Telegraph = new TelegraphSpec("cone", 0) { Angle1024 = 200, Radius = 6000 } });

    [Fact]
    public void Registers_a_telegraph_and_resolves_it_with_leadTicks_0_same_tick_producing_a_hit_list_never_touching_health()
    {
        var state = EncounterTestKit.State(100,
            EncounterTestKit.Actor(1, ActorKind.Enemy, health: 100),
            EncounterTestKit.Actor(2, ActorKind.Player, new Vec3(3000, 0, 0), health: 100));
        var events = new List<SimEvent>();
        var result = EncounterStep.Step(state, EncounterTestKit.BaseEncounter(100, 1000), Choir, CleaveScripts, events.Add);

        Assert.Equal(new Command[] { new CastCommand(100, 1, "boss_cleave", 2) }, result.Commands);
        Assert.Contains(events, e => e.Type == "telegraph");
        var hit = events.Single(e => e.Type == "telegraph_hit");
        Assert.Equal("[2]", hit.Get("actorIds")!.ToCanonicalJson());
        Assert.Empty(result.Encounter.Telegraphs);
        Assert.Equal(100, result.State.Actors[2].Health);
        Assert.Equal(2, result.Encounter.NextTelegraphId);
        // index.ts:120-133: the telegraph event is emitted before its telegraph_hit.
        Assert.Equal(new[] { "telegraph", "telegraph_hit" }, events.Select(e => e.Type));
    }

    [Fact]
    public void Advances_pullIndex_once_the_active_pulls_enemies_are_all_dead()
    {
        var d = new DungeonDef("d", new[] { new PullDef("p0", new[] { new PullSpawn(5, "x", Vec3.Zero, 0, 100) }) });
        var state = EncounterTestKit.State(1, EncounterTestKit.Actor(5, ActorKind.Enemy, alive: false, health: 0));
        var result = EncounterStep.Step(state, EncounterTestKit.BaseEncounter(100, 1000), d, new Dictionary<int, IReadOnlyList<ScriptStep>>(), _ => { });
        Assert.Equal(1, result.Encounter.PullIndex);
    }

    [Fact]
    public void Threads_firedOnDeath_so_an_onDeath_step_fires_exactly_once_across_ticks()
    {
        var scripts = EncounterTestKit.Scripts(9, ScriptStep.OnDeath("seed.deathBloom"));
        var encounter = EncounterTestKit.BaseEncounter(100, 1000);
        var fired = 0;
        for (var t = 10; t < 15; t++)
        {
            var state = EncounterTestKit.State(t, EncounterTestKit.Actor(9, ActorKind.Enemy, alive: false, health: 0));
            var result = EncounterStep.Step(state, encounter, Choir, scripts, _ => { });
            fired += result.Commands.Count;
            encounter = result.Encounter;
        }
        Assert.Equal(1, fired);
        Assert.Equal(new[] { "seed.deathBloom" }, encounter.FiredOnDeath[9]);
    }

    [Fact]
    public void Tide_drifts_a_pending_telegraph_before_it_resolves()
    {
        var tide = new ClauseDef("tide", "Tide", ClauseKind.Tide, new Dictionary<string, long> { ["driftMmPerTick"] = 50 }, "");
        var encounter = EncounterTestKit.BaseEncounter(100, 1000) with
        {
            Rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(1), tide),
            Telegraphs = new[] { new Telegraph(4, 1, "wave", "circle", Vec3.Zero, 0, 101) { Radius = 1000 } },
            NextTelegraphId = 5,
        };
        var state = EncounterTestKit.State(100, EncounterTestKit.Actor(2, ActorKind.Player, new Vec3(1040, 0, 0)));
        var r1 = EncounterStep.Step(state, encounter, Choir, new Dictionary<int, IReadOnlyList<ScriptStep>>(), _ => { });
        Assert.Equal(50, Assert.Single(r1.Encounter.Telegraphs).Origin.X);
        var events = new List<SimEvent>();
        var r2 = EncounterStep.Step(EncounterTestKit.State(101, state.Actors.Values.ToArray()), r1.Encounter, Choir, new Dictionary<int, IReadOnlyList<ScriptStep>>(), events.Add);
        Assert.Empty(r2.Encounter.Telegraphs);
        Assert.Equal(EncounterTestKit.Ev("{\"tick\":101,\"type\":\"telegraph_hit\",\"telegraphId\":4,\"actorIds\":[2]}"), Assert.Single(events).ToCanonicalJson());
    }
}
