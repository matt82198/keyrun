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
/// HUD-FORCES (BIBLE s9.2, Mythic+ enemy forces; ledger f-228202e1): the Enemy Forces tally is the sum of the forces
/// value of every enemy that has died in the run, read from the run's own state. Driven through
/// <see cref="RunRoot.CreateRun"/> / <see cref="RunRoot.StepRun"/>: a warden kills enemies with a real cast, nothing is
/// set by hand. The values are the shipped game/content/data/forces.json numbers (choir.melee 40, choir.lieutenant 80);
/// "boss" has no entry and is worth 0, like the Hollow Warden, the Twins and Echo adds.
/// </summary>
public sealed class ForcesTests
{
    private const long Seed = 5151;
    private const int WardenId = 1;
    private const int MeleeA = 101;
    private const int MeleeB = 102;
    private const int Lieutenant = 103;
    private const int BossId = 900;
    private const int Required = 1000;

    private static readonly IReadOnlyDictionary<string, int> Table = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["choir.melee"] = 40,
        ["choir.lieutenant"] = 80,
    };

    private static RunContent Content() => RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
    {
        ["test.kill"] = RunTestKit.Ability("test.kill", new EffectOp[] { new DamageOp(100000, "physical") }),
    });

    private static Run CreateRun() => RunRoot.CreateRun(
        Seed,
        new[] { RunTestKit.Member(WardenId, "warden", ActorRole.Tank, 0, 0, 100000, 100) },
        RunTestKit.OnePull(
            "forces-fixture",
            "choir",
            RunTestKit.Spawn(MeleeA, "choir.melee", 1000, 0, 100),
            RunTestKit.Spawn(MeleeB, "choir.melee", 1200, 0, 100),
            RunTestKit.Spawn(Lieutenant, "choir.lieutenant", 1400, 0, 100),
            RunTestKit.Spawn(BossId, "boss", 1600, 0, 100)),
        Content());

    private static Run Kill(params (int Tick, int Target)[] kills)
    {
        var commands = kills.Select(k => (Command)new CastCommand(k.Tick, WardenId, "test.kill", k.Target)).ToList();
        var last = kills.Length == 0 ? 0 : kills.Max(k => k.Tick);
        return RunTestKit.StepUntil(CreateRun(), last + 2, commands).Run;
    }

    [Fact(DisplayName = "HUD-FORCES: no death banks nothing")]
    public void No_death_banks_nothing()
    {
        var run = RunTestKit.StepUntil(CreateRun(), 5).Run;
        Assert.Equal(0, Forces.Points(run.State, Table));
        var reading = Forces.Read(run.State, Table, Required);
        Assert.Equal(new ForcesReading(0, Required), reading);
        Assert.Equal(0, reading.PercentBp);
        Assert.False(reading.Full);
    }

    [Fact(DisplayName = "HUD-FORCES: each killed enemy banks its forces value, the living bank nothing")]
    public void Each_killed_enemy_banks_its_value()
    {
        var one = Kill((0, MeleeA));
        Assert.False(one.State.Actors[MeleeA].Alive);
        Assert.Equal(40, Forces.Points(one.State, Table));

        var three = Kill((0, MeleeA), (4, MeleeB), (8, Lieutenant));
        Assert.Equal(160, Forces.Points(three.State, Table));
        Assert.Equal(1600, Forces.Read(three.State, Table, Required).PercentBp);
    }

    [Fact(DisplayName = "HUD-FORCES: an enemy with no forces entry (the boss) banks 0, and players never bank")]
    public void Boss_and_players_bank_nothing()
    {
        var run = Kill((0, BossId));
        Assert.False(run.State.Actors[BossId].Alive);
        Assert.Equal(0, Forces.Points(run.State, Table));

        // A dead party member whose class id happens to have a value still banks nothing: forces are enemy deaths.
        var withPlayerEntry = new Dictionary<string, int>(Table, StringComparer.Ordinal) { ["warden"] = 500 };
        var dead = RunTestKit.StepUntil(CreateRun(), 2).Run;
        dead.State.Actors[WardenId].Alive = false;
        Assert.Equal(0, Forces.Points(dead.State, withPlayerEntry));
    }

    [Fact(DisplayName = "HUD-FORCES: a party wipe after the kills never lowers the bar")]
    public void A_wipe_never_lowers_the_bar()
    {
        var run = Kill((0, MeleeA), (4, Lieutenant));
        Assert.Equal(120, Forces.Points(run.State, Table));
        run.State.Actors[WardenId].Alive = false;
        var after = RunTestKit.StepUntil(run, run.State.Tick + RunWiring.WipeGraceTicks + 5).Run;
        Assert.Equal(RunResult.Depleted, after.RunEnd.Result);
        Assert.Equal(120, Forces.Points(after.State, Table));
    }

    [Theory(DisplayName = "HUD-FORCES: the bar reads in WoW's Enemy Forces form, two decimals, full at the requirement")]
    [InlineData(0, 0, false, "Enemy Forces 0.00%", "HUD forces pts=0/1000 bp=0 full=0")]
    [InlineData(160, 1600, false, "Enemy Forces 16.00%", "HUD forces pts=160/1000 bp=1600 full=0")]
    [InlineData(333, 3330, false, "Enemy Forces 33.30%", "HUD forces pts=333/1000 bp=3330 full=0")]
    [InlineData(999, 9990, false, "Enemy Forces 99.90%", "HUD forces pts=999/1000 bp=9990 full=0")]
    [InlineData(1000, 10000, true, "Enemy Forces 100.00% done", "HUD forces pts=1000/1000 bp=10000 full=1")]
    [InlineData(1040, 10400, true, "Enemy Forces 104.00% done", "HUD forces pts=1040/1000 bp=10400 full=1")]
    public void Reading_formats_like_the_Mythic_plus_bar(int points, int bp, bool full, string label, string log)
    {
        var r = new ForcesReading(points, Required);
        Assert.Equal(bp, r.PercentBp);
        Assert.Equal(full, r.Full);
        Assert.Equal(label, r.Label);
        Assert.Equal(log, r.LogLine());
    }

    [Fact(DisplayName = "HUD-FORCES: 1/3 of a 600-point bar floors to 33.33%, and no requirement reads empty")]
    public void Percent_floors_and_a_zero_requirement_is_empty()
    {
        Assert.Equal(3333, new ForcesReading(200, 600).PercentBp);
        Assert.Equal("Enemy Forces 33.33%", new ForcesReading(200, 600).Label);
        Assert.Equal(0, new ForcesReading(50, 0).PercentBp);
        Assert.False(new ForcesReading(50, 0).Full);
    }
}
