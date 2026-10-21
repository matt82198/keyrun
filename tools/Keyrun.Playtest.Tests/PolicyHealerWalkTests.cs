using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// POLICY-WALK-HELD (R-326-B, the Choir lesson red once the Tempo clamp restored the written enemy rates): at seed 1
/// key 2 (kick plan) the tank fell under 70% at tick 194 with Chorus on cooldown and Hymn ready, so the healer's heal
/// pick walked it into Hymn's pool (about 1.1 m, six one-tick steps). <see cref="PolicyBot"/> asked the role only on
/// its hands' reaction ticks, so the walk was one step per 6-13 tick reaction: 41 ticks instead of 6, Hymn began at 242
/// instead of about 201, every later heal came 40 ticks late and the tank died at 311. In WoW a player who decides to
/// walk holds the key; the reaction delay is for deciding, not for every step of the walk.
/// <para>
/// R-PILE-8 re-pin (a measurement, not a retune; the assertions are unchanged). History: tick 194 on #326's own tree
/// (f79b304b). On the integrated tree (#307 + #362 + #364 + setup-lesson + #326, then main 026bca17, #425, #426 and
/// death-cancels-casts) #362's D-HEALER-DOWNTIME press closes the Cinder's kindled Window into Steam on enemy 100 at
/// 147, 207 and 272; the Blind rule makes 11 Choir swings on the tank miss between 154 and 349, so the tank sits at
/// 360/400 and is nobody's heal target at 194 (the old pin then read <c>Expected: 1 Actual: null</c> at the scene
/// check). Measured by playing seed 1 key 2 to every tick from 100: no tick before 441 shows the scene; from 441 it
/// holds on part of the 20-tick window (Hymn readiness is tick-dependent); 454 (tank 255/400) is the first tick at
/// which it holds on every window tick, and the walk then starts at 454 and runs all 20 ticks.
/// </para>
/// </summary>
public sealed class PolicyHealerWalkTests
{
    private const string KickPlan = "{\"mitigate\": [\"warden\"], \"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";
    private const int Chanter = 2;
    private const int Tick = 454; // R-PILE-8: was 194 on #326's own tree; measured on the integrated tree (summary)
    private const int Window = 20;

    [Fact(DisplayName = "R-PILE-8 guard: the pinned scene exists at its tick (the tank under 70% is the triage target and the heal pick walks)")]
    public void The_pinned_scene_exists_at_its_tick()
    {
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(KickPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var run = PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = Tick }, content).Run;
        Assert.Equal(Tick, run.State.Tick);
        var tank = run.State.Actors[PlaytestRunner.TankId];
        Assert.True(tank.Alive && tank.Health * 10 < tank.MaxHealth * 7, $"the tank is not under 70% at {Tick}: {tank.Health}/{tank.MaxHealth}");
        Assert.Equal(PlaytestRunner.TankId, Priority.PickHealTarget(run.State.Actors, 0.7));
        var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, KitRegistry.From(registry)) with { Layout = run.Content.Layout };
        Assert.IsType<MoveCommand>(Assert.Single(HealerRole.HealerDecide(view, run.State.Actors[Chanter])));
    }

    [Fact(DisplayName = "A policy healer walking into its heal pool keeps walking every tick, not one step per reaction")]
    public void Healer_holds_the_walk_into_its_heal_pool()
    {
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(KickPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var run = PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = Tick }, content).Run;
        Assert.Equal(Tick, run.State.Tick);
        var kits = KitRegistry.From(registry);
        var seat = new PolicyBot(policy, new PolicyContext(), registry, 1, Chanter, PlaytestRunner.TankId);

        var moves = 0;
        var firstMove = -1;
        for (var t = Tick; t < Tick + Window; t++)
        {
            var state = run.State.Clone();
            state.Tick = t;
            var view = BotViews.BuildBotView(state, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var healer = state.Actors[Chanter];

            // The scene: the tank is the triage target and the heal pick walks the healer into Hymn's pool.
            Assert.Equal(PlaytestRunner.TankId, Priority.PickHealTarget(state.Actors, 0.7));
            Assert.IsType<MoveCommand>(Assert.Single(HealerRole.HealerDecide(view, healer)));

            var decision = seat.Decide(view, BotMemory.Default(PlaytestRunner.ScriptedSkillDial), Chanter, Skill.SeedPrng(1));
            if (decision.Commands.Any(c => c is MoveCommand))
            {
                moves++;
                if (firstMove < 0)
                {
                    firstMove = t;
                }
            }
        }

        Assert.True(firstMove >= 0, "the healer never started the walk");
        Assert.Equal(Tick + Window - firstMove, moves);
    }
}
