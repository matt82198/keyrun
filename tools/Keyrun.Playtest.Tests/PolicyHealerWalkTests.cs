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
/// <para>
/// R-BOTS-6 re-pin (BOTS-INTEGRATE: #295 + #327 + #298 on main a7069768; a measurement, the assertions are unchanged):
/// was 454, where the tank now stands at 382/400 (the guard read "the tank is not under 70% at 454: 382/400"). Measured
/// in one pass over seed 1 key 2 (kick plan, a replica of the runner's loop that ends Timed at 9588 as the runner does),
/// testing the scene on every tick from 100: it holds on single ticks 545-550 but never across a 20-tick window there;
/// 9452 (boss pull, tank 235/400) is the first tick at which it holds on every window tick.
/// </para>
/// <para>
/// R-BOTS-11 re-pin (a measurement; the assertions are unchanged): 9452 lies past the run's end (it ends Timed at 9313,
/// the guard read <c>Expected: 9452 Actual: 9313</c>), so both facts were dead. The pin is now the walk itself, and the
/// window is that walk's measured length, not a number chosen to pass. Measured on this tree by playing seed 1 key 2
/// (kick plan, a replica of the runner's loop, Timed at 9313) and reading the Chanter on every tick (real run):
/// <code>
///   t=544 tank 300/400  healer (14771,-998)  decides Target 100 + Cast chanter.dissonance (no walk)
///   t=545 tank 255/400  healer (14771,-998)  decides Move (946,325)   walk starts: tank under 70%, heal pick walks
///   t=546 tank 255/400  healer (14941,-940)  decides Move (946,325)
///   t=547 tank 255/400  healer (15111,-882)  decides Move (946,325)
///   t=548 tank 255/400  healer (15281,-824)  decides Move (946,325)
///   t=549 tank 255/400  healer (15451,-766)  decides Move (946,325)
///   t=550 tank 255/400  healer (15621,-708)  decides Move (946,325)   last step
///   t=551 tank 255/400  healer (15791,-650)  decides Cast chanter.hymn   in Hymn's pool: the walk is over
/// </code>
/// The scene (tank under 70%, the tank is the triage target, the heal pick is a single Move) holds on exactly ticks
/// 545-550, so the walk is six one-tick steps (about 1.0 m at 170 mm per tick), the same six steps as the original
/// R-326-B walk. The pin is the walk's first tick and the window is its length, <see cref="WalkLastTick"/> -
/// <see cref="Tick"/> + 1 = 6.
/// </para>
/// <para>
/// R-BOTS-18 re-pin (one re-derivation with the R-BOTS-17 oracle re-record on #449: in Mend the Chanter presses the
/// Payoff on the fight's first idle GCD only, then the Refrain into Ward; a measurement, the assertions are unchanged):
/// was 545, where the tank now stands at 315/400 (the guard read "the tank is not under 70% at 545: 315/400"; the
/// walker read <c>Expected: 1 Actual: null</c>). Measured by playing seed 1 key 2 (kick plan with the Warden mitigating,
/// this test's plan; on #449 merged with main 0f6a8989 the run ends Timed at 8764 with 0 deaths, and the table is the
/// same before that merge) to each tick and asking the Chanter's role (HealerRole.HealerDecide):
/// <code>
///   t=330-341 tank 252/400  healer (14771,-998)  decides nothing (no walk)
///   t=342 tank 252/400  healer (14771,-998)  decides Move (946,325)   walk starts: tank under 70%, heal pick walks
///   t=343-346 tank 252/400  healer (14771,-998)  decides Move (946,325)   (the policy seat's first step lands at 346)
///   t=347 tank 252/400  healer (14941,-940)  decides Move (946,325)
///   t=348 tank 252/400  healer (15111,-882)  decides Move (946,325)
///   t=349 tank 252/400  healer (15281,-824)  decides Move (946,325)
///   t=350 tank 207/400  healer (15451,-766)  decides Move (946,325)
///   t=351 tank 207/400  healer (15621,-708)  decides Move (946,325)   last step
///   t=352 tank 207/400  healer (15791,-650)  decides Cast chanter.hymn   in Hymn's pool: the walk is over
/// </code>
/// The scene holds on exactly ticks 342-351, so the pin is the walk's first tick and the window is its length,
/// 351 - 342 + 1 = 10 (the healer itself takes the same six one-tick steps, 346-351, about 1.0 m). The run's other
/// walk (boss pull, 8307-8312) is the later one.
/// </para>
/// </summary>
public sealed class PolicyHealerWalkTests
{
    private const string KickPlan = "{\"mitigate\": [\"warden\"], \"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";
    private const int Chanter = 2;
    private const int Tick = 342; // R-BOTS-18: the measured walk's first tick (was 545, R-BOTS-11; 9452 before that)
    private const int WalkLastTick = 351; // R-BOTS-18: the measured walk's last step (the trace above); 352 casts Hymn (was 550)
    private const int Window = WalkLastTick - Tick + 1;

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
