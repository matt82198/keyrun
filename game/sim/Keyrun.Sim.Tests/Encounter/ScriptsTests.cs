using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>tests/sim/encounter/scripts.test.ts ported 1:1 (enemy scripts as data-driven state machines).</summary>
public sealed class ScriptsTests
{
    private static SimState StateAt(int tick) => EncounterTestKit.State(tick, EncounterTestKit.Actor(1, ActorKind.Enemy));

    private static ActorState Player(int id, Vec3 pos) => EncounterTestKit.Actor(id, ActorKind.Player, pos);

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> CleaveScripts = EncounterTestKit.Scripts(1,
        ScriptStep.Loop(100, "boss_cleave") with { Telegraph = new TelegraphSpec("cone", 40) { Angle1024 = 170, Radius = 6000 } });

    private static IReadOnlyList<Command> Commands(SimState s, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts) =>
        Scripts.StepEnemyScripts(s, scripts).Commands;

    [Fact]
    public void Fires_the_loop_cast_at_every_multiple_of_every_never_at_tick_0()
    {
        Assert.Empty(Commands(StateAt(0), CleaveScripts));
        Assert.Empty(Commands(StateAt(99), CleaveScripts));
        Assert.Equal(new Command[] { new CastCommand(100, 1, "boss_cleave") }, Commands(StateAt(100), CleaveScripts));
        Assert.Single(Commands(StateAt(200), CleaveScripts));
    }

    [Fact]
    public void Emits_the_telegraph_exactly_leadTicks_before_the_cast_it_precedes_with_matching_resolvesTick()
    {
        var before = Scripts.StepEnemyScripts(StateAt(60), CleaveScripts);
        var tg = Assert.Single(before.TelegraphEvents);
        Assert.Equal(
            EncounterTestKit.Ev("{\"tick\":60,\"type\":\"telegraph\",\"actorId\":1,\"cast\":\"boss_cleave\",\"shape\":\"cone\",\"leadTicks\":40,\"resolvesTick\":100,\"origin\":{\"x\":0,\"y\":0,\"z\":0},\"yaw\":0,\"radius\":6000,\"angle1024\":170}"),
            tg.ToCanonicalJson());
        Assert.Empty(before.Commands);

        var second = Scripts.StepEnemyScripts(StateAt(160), CleaveScripts);
        Assert.Equal(200, ((JsInt)Assert.Single(second.TelegraphEvents).Get("resolvesTick")!).Value);

        Assert.Empty(Scripts.StepEnemyScripts(StateAt(59), CleaveScripts).TelegraphEvents);
        Assert.Empty(Scripts.StepEnemyScripts(StateAt(61), CleaveScripts).TelegraphEvents);
    }

    [Fact]
    public void Never_casts_for_a_dead_or_missing_actor()
    {
        var dead = StateAt(100);
        dead.Actors[1].Alive = false;
        Assert.Empty(Commands(dead, CleaveScripts));
        Assert.Empty(Commands(EncounterTestKit.State(100), CleaveScripts));
    }

    [Fact]
    public void Fires_a_one_shot_numeric_at_step_exactly_once_at_that_tick()
    {
        var s = EncounterTestKit.Scripts(1, ScriptStep.At(250, "intro_roar"));
        Assert.Empty(Commands(StateAt(249), s));
        Assert.Single(Commands(StateAt(250), s));
        Assert.Empty(Commands(StateAt(251), s));
    }

    [Fact]
    public void Fires_a_numeric_at_plus_every_step_at_at_at_plus_every_at_plus_2_every()
    {
        var s = EncounterTestKit.Scripts(1, ScriptStep.At(600, "pair.stackCall", every: 400));
        Assert.Empty(Commands(StateAt(200), s));
        Assert.Empty(Commands(StateAt(400), s));
        Assert.Equal(new Command[] { new CastCommand(600, 1, "pair.stackCall") }, Commands(StateAt(600), s));
        Assert.Single(Commands(StateAt(1000), s));
        Assert.Single(Commands(StateAt(1400), s));
        Assert.Empty(Commands(StateAt(800), s));
    }

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> DeathScripts =
        EncounterTestKit.Scripts(1, ScriptStep.OnDeath("seed.deathBloom"));

    [Fact]
    public void OnDeath_never_fires_while_the_actor_is_alive() => Assert.Empty(Commands(StateAt(500), DeathScripts));

    [Fact]
    public void OnDeath_fires_once_the_actor_is_dead()
    {
        var dead = StateAt(500);
        dead.Actors[1].Alive = false;
        var result = Scripts.StepEnemyScripts(dead, DeathScripts);
        Assert.Equal(new Command[] { new CastCommand(500, 1, "seed.deathBloom") }, result.Commands);
        Assert.Equal(new[] { "seed.deathBloom" }, result.FiredOnDeath[1]);
    }

