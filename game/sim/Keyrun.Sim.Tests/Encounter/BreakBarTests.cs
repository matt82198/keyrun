using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// tests/sim/encounter/breakbar.test.ts and break-bar-wiring.test.ts ported 1:1 (the shared Break bar of the
/// Sundered Twins, BIBLE s9, drained by interrupt/control-tagged casts; stepEncounter's lazy creation hook).
/// </summary>
public sealed class BreakBarTests
{
    private static readonly BreakBarSource Twins = new(
        "twinsBreak", new[] { 10, 11 }, 100, 34, 20, new[] { new BreakBarOnEmptyEffect("stunned", 100, 1) });

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>?> Tags = new Dictionary<string, IReadOnlyList<string>?>
    {
        ["warden.shieldBash"] = new[] { "interrupt", "control" },
        ["cinder.quench"] = new[] { "interrupt" },
        ["cinder.coldSnap"] = new[] { "control" },
        ["warden.strike"] = null,
    };

    private static readonly IReadOnlyDictionary<int, ActorState> NoActors = new Dictionary<int, ActorState>();

    [Fact]
    public void Drains_only_on_control_interrupt_tagged_inputs_for_the_current_tick()
    {
        var bar = BreakBar.Create(100, new[] { 10, 11 });
        var emitted = new List<SimEvent>();
        var inputs = new[]
        {
            new BreakBarInput(5, 10, BreakBarTag.Control, 30),
            new BreakBarInput(5, 11, BreakBarTag.Interrupt, 20),
            new BreakBarInput(6, 10, BreakBarTag.Control, 999),
        };
        bar = BreakBar.Drain(bar, "twins", inputs, 5, emitted.Add);
        Assert.Equal(50, bar.Current);
        Assert.Empty(emitted);
    }

    [Fact]
    public void Emits_stagger_exactly_once_at_empty_then_locks_the_bar_until_the_cooldown_elapses_then_resets_to_max()
    {
        var bar = BreakBar.Create(100, new[] { 10, 11 });
        var emitted = new List<SimEvent>();
        bar = BreakBar.Drain(bar, "twins", new[] { new BreakBarInput(1, 10, BreakBarTag.Control, 60) }, 1, emitted.Add);
        bar = BreakBar.Drain(bar, "twins", new[] { new BreakBarInput(2, 10, BreakBarTag.Control, 60) }, 2, emitted.Add);
        Assert.Equal(0, bar.Current);
        Assert.Equal(
            new[] { EncounterTestKit.Ev("{\"tick\":2,\"type\":\"stagger\",\"barId\":\"twins\",\"actorIds\":[10,11]}") },
            EncounterTestKit.Json(emitted));
        var readyTick = bar.CooldownReadyTick;
        Assert.Equal(2 + BreakBar.ResetCooldownTicks, readyTick);

        bar = BreakBar.Drain(bar, "twins", new[] { new BreakBarInput(3, 10, BreakBarTag.Control, 999) }, 3, emitted.Add);
        Assert.Single(emitted);
        Assert.Equal(0, bar.Current);

        bar = BreakBar.TickReset(bar, readyTick - 1);
        Assert.Equal(0, bar.Current);
        bar = BreakBar.TickReset(bar, readyTick);
        Assert.Equal(100, bar.Current);
        Assert.Equal(0, bar.CooldownReadyTick);
    }

    [Fact]
    public void Drains_on_an_interrupt_and_control_tagged_ability_targeting_a_bar_actor_preferring_interrupt()
    {
        var cmds = new Command[] { new CastCommand(5, 1, "warden.shieldBash", 10) };
        Assert.Equal(new[] { new BreakBarInput(5, 1, BreakBarTag.Interrupt, 34) }, BreakBar.DeriveInputs(cmds, NoActors, Tags, Twins));
    }

    [Fact]
    public void Drains_on_a_control_only_ability_using_drainPerControl()
    {
        var cmds = new Command[] { new CastCommand(5, 1, "cinder.coldSnap", 11) };
        Assert.Equal(new[] { new BreakBarInput(5, 1, BreakBarTag.Control, 20) }, BreakBar.DeriveInputs(cmds, NoActors, Tags, Twins));
    }

    [Fact]
    public void Falls_back_to_the_casters_current_targetId_when_the_command_carries_none()
    {
        var caster = EncounterTestKit.Actor(1, ActorKind.Player);
        caster.TargetId = 10;
        var actors = new Dictionary<int, ActorState> { [1] = caster };
        var cmds = new Command[] { new CastCommand(5, 1, "cinder.quench") };
        Assert.Equal(new[] { new BreakBarInput(5, 1, BreakBarTag.Interrupt, 34) }, BreakBar.DeriveInputs(cmds, actors, Tags, Twins));
    }

