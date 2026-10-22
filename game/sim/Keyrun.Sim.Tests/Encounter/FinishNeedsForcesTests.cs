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
/// FINISH-NEEDS-FORCES (ledger f-8c50a787; Matt 2026-10-20 "defer to wow" in the dungeon loop): as in WoW Mythic+, a key
/// completes only when Enemy Forces reach 100% AND the last boss is dead, in either order. Killing the boss short of the
/// count leaves the run open with the clock running; the count filling later finishes it on that tick; the result is
/// timed only when that tick is within par. A dungeon with no <see cref="DungeonDef.ForcesGoal"/> (every oracle run) keeps
/// the frozen rule: the run finishes when the last pull clears.
/// </summary>
public sealed class FinishNeedsForcesTests
{
    private const long Seed = 5151;
    private const int WardenId = 1;
    private const int MeleeA = 101;
    private const int MeleeB = 102;
    private const int Lieutenant = 103;
    private const int BossId = 900;
    private const int Par = 1000;

    // The shipped forces.json values; "boss" has no entry and banks 0. 160 points = all three trash = 100%.
    private static readonly IReadOnlyDictionary<string, int> Table = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["choir.melee"] = 40,
        ["choir.lieutenant"] = 80,
    };

    private static readonly ForcesGoal Goal = new(Table, 160);

    private static RunContent Content() => RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
    {
        ["test.kill"] = RunTestKit.Ability("test.kill", new EffectOp[] { new DamageOp(100000, "physical") }),
    });

    private static DungeonDef Dungeon(ForcesGoal? goal) => RunTestKit.OnePull(
        "forces-finish",
        "choir",
        RunTestKit.Spawn(MeleeA, "choir.melee", 1000, 0, 100),
        RunTestKit.Spawn(MeleeB, "choir.melee", 1200, 0, 100),
        RunTestKit.Spawn(Lieutenant, "choir.lieutenant", 1400, 0, 100),
        RunTestKit.Spawn(BossId, "boss", 1600, 0, 100)) with { ForcesGoal = goal };

    private static Run CreateRun(ForcesGoal? goal) => RunRoot.CreateRun(
        Seed,
        new[] { RunTestKit.Member(WardenId, "warden", ActorRole.Tank, 0, 0, 100000, 100) },
        Dungeon(goal),
        Content());

    /// <summary>One run-end check at <paramref name="tick"/> with the given actors dead and the boss pull cleared or not.</summary>
    private static (RunEndState End, List<SimEvent> Events) Check(RunEndState prev, ForcesGoal? goal, int tick, bool bossCleared, params int[] dead)
    {
        var run = CreateRun(goal);
        var state = run.State.Clone();
        foreach (var id in dead)
        {
            state.Actors[id].Alive = false;
        }
        var encounter = run.Encounter with { PullIndex = bossCleared ? run.Dungeon.Pulls.Count : 0, ParTicks = Par };
        var events = new List<SimEvent>();
        var end = RunWiring.CheckRunEnd(prev, state, encounter, run.Dungeon, tick, false, events.Add, release: true);
        return (end, events);
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: the boss dead with forces short of 100% does not finish the key")]
    public void Boss_dead_with_forces_short_does_not_finish()
    {
        var (end, events) = Check(RunEndState.Initial, Goal, 500, bossCleared: true, BossId, MeleeA);

        Assert.Null(end.Result);
        Assert.Null(end.ResultTick);
        Assert.DoesNotContain(events, e => e.Type == "finish");
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: forces reaching 100% after the boss died finish the key on that tick")]
    public void Forces_full_after_boss_death_finishes_on_that_tick()
    {
        var (open, _) = Check(RunEndState.Initial, Goal, 500, bossCleared: true, BossId, MeleeA);
        Assert.Null(open.Result);

        var (end, events) = Check(open, Goal, 650, bossCleared: true, BossId, MeleeA, MeleeB, Lieutenant);

        Assert.Equal(RunResult.Timed, end.Result);
        Assert.Equal(650, end.ResultTick);
        Assert.Equal(650, Assert.Single(events, e => e.Type == "finish").Tick);
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: forces at 100% before the boss dies finish the key on the boss's death")]
    public void Forces_full_before_boss_death_finishes_on_boss_death()
    {
        var (open, early) = Check(RunEndState.Initial, Goal, 400, bossCleared: false, MeleeA, MeleeB, Lieutenant);
        Assert.Null(open.Result);
        Assert.DoesNotContain(early, e => e.Type == "finish");

        var (end, events) = Check(open, Goal, 720, bossCleared: true, MeleeA, MeleeB, Lieutenant, BossId);

        Assert.Equal(RunResult.Timed, end.Result);
        Assert.Equal(720, end.ResultTick);
        Assert.Equal(720, Assert.Single(events, e => e.Type == "finish").Tick);
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: the key is timed only when the finish tick is within par")]
    public void Timed_only_when_finish_tick_within_par()
    {
        var (atPar, _) = Check(RunEndState.Initial, Goal, Par, bossCleared: true, BossId, MeleeA, MeleeB, Lieutenant);
        var (pastPar, _) = Check(RunEndState.Initial, Goal, Par + 1, bossCleared: true, BossId, MeleeA, MeleeB, Lieutenant);

        Assert.Equal(RunResult.Timed, atPar.Result);
        Assert.Equal(RunResult.Depleted, pastPar.Result);
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: a run left open on forces still ends depleted at the timer cap")]
    public void Open_run_ends_depleted_at_the_cap()
    {
        var run = CreateRun(Goal);
        var encounter = run.Encounter with { ParTicks = Par };
        var cap = (int)Timer.CapTicks(encounter);

        var (beforeCap, _) = Check(RunEndState.Initial, Goal, cap - 1, bossCleared: true, BossId);
        var (atCap, _) = Check(RunEndState.Initial, Goal, cap, bossCleared: true, BossId);

        Assert.Null(beforeCap.Result);
        Assert.Equal(RunResult.Depleted, atCap.Result);
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: a dungeon with no forces goal (every oracle run) finishes when the last pull clears")]
    public void No_goal_keeps_the_frozen_rule()
    {
        var (end, _) = Check(RunEndState.Initial, null, 500, bossCleared: true, BossId);

        Assert.Equal(RunResult.Timed, end.Result);
        Assert.Equal(500, end.ResultTick);
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: through StepRun, a goal the run cannot reach keeps it open after every enemy dies")]
    public void Step_run_reads_the_dungeon_goal()
    {
        var kills = new (int Tick, int Target)[] { (0, MeleeA), (4, MeleeB), (8, Lieutenant), (12, BossId) };
        var commands = kills.Select(k => (Command)new CastCommand(k.Tick, WardenId, "test.kill", k.Target)).ToList();

        var reachable = RunTestKit.StepUntil(CreateRun(Goal), 40, commands).Run;
        var unreachable = RunTestKit.StepUntil(CreateRun(new ForcesGoal(Table, 200)), 40, commands).Run;

        Assert.Equal(RunResult.Timed, reachable.RunEnd.Result);
        Assert.True(unreachable.Encounter.PullIndex >= unreachable.Dungeon.Pulls.Count, "every pull cleared");
        Assert.Null(unreachable.RunEnd.Result);
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: a forces goal must require a positive count (fail closed)")]
    public void Goal_requires_a_positive_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ForcesGoal(Table, 0));
        Assert.True(Goal.Met(Full()));
        Assert.False(Goal.Met(CreateRun(Goal).State));
    }

    private static SimState Full()
    {
        var state = CreateRun(Goal).State.Clone();
        foreach (var id in new[] { MeleeA, MeleeB, Lieutenant })
        {
            state.Actors[id].Alive = false;
        }
        return state;
    }
}
