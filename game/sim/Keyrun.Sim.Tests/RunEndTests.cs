using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Port of tests/sim/integration/run-end.test.ts (policy-inside-stepRun, P2): targeting fixup, Slack spend and run-end
/// detection happen inside <see cref="RunRoot.CreateRun"/> / <see cref="RunRoot.StepRun"/> alone; no test calls the
/// <see cref="RunWiring"/> helpers directly.
/// </summary>
public sealed class RunEndTests
{
    private const long Seed = 909090;
    private const int TankId = 1;
    private const int DpsId = 2;
    private const int EnemyId = 10;

    private static Run BuildRun() => RunRoot.CreateRun(
        Seed,
        new[]
        {
            RunTestKit.Member(TankId, "warden", ActorRole.Tank, 0, 0, 500, 100),
            RunTestKit.Member(DpsId, "cinder", ActorRole.Damage, 1000, 0, 250, 100),
        },
        RunTestKit.OnePull("run-end-fixture", "the-only-pull", RunTestKit.Spawn(EnemyId, "dummy", 2000, 0, 1000)),
        RunTestKit.Content(lockoutTicks: 0));

    [Fact]
    public void CreateRun_points_the_enemy_at_the_tank_and_the_damage_player_at_the_enemy_immediately()
    {
        var run = BuildRun();
        Assert.Equal(TankId, run.State.Actors[EnemyId].TargetId);
        Assert.Equal(EnemyId, run.State.Actors[DpsId].TargetId);
    }

    [Fact]
    public void StepRun_keeps_re_resolving_targeting_after_a_real_tick_even_if_a_target_is_cleared()
    {
        var run = BuildRun();
        run.State.Actors[EnemyId].TargetId = null;
        var stepped = RunRoot.StepRun(run, System.Array.Empty<Command>());
        Assert.Equal(TankId, stepped.Run.State.Actors[EnemyId].TargetId);
    }

    [Fact]
    public void StepRun_ends_a_full_wipe_as_depleted_after_the_grace_ticks_with_one_run_end_event()
    {
        var run = BuildRun();
        foreach (var actor in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player))
        {
            actor.Alive = false;
        }

        var (after, events) = RunTestKit.StepUntil(run, RunWiring.WipeGraceTicks + 5);

        Assert.True(after.Encounter.Depleted);
        Assert.Equal(RunResult.Depleted, after.RunEnd.Result);
        var runEnds = events.Where(e => e.Type == "run_end").ToList();
        Assert.Single(runEnds);
        Assert.Equal("{\"result\":\"depleted\",\"tick\":" + after.RunEnd.ResultTick + ",\"type\":\"run_end\"}", runEnds[0].ToCanonicalJson());
        Assert.Equal(RunWiring.WipeGraceTicks + 1, after.RunEnd.ResultTick);

        var again = RunRoot.StepRun(after, System.Array.Empty<Command>());
        Assert.DoesNotContain(again.Events, e => e.Type == "run_end");
    }

    [Fact]
    public void StepRun_ends_timed_once_the_final_pulls_enemies_are_all_dead()
    {
        var run = BuildRun();
        run.State.Actors[EnemyId].Alive = false;
        run.State.Actors[EnemyId].Health = 0;

        var stepped = RunRoot.StepRun(run, System.Array.Empty<Command>());

        Assert.Equal(1, stepped.Run.Encounter.PullIndex);
        Assert.Equal(RunResult.Timed, stepped.Run.RunEnd.Result);
        Assert.Contains(stepped.Events, e => e.Type == "run_end" && e.Get("result") is JsString { Value: "timed" });
    }

    [Fact]
    public void StepRun_ends_abandoned_immediately_on_an_explicit_abandon_request()
    {
        var run = BuildRun();
        var stepped = RunRoot.StepRun(run, System.Array.Empty<Command>(), new StepRunOpts(AbandonRequested: true));

        Assert.Equal(RunResult.Abandoned, stepped.Run.RunEnd.Result);
        var json = stepped.Events.Select(e => e.ToCanonicalJson()).ToList();
        Assert.Contains("{\"result\":\"abandoned\",\"tick\":1,\"type\":\"finish\"}", json);
        Assert.Contains("{\"result\":\"abandoned\",\"tick\":1,\"type\":\"run_end\"}", json);
    }

    [Fact]
    public void StepRun_never_mutates_the_input_run_state()
    {
        var run = BuildRun();
        var before = Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks);
        var stepped = RunRoot.StepRun(run, new Command[] { new MoveCommand(0, TankId, new Dir2(1000, 0), 256) });
        Assert.Equal(before, Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks));
        Assert.Equal(0, run.State.Tick);
        Assert.Equal(1, stepped.Run.State.Tick);
        Assert.NotEqual(run.State.Actors[TankId].Pos, stepped.Run.State.Actors[TankId].Pos);
    }
}