    [Fact]
    public void Ignores_non_cast_commands_untagged_abilities_and_casts_aimed_off_the_bar()
    {
        var cmds = new Command[]
        {
            new StopCastCommand(5, 1),
            new CastCommand(5, 2, "warden.strike", 10),
            new CastCommand(5, 3, "cinder.quench", 999),
        };
        Assert.Empty(BreakBar.DeriveInputs(cmds, NoActors, Tags, Twins));
    }

    [Fact]
    public void Returns_one_patch_per_actorId_x_onEmpty_entry_only_when_justStaggered()
    {
        Assert.Empty(BreakBar.EffectPatches(Twins, false));
        Assert.Equal(
            new[] { new BreakBarEffectPatch(10, "stunned", 100, 1), new BreakBarEffectPatch(11, "stunned", 100, 1) },
            BreakBar.EffectPatches(Twins, true));
    }

    private static ActorState WiringActor(int id) =>
        EncounterTestKit.Actor(id, id < 10 ? ActorKind.Player : ActorKind.Enemy, health: 100);

    private static readonly DungeonDef BossDungeon = new("hollow-vault", new[] { new PullDef("boss", Array.Empty<PullSpawn>()) });

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> NoScripts = new Dictionary<int, IReadOnlyList<ScriptStep>>();

    [Fact]
    public void Does_nothing_when_breakBarWiring_is_omitted_unchanged_behaviour_for_existing_callers()
    {
        var state = EncounterTestKit.State(0, WiringActor(1), WiringActor(10), WiringActor(11));
        var result = EncounterStep.Step(state, EncounterTestKit.BaseEncounter(100, 1000), BossDungeon, NoScripts, _ => { });
        Assert.Equal(0, result.Encounter.BreakBars.Count);
        Assert.Empty(result.EffectPatches);
    }

    [Fact]
    public void Lazily_creates_the_bar_only_once_every_actorId_is_alive_then_drains_it_and_reports_stagger_effect_patches_on_empty()
    {
        var events = new List<SimEvent>();
        BreakBarWiring Wiring(params Command[] commands) => new(new[] { Twins }, commands, Tags);

        var state = EncounterTestKit.State(0, WiringActor(10));
        var result = EncounterStep.Step(state, EncounterTestKit.BaseEncounter(100, 1000), BossDungeon, NoScripts, events.Add, Wiring());
        Assert.False(result.Encounter.BreakBars.ContainsKey("twinsBreak"));

        state = EncounterTestKit.State(1, WiringActor(1), WiringActor(10), WiringActor(11));
        result = EncounterStep.Step(state, result.Encounter, BossDungeon, NoScripts, events.Add, Wiring(new CastCommand(1, 1, "warden.shieldBash", 10)));
        Assert.Equal(66, result.Encounter.BreakBars["twinsBreak"].Current);
        Assert.Empty(result.EffectPatches);

        state = EncounterTestKit.State(2, state.Actors.Values.ToArray());
        result = EncounterStep.Step(state, result.Encounter, BossDungeon, NoScripts, events.Add, Wiring(new CastCommand(2, 1, "warden.shieldBash", 11)));
        Assert.Equal(32, result.Encounter.BreakBars["twinsBreak"].Current);

        state = EncounterTestKit.State(3, state.Actors.Values.ToArray());
        result = EncounterStep.Step(state, result.Encounter, BossDungeon, NoScripts, events.Add, Wiring(new CastCommand(3, 1, "warden.shieldBash", 10)));
        Assert.Equal(0, result.Encounter.BreakBars["twinsBreak"].Current);
        Assert.Equal(
            new[] { new BreakBarEffectPatch(10, "stunned", 100, 1), new BreakBarEffectPatch(11, "stunned", 100, 1) },
            result.EffectPatches);
        Assert.Contains(events, e => e.Type == "stagger" && e.Get("barId") is JsString { Value: "twinsBreak" });

        state = EncounterTestKit.State(4, state.Actors.Values.ToArray());
        result = EncounterStep.Step(state, result.Encounter, BossDungeon, NoScripts, events.Add, Wiring(new CastCommand(4, 1, "warden.shieldBash", 10)));
        Assert.Empty(result.EffectPatches);
    }
}
