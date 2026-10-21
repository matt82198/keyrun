using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// HOLDER-LOCK x the autorun Warden (STATE.md D30, ruling R-307-C item 2; Matt on #306: "Once threat is initialized, it
/// cant be taken except for abilities"). A party member who body-pulls holds the enemy, and only a taunt moves it, so
/// the autorun <see cref="ScriptedWarden"/> taunts back the nearest engaged enemy another living player holds when
/// Taunt is ready and the enemy is in its range, as the bot tank does (TankEngage.PickLooseEnemy) and as a WoW tank
/// does. Built on the run SimDriver builds (key 2, seed 1, bots on), with one Pair enemy placed beside the Warden.
/// </summary>
public sealed class ScriptedWardenTauntTests
{
    private const string Taunt = "warden.taunt";
    private const int WardenId = 1;
    private const int CarelessSeat = 3;

    private static (Run Run, List<int> PairIds) Build()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var created = RunRoot.CreateRun(1, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var pairIds = setup.Dungeon.Pulls.Single(p => p.Id == "pair").Spawns.Select(s => s.ActorId).ToList();
        return (run, pairIds);
    }

    /// <summary>Clones the run's state with the given Pair enemies engaged beside the Warden, each held by its holder.</summary>
    private static Run Engage(Run run, params (int EnemyId, int HolderId, long OffsetXMm)[] enemies)
    {
        var state = run.State.Clone();
        var warden = state.Actors[WardenId];
        foreach (var (enemyId, holderId, offsetX) in enemies)
        {
            var e = state.Actors[enemyId];
            e.Idle = false;
            e.Alive = true;
            e.HolderId = holderId;
            e.Pos = new Vec3(warden.Pos.X + offsetX, warden.Pos.Y, warden.Pos.Z);
        }
        return run with { State = state };
    }

    private static List<CastCommand> TauntsFrom(Run run)
    {
        var commands = new List<Command>();
        new ScriptedWarden().Sample(run.State.Tick, run, commands);
        return commands.OfType<CastCommand>().Where(c => c.AbilityId == Taunt).ToList();
    }

    [Fact(DisplayName = "The autorun Warden taunts an engaged enemy another living player holds, in Taunt range")]
    public void Warden_taunts_an_enemy_held_by_another_player()
    {
        var (run, pair) = Build();
        var engaged = Engage(run, (pair[0], CarelessSeat, 1500));
        var taunts = TauntsFrom(engaged);
        var taunt = Assert.Single(taunts);
        Assert.Equal(WardenId, taunt.ActorId);
        Assert.Equal(pair[0], taunt.TargetId);
    }

    [Fact(DisplayName = "The autorun Warden taunts the nearest loose enemy, one per press (Taunt stays single-target)")]
    public void Warden_taunts_the_nearest_loose_enemy_only()
    {
        var (run, pair) = Build();
        var engaged = Engage(run, (pair[0], CarelessSeat, 2500), (pair[1], CarelessSeat, 1200), (pair[2], CarelessSeat, 2000));
        var taunt = Assert.Single(TauntsFrom(engaged));
        Assert.Equal(pair[1], taunt.TargetId);
    }

    [Fact(DisplayName = "The autorun Warden never taunts an enemy he already holds")]
    public void Warden_never_taunts_his_own_enemy()
    {
        var (run, pair) = Build();
        Assert.Empty(TauntsFrom(Engage(run, (pair[0], WardenId, 1500))));
    }

    [Fact(DisplayName = "The autorun Warden does not taunt a loose enemy beyond Taunt's range")]
    public void Warden_does_not_taunt_out_of_range()
    {
        var (run, pair) = Build();
        var range = run.Content.Abilities[Taunt].Range;
        Assert.Empty(TauntsFrom(Engage(run, (pair[0], CarelessSeat, range + 2000))));
    }

    // Steps the run with the autorun Warden's commands each tick until the enemy's holder is the Warden; the tick it
    // flipped, or null within the window.
    private static int? StepUntilWardenHolds(Run run, int enemyId, int window)
    {
        var warden = new ScriptedWarden();
        for (var i = 0; i < window; i++)
        {
            var commands = new List<Command>();
            warden.Sample(run.State.Tick, run, commands);
            run = RunRoot.StepRun(run, commands).Run;
            if (run.State.Actors[enemyId].HolderId == WardenId)
            {
                return run.State.Tick;
            }
        }
        return null;
    }

    [Fact(DisplayName = "Through the run loop the autorun Warden takes back a loose enemy within one second of Taunt coming ready (f-2e1fc45a)")]
    public void Warden_takes_back_a_loose_enemy_within_his_reaction_window()
    {
        var (run, pair) = Build();
        var engaged = Engage(run, (pair[0], CarelessSeat, 1500));
        // Taunt is on cooldown for 10 more ticks when the Cinder body-pulls, so the window opens when it comes ready.
        const int CooldownLeft = 10;
        var state = engaged.State.Clone();
        var readyAt = state.Tick + CooldownLeft;
        state.Actors[WardenId].Cooldowns[Taunt] = readyAt;
        engaged = engaged with { State = state };
        Assert.Equal(CarelessSeat, engaged.State.Actors[pair[0]].HolderId);

        // Reaction window: one second (Tick.TickHz ticks) after Taunt is ready; a WoW tank taunts a loose add on sight.
        var flipped = StepUntilWardenHolds(engaged, pair[0], CooldownLeft + Tick.TickHz);
        Assert.True(flipped is not null, $"the Warden never took enemy {pair[0]} back from seat {CarelessSeat} within {CooldownLeft + Tick.TickHz} ticks");
        Assert.InRange(flipped!.Value, readyAt, readyAt + Tick.TickHz);
    }

    [Fact(DisplayName = "The autorun Warden does not press Taunt while it is on cooldown")]
    public void Warden_does_not_taunt_on_cooldown()
    {
        var (run, pair) = Build();
        var engaged = Engage(run, (pair[0], CarelessSeat, 1500));
        var state = engaged.State.Clone();
        var warden = state.Actors[WardenId];
        warden.Cooldowns[Taunt] = state.Tick + run.Content.Abilities[Taunt].CooldownTicks;
        Assert.Empty(TauntsFrom(engaged with { State = state }));
    }
}
