using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Finding f-7aa25aa6 (WoW Mythic+: plan the count, pull to 100, then boss), the bot-led half. When the lead seat is a bot
/// (<see cref="Follow.LeadCommand"/>: the human seat stands still, or a playtester's policy runs the party), the leader's
/// <see cref="BotView"/> must carry the packs and the forces table (<see cref="BotView.PullPlan"/>) so the same
/// <see cref="ForcesPlanner"/> the scripted Warden uses (<see cref="Follow.LeadRoute"/>) makes the bot leader pull the bar
/// to 100% before it walks into the boss arena. Driven through <see cref="BotCommandSource"/> with every seat a bot at
/// key 2, seed 1; <see cref="BotCommandSource.Forces"/> is the switch, and without it the leader walks past the optional
/// packs (the control).
/// </summary>
public sealed class LeadCommandPullPlanTests
{
    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());
    private static readonly Lazy<Drive> WithPlan = new(() => Play(true));
    private static readonly Lazy<Drive> WithoutPlan = new(() => Play(false));

    /// <summary>The bar the tick the boss first engaged (null when it never did), and the run's end.</summary>
    private sealed record Drive(ForcesReading? AtBossEngage, int BossEngageTick, RunResult? Result, int? EndTick, int ParTicks);

    private static Drive Play(bool plan)
    {
        var registry = Registry.Value;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        // Every seat a bot, the human's too: the lowest living seat is a bot, so LeadCommand leads (the host's idle-human case).
        var bots = new BotCommandSource(setup.Party.Select(p => p.Id), KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null)
        {
            Forces = plan ? ForcesPlan.Of(registry) : null,
        };
        bots.Reset();
        var forces = ForcesPlan.Of(registry)!;
        var boss = run.Dungeon.Pulls[^1].Spawns.Select(s => s.ActorId).ToHashSet();
        var par = setup.Content.ParTicks ?? 24000;
        ForcesReading? atBoss = null;
        var bossTick = -1;
        while (run.State.Tick < 2 * par && run.RunEnd.ResultTick is null)
        {
            var commands = new List<Command>();
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            if (atBoss is null && run.State.Actors.Values.Any(a => boss.Contains(a.Id) && a.Alive && !a.Idle))
            {
                atBoss = Forces.Read(run.State, forces.PointsByEnemy, forces.Required);
                bossTick = run.State.Tick;
            }
            run = RunRoot.StepRun(run, commands).Run;
        }
        return new Drive(atBoss, bossTick, run.RunEnd.Result, run.RunEnd.ResultTick, par);
    }

    [Fact(DisplayName = "Bot-led pull plan: a bot leader seat pulls the forces bar to 100% before the boss engages (key 2, seed 1)")]
    public void A_bot_leader_fills_the_bar_before_the_boss()
    {
        var r = WithPlan.Value;
        Assert.NotNull(r.AtBossEngage);
        Assert.True(r.AtBossEngage!.Full, $"forces at boss engage (tick {r.BossEngageTick}): {r.AtBossEngage.LogLine()}");
    }

    [Fact(DisplayName = "Bot-led pull plan: the bot-led run still ends timed within par")]
    public void The_bot_led_run_ends_timed_within_par()
    {
        var r = WithPlan.Value;
        Assert.Equal(RunResult.Timed, r.Result);
        Assert.True(r.EndTick <= r.ParTicks, $"run_end at tick {r.EndTick}, par {r.ParTicks}");
    }

    [Fact(DisplayName = "Bot-led pull plan: without the forces table the bot leader reaches the boss short of the bar (control)")]
    public void Without_the_plan_the_bar_is_short()
    {
        var r = WithoutPlan.Value;
        Assert.NotNull(r.AtBossEngage);
        Assert.False(r.AtBossEngage!.Full, $"forces at boss engage without the plan: {r.AtBossEngage.LogLine()}");
    }
}
