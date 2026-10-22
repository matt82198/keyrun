using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// R-MULTIBOSS-1 (event seq 118): a multi-segment dungeon's run ends on the FINAL segment boss's death only. An earlier
/// boss's death emits <c>boss_death</c> and the run continues into the next segment's boss, whose rules arm in its place;
/// the last boss's death ends the run as before. A run armed with one boss (the Hollow Vault) is unchanged: no
/// <c>boss_death</c> event, the run ends on its death. Everything runs through <see cref="RunRoot.StepRun"/>.
/// </summary>
public sealed class MultiBossRunTests
{
    private const int First = 900;
    private const int Second = 910;
    private const int Third = 920;

    /// <summary>A boss with no adds and no Lock: only the run-end rule is under test.</summary>
    private static BossConfig Config(int bossActorId) => new(
        bossActorId,
        Array.Empty<BossTwinSpec>(),
        new BreakBarSource("none", Array.Empty<int>(), 1, 0, 0, Array.Empty<BreakBarOnEmptyEffect>()))
    {
        LockThresholdBp = 0,
        SuppressedBossCasts = Array.Empty<string>(),
        CastLock = false,
    };

    /// <summary>A layout-less dungeon of one boss pull per id, the party standing in strike range of every boss spot.</summary>
    private static Run Build(params int[] bossIds)
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var pulls = bossIds
            .Select((id, i) => new PullDef(i == bossIds.Length - 1 ? "boss" : $"boss-s{i + 1}", new[] { new PullSpawn(id, "hollowWarden.boss", Vec3.Zero, 512, 60000) }))
            .ToList();
        var content = setup.Content with
        {
            Bounds = new Keyrun.Sim.Combat.Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = bossIds.ToDictionary(id => id, _ => (IReadOnlyList<ScriptStep>)Array.Empty<ScriptStep>()),
            BreakBarSources = null,
            KeyLevel = null,
            ParTicks = null,
            Clauses = null,
        };
        var party = new[]
        {
            RunTestKit.Member(BossTestKit.Warden, "warden", ActorRole.Tank, 0, 1500, BossTestKit.PartyHealth, 100),
            RunTestKit.Member(BossTestKit.Chanter, "chanter", ActorRole.Healer, 500, 2000, BossTestKit.PartyHealth, 1),
            RunTestKit.Member(BossTestKit.Cinder, "cinder", ActorRole.Damage, -500, 2000, BossTestKit.PartyHealth, 100),
        };
        var dungeon = new DungeonDef("multi-boss", pulls);
        var run = RunRoot.CreateRun(BossTestKit.Seed, party, dungeon, content);
        // A layout run stands every pull idle in its room from tick 0 (PRESPAWN-AGGRO); this layout-less run does the same for
        // the later bosses, so the pull index (not a spawn) is what moves the fight on.
        foreach (var later in Pulls.SpawnAllIdle(dungeon with { Pulls = pulls.Skip(1).ToList() }, 0))
        {
            run.State.Actors[later.Id] = later;
        }
        return run;
    }

    private static Run Armed(params int[] bossIds)
    {
        var run = Build(bossIds);
        return run with { Encounter = BossRules.ArmAll(run.Encounter, bossIds.Select(Config).ToList()) };
    }

    /// <summary>Puts <paramref name="bossId"/> on 1 hp and has the Warden strike it for <paramref name="ticks"/> ticks.</summary>
    private static (Run Run, List<SimEvent> Events) Kill(Run run, int bossId, int ticks = 60)
    {
        run.State.Actors[bossId].Health = 1;
        var events = new List<SimEvent>();
        for (var i = 0; i < ticks && run.RunEnd.Result is null; i++)
        {
            var stepped = RunRoot.StepRun(run, new Command[] { new CastCommand(run.State.Tick, BossTestKit.Warden, "warden.strike", bossId) });
            run = stepped.Run;
            events.AddRange(stepped.Events);
        }
        return (run, events);
    }

    private static List<long> BossDeaths(IEnumerable<SimEvent> events) =>
        events.Where(e => e.Type == "boss_death").Select(e => ((JsInt)e.Get("actorId")!).Value).ToList();

    [Fact(DisplayName = "Two bosses: the first boss's death emits boss_death and the run continues; the second boss is armed in its place")]
    public void First_boss_death_does_not_end_the_run()
    {
        var (after, events) = Kill(Armed(First, Second), First);

        Assert.Equal(new long[] { First }, BossDeaths(events));
        Assert.Null(after.RunEnd.Result);
        Assert.DoesNotContain(events, e => e.Type == "run_end");
        Assert.False(after.State.Actors[First].Alive);
        Assert.Equal(1, after.Encounter.PullIndex);
        Assert.True(after.State.Actors[Second].Alive);
        Assert.Equal(Second, after.Encounter.Boss!.Config.BossActorId);
    }

    [Fact(DisplayName = "Two bosses: the last boss's death emits boss_death and ends the run timed")]
    public void Last_boss_death_ends_the_run()
    {
        var (mid, first) = Kill(Armed(First, Second), First);
        var (end, second) = Kill(mid, Second);

        Assert.Equal(new long[] { First }, BossDeaths(first));
        Assert.Equal(new long[] { Second }, BossDeaths(second));
        Assert.Equal(RunResult.Timed, end.RunEnd.Result);
        Assert.Single(second, e => e.Type == "run_end");
        Assert.Equal(2, end.Encounter.PullIndex);
    }

    [Fact(DisplayName = "Three bosses: only the third boss's death ends the run, each death emits boss_death in order")]
    public void Only_the_final_boss_ends_a_three_boss_run()
    {
        var run = Armed(First, Second, Third);
        var deaths = new List<long>();
        foreach (var boss in new[] { First, Second })
        {
            var (next, events) = Kill(run, boss);
            run = next;
            deaths.AddRange(BossDeaths(events));
            Assert.Null(run.RunEnd.Result);
        }
        var (end, last) = Kill(run, Third);
        deaths.AddRange(BossDeaths(last));

        Assert.Equal(new long[] { First, Second, Third }, deaths);
        Assert.Equal(RunResult.Timed, end.RunEnd.Result);
    }

    [Fact(DisplayName = "One armed boss (the Hollow Vault): its death ends the run and no boss_death event is emitted")]
    public void A_single_boss_run_is_unchanged()
    {
        var one = Build(First);
        var armed = one with { Encounter = BossRules.Arm(one.Encounter, Config(First)) };

        var (end, events) = Kill(armed, First);

        Assert.Equal(RunResult.Timed, end.RunEnd.Result);
        Assert.Empty(BossDeaths(events));
    }

    [Fact(DisplayName = "Arming a boss list fails closed on a repeated boss actor id and on an empty list")]
    public void Arming_validates_the_list()
    {
        var encounter = Build(First, Second).Encounter;
        Assert.Throws<ArgumentException>(() => BossRules.ArmAll(encounter, new[] { Config(First), Config(First) }));
        Assert.Throws<ArgumentException>(() => BossRules.ArmAll(encounter, Array.Empty<BossConfig>()));
    }

    /// <summary>An enemy worth <see cref="TrashPoints"/> forces, the only forces source in these runs (the boss banks 0).</summary>
    private const int Trash = 950;
    private const int TrashPoints = 100;

    /// <summary>
    /// Adds the forces trash enemy (idle, at the boss spot) and a forces goal of <paramref name="required"/> points to the
    /// run's dungeon (R-FORCES-COMPLETE-1, FINISH-NEEDS-FORCES). The trash is the only forces source; the boss banks 0.
    /// </summary>
    private static Run WithForces(Run run, int required)
    {
        run.State.Actors[Trash] = Keyrun.Sim.Combat.Actors.Create(Trash, ActorKind.Enemy, "trash.melee", ActorRole.None, Vec3.Zero, 100000, 0);
        var table = new Dictionary<string, int>(StringComparer.Ordinal) { ["trash.melee"] = TrashPoints };
        return run with { Dungeon = run.Dungeon with { ForcesGoal = new ForcesGoal(table, required) } };
    }

    [Fact(DisplayName = "R-FORCES-COMPLETE-1: the final boss dies below 100% forces, no run_end; the packs then reach 100% and run_end fires")]
    public void Final_boss_below_full_forces_does_not_end_the_run()
    {
        var run = WithForces(Armed(First), TrashPoints);

        var (boss, bossEvents) = Kill(run, First);

        Assert.False(boss.State.Actors[First].Alive);
        Assert.Null(boss.RunEnd.Result);
        Assert.DoesNotContain(bossEvents, e => e.Type == "run_end");

        var (end, packEvents) = Kill(boss, Trash);

        Assert.Equal(RunResult.Timed, end.RunEnd.Result);
        Assert.Single(packEvents, e => e.Type == "run_end");
    }

    [Fact(DisplayName = "R-FORCES-COMPLETE-1: forces reach 100% before the final boss, and the run still ends on the boss's death")]
    public void Full_forces_before_the_final_boss_ends_the_run_on_the_boss_death()
    {
        var run = WithForces(Armed(First), TrashPoints);

        var (packed, packEvents) = Kill(run, Trash);

        Assert.True(packed.State.Actors[First].Alive);
        Assert.Null(packed.RunEnd.Result);
        Assert.DoesNotContain(packEvents, e => e.Type == "run_end");

        var (end, bossEvents) = Kill(packed, First);

        Assert.Equal(RunResult.Timed, end.RunEnd.Result);
        Assert.Single(bossEvents, e => e.Type == "run_end");
    }
}