    [Fact]
    public void OnDeath_does_not_re_fire_once_firedOnDeath_records_it_the_callers_fire_once_contract()
    {
        var dead = StateAt(600);
        dead.Actors[1].Alive = false;
        var fired = new Dictionary<int, IReadOnlyList<string>> { [1] = new[] { "seed.deathBloom" } };
        var result = Scripts.StepEnemyScripts(dead, DeathScripts, fired);
        Assert.Empty(result.Commands);
        Assert.Equal(new[] { "seed.deathBloom" }, result.FiredOnDeath[1]);
    }

    [Fact]
    public void OnDeath_emits_the_telegraph_on_the_same_tick_the_onDeath_cast_fires_resolving_leadTicks_later()
    {
        var dead = StateAt(700);
        dead.Actors[1].Alive = false;
        var s = EncounterTestKit.Scripts(1, ScriptStep.OnDeath("seed.deathBloom") with { Telegraph = new TelegraphSpec("circle", 20) { Radius = 2000 } });
        var e = Assert.Single(Scripts.StepEnemyScripts(dead, s).TelegraphEvents);
        Assert.Equal(700, e.Tick);
        Assert.Equal("telegraph", e.Type);
        Assert.Equal("seed.deathBloom", ((JsString)e.Get("cast")!).Value);
        Assert.Equal(720, ((JsInt)e.Get("resolvesTick")!).Value);
    }

    [Fact]
    public void OnDeath_leaves_loop_numeric_steps_for_the_same_actor_unaffected_by_a_dead_actors_onDeath_step()
    {
        var dead = StateAt(500);
        dead.Actors[1].Alive = false;
        var s = EncounterTestKit.Scripts(1, ScriptStep.Loop(100, "seed.slam"), ScriptStep.OnDeath("seed.deathBloom"));
        Assert.Equal(new Command[] { new CastCommand(500, 1, "seed.deathBloom") }, Scripts.StepEnemyScripts(dead, s).Commands);
    }

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> MeleeScript = EncounterTestKit.Scripts(1, ScriptStep.Loop(40, "melee"));

    private static readonly IReadOnlyDictionary<string, ScriptAbility> Melee = new Dictionary<string, ScriptAbility>
    {
        ["melee"] = new("melee", 3000, "target", false),
    };

    [Fact]
    public void Targeting_with_no_abilities_registry_still_resolves_a_hostile_default_target_when_a_player_is_present()
    {
        var state = StateAt(40);
        state.Actors[2] = Player(2, new Vec3(0, 0, 500));
        Assert.Equal(new Command[] { new CastCommand(40, 1, "melee", 2) }, Commands(state, MeleeScript));
    }

    [Fact]
    public void Targeting_with_an_abilities_registry_a_nothing_in_range_target_shaped_cast_is_skipped_entirely()
    {
        var state = StateAt(40);
        state.Actors[2] = Player(2, new Vec3(0, 0, 9000));
        var result = Scripts.StepEnemyScripts(state, MeleeScript, null, Melee);
        Assert.DoesNotContain(result.Commands, c => c is CastCommand);
        Assert.Equal(new Command[] { new MoveCommand(40, 1, new Dir2(0, 1000), 256) }, result.Commands);
    }

    [Fact]
    public void Targeting_a_blocked_onDeath_step_emits_neither_the_cast_command_nor_its_telegraph()
    {
        var state = StateAt(500);
        state.Actors[1].Alive = false;
        state.Actors[2] = Player(2, new Vec3(0, 0, 9000));
        var s = EncounterTestKit.Scripts(1, ScriptStep.OnDeath("melee") with { Telegraph = new TelegraphSpec("circle", 20) { Radius = 2000 } });
        var result = Scripts.StepEnemyScripts(state, s, null, Melee);
        Assert.Empty(result.Commands);
        Assert.Empty(result.TelegraphEvents);
        Assert.Equal(new[] { "melee" }, result.FiredOnDeath[1]);
    }

    [Fact]
    public void Targeting_a_steps_target_self_override_reaches_the_emitted_command_via_stepEnemyScripts()
    {
        var state = StateAt(40);
        state.Actors[2] = Player(2, new Vec3(0, 0, 500));
        var s = EncounterTestKit.Scripts(1, ScriptStep.Loop(40, "melee") with { Target = ScriptTargetOverride.Self });
        Assert.Equal(new Command[] { new CastCommand(40, 1, "melee", 1) }, Commands(state, s));
    }

    [Fact]
    public void Scripts_run_in_ascending_actor_id_order_and_steps_in_script_order()
    {
        var state = EncounterTestKit.State(100, EncounterTestKit.Actor(7, ActorKind.Enemy), EncounterTestKit.Actor(3, ActorKind.Enemy));
        var scripts = new Dictionary<int, IReadOnlyList<ScriptStep>>
        {
            [7] = new[] { ScriptStep.Loop(50, "b"), ScriptStep.Loop(100, "a") },
            [3] = new[] { ScriptStep.Loop(100, "c") },
        };
        var casts = Scripts.StepEnemyScripts(state, scripts).Commands.Cast<CastCommand>().Select(c => $"{c.ActorId}:{c.AbilityId}");
        Assert.Equal(new[] { "3:c", "7:b", "7:a" }, casts);
    }
}
