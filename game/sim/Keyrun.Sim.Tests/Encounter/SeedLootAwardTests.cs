using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// SEED-LOOT-AWARD (BIBLE s9 item 3, finding filed by SEED-LOOT-VIEW #188): the run root itself folds
/// <see cref="SeedLoot.Observe"/> over every step's events, so the Seed bonus is part of the run's own result
/// (<see cref="Run.Rewards"/>) and not only recomputed by the view's run-end panel. Driven through
/// <see cref="RunRoot.CreateRun"/> / <see cref="RunRoot.StepRun"/> only: a warden kills a Seed whose onDeath cast
/// (seed.deathBloom, shaped as content/data/abilities/enemies.json has it) drops the floor "seedbloom" Window under the
/// party, then lands a real field Payoff in it (the combo event comes from <see cref="Payoff.TryClosePayoffOnFloor"/>,
/// nothing is injected), or lets it expire. A second, sturdy enemy keeps the pull open until it is killed and the run
/// ends timed.
/// </summary>
public sealed class SeedLootAwardTests
{
    private const long Seed = 4242;
    private const int WardenId = 1;
    private const int SeedActorId = 108;
    private const int DummyId = 200;

    private static RunContent Content()
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            ["test.kill"] = RunTestKit.Ability("test.kill", new EffectOp[] { new DamageOp(100000, "physical") }),
            ["test.strike"] = RunTestKit.Ability("test.strike", new EffectOp[] { new PayoffOp() }, new AbilityShape(ShapeKind.Self)),
            ["seed.deathBloom"] = RunTestKit.Ability(
                "seed.deathBloom",
                new EffectOp[] { new WindowOp("seedbloom", 100, false, "boss") },
                new AbilityShape(ShapeKind.Circle, 2500),
                range: 0),
        };
        var field = new ComboTable();
        field.Set("boss", "warden", new ComboDef("seedbloom-field", Array.Empty<EffectOp>()));
        return RunTestKit.Content(abilities) with
        {
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [SeedActorId] = new[] { ScriptStep.OnDeath("seed.deathBloom") } },
            FieldCombos = field,
        };
    }

    private static Run CreateRun() => RunRoot.CreateRun(
        Seed,
        new[] { RunTestKit.Member(WardenId, "warden", ActorRole.Tank, 0, 0, 100000, 100) },
        RunTestKit.OnePull(
            "seed-award-fixture",
            "seed",
            RunTestKit.Spawn(SeedActorId, "seed", 1000, 0, 100),
            RunTestKit.Spawn(DummyId, "dummy", 9000, 0, 100000)),
        Content());

    /// <summary>Kills the Seed at t0, casts the Payoff at <paramref name="strikeTick"/> (null = never), kills the dummy at
    /// <paramref name="endTick"/>, and steps until the run has ended.</summary>
    private static (Run Run, List<SimEvent> Events) Play(int? strikeTick, int endTick)
    {
        var commands = new List<Command> { new CastCommand(0, WardenId, "test.kill", SeedActorId) };
        if (strikeTick is { } st)
        {
            commands.Add(new CastCommand(st, WardenId, "test.strike"));
        }
        commands.Add(new CastCommand(endTick, WardenId, "test.kill", DummyId));
        var (run, events) = RunTestKit.StepUntil(CreateRun(), endTick + 3, commands);
        Assert.Equal(RunResult.Timed, run.RunEnd.Result);
        return (run, events);
    }

    [Fact(DisplayName = "SEED-LOOT-AWARD: a Combo landed in the Seed Window is awarded on the run's own result")]
    public void A_Combo_in_the_Seed_Window_is_awarded_by_the_run_root()
    {
        var (run, events) = Play(strikeTick: 10, endTick: 20);
        var opened = Assert.Single(events, e => e.Type == "window_opened");
        var ends = (int)((JsInt)opened.Get("endsTick")!).Value;
        var combo = Assert.Single(events, e => e.Type == "combo");
        Assert.Equal("seedbloom", ((JsString)combo.Get("windowId")!).Value);

        var entry = Assert.Single(run.Rewards.BonusLoot);
        Assert.Equal(new BonusLootEntry("seed", "seedbloom", "seedbloom-field", combo.Tick, SeedActorId, WardenId), entry);
        Assert.Equal(ends, run.Rewards.SeedWindowEndsTick);
    }

    [Fact(DisplayName = "SEED-LOOT-AWARD: a Seed Window that expires unused awards nothing")]
    public void An_expired_Seed_Window_awards_nothing()
    {
        // The Payoff lands at t150, after the Window's endsTick (t101): the patch is gone, so no Combo and no award.
        var (run, events) = Play(strikeTick: 150, endTick: 160);
        Assert.Contains(events, e => e.Type == "cast_complete" && e.Tick == 150);
        var opened = Assert.Single(events, e => e.Type == "window_opened");
        Assert.DoesNotContain(events, e => e.Type == "combo");

        Assert.Empty(run.Rewards.BonusLoot);
        Assert.Equal((int)((JsInt)opened.Get("endsTick")!).Value, run.Rewards.SeedWindowEndsTick);
    }

    [Fact(DisplayName = "SEED-LOOT-AWARD: a fresh run starts with no rewards")]
    public void A_fresh_run_has_empty_rewards()
    {
        Assert.Same(RunRewards.Empty, CreateRun().Rewards);
    }
}
