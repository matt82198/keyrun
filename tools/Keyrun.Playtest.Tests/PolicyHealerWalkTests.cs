using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// POLICY-WALK-HELD (R-326-B): at seed 1 key 2 (kick plan) the tank falls under 70% while Hymn is ready, so the healer's
/// heal pick walks it into Hymn's pool (about 1.1 m, six one-tick steps). <see cref="PolicyBot"/> once asked the role only
/// on its hands' reaction ticks, so the walk was one step per 6-13 tick reaction: 41 ticks instead of 6, every later heal
/// came 40 ticks late and the tank died. In WoW a player who decides to walk holds the key; the reaction delay is for
/// deciding, not for every step of the walk.
/// <para>
/// R-PINS-1 (finding f-b5b9d0e9): this file used to pin the scene to one run tick (325, re-pinned from 342, 545, 454, 194
/// and 9452 each time a gameplay change moved the fight) with a hand-measured window (14, 10, 6). The property under test
/// does not depend on WHICH tick the scene first appears: given a frozen scene where the tank is under 70%, is the triage
/// target and the heal pick is a single Move, the seat must keep walking on every tick after it starts, whatever its
/// reaction delay. So the scene tick is now FOUND by observing the run (<see cref="PlaytestOptions.Observe"/>), and the
/// window is derived from the hands' own reaction bounds, long enough to contain a full reaction period.
/// </para>
/// </summary>
public sealed class PolicyHealerWalkTests
{
    private const string KickPlan = "{\"mitigate\": [\"warden\"], \"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";
    private const int Chanter = 2;

    /// <summary>How far into the run the scene is searched for. A bound so a drifted run fails loudly, not forever.</summary>
    private const int SearchTicks = 4000;

    /// <summary>The window the held walk is watched over: one more tick than the hands' longest reaction delay
    /// (<see cref="Hands.ReactBaseTicks"/> + <see cref="Hands.ReactSpreadTicks"/> - 1), so a walk re-decided only on reaction
    /// ticks (one step per reaction) cannot move on every tick of it. Derived from the hands, not from a measured walk.</summary>
    private const int Window = Hands.ReactBaseTicks + Hands.ReactSpreadTicks;

    private static bool SceneHolds(SimState state, Run run, KitRegistry kits)
    {
        var tank = state.Actors[PlaytestRunner.TankId];
        if (!(tank.Alive && tank.Health * 10 < tank.MaxHealth * 7)
            || Priority.PickHealTarget(state.Actors, 0.7) != PlaytestRunner.TankId)
        {
            return false;
        }
        var view = BotViews.BuildBotView(state, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
        var decision = HealerRole.HealerDecide(view, state.Actors[Chanter]);
        return decision.Count == 1 && decision[0] is MoveCommand;
    }

    /// <summary>The first run tick at which the walking scene holds on every tick of the whole <see cref="Window"/> (the
    /// state frozen, only its tick advanced, as the held-walk test plays it), and the run at that tick; null if none within
    /// <see cref="SearchTicks"/>.</summary>
    private static (int Tick, Run Run, KitRegistry Kits, PolicyContent Content)? FindScene()
    {
        var content = ContentLoader.Load();
        var policy = PolicyReader.Parse(KickPlan, content);
        var registry = PlaytestRunner.WithTalents(content, policy);
        var kits = KitRegistry.From(registry);
        (int, Run)? found = null;
        PlaytestRunner.Play(new PlaytestOptions(2, 1, policy)
        {
            MaxTicks = SearchTicks,
            Observe = (tick, run, _) =>
            {
                if (found is not null)
                {
                    return;
                }
                var holds = true;
                for (var t = tick; t < tick + Window && holds; t++)
                {
                    var frozen = run.State.Clone();
                    frozen.Tick = t;
                    holds = SceneHolds(frozen, run, kits);
                }
                if (holds)
                {
                    found = (tick, run);
                }
            },
        }, content);
        return found is { } f ? (f.Item1, f.Item2, kits, new PolicyContent(policy, registry)) : null;
    }

    private sealed record PolicyContent(Policy Policy, ContentRegistry Registry);

    [Fact(DisplayName = "R-PILE-8 guard (R-PINS-1): a walking scene exists in the run (the tank under 70% is the triage target and the heal pick walks) for a full reaction window")]
    public void A_walking_scene_exists_in_the_run()
    {
        var scene = FindScene();
        Assert.True(scene is not null, $"no tick in the first {SearchTicks} of seed 1 key 2 (kick plan) has the tank under 70%, the triage target, and the heal pick a single Move for {Window} ticks: the held-walk test below would be vacuous");
        var (tick, run, kits, _) = scene.Value;
        var tank = run.State.Actors[PlaytestRunner.TankId];
        Assert.Equal(tick, run.State.Tick);
        Assert.True(tank.Alive && tank.Health * 10 < tank.MaxHealth * 7, $"the tank is not under 70% at {tick}: {tank.Health}/{tank.MaxHealth}");
        Assert.Equal(PlaytestRunner.TankId, Priority.PickHealTarget(run.State.Actors, 0.7));
    }

    [Fact(DisplayName = "A policy healer walking into its heal pool keeps walking every tick, not one step per reaction")]
    public void Healer_holds_the_walk_into_its_heal_pool()
    {
        var scene = FindScene();
        Assert.True(scene is not null, $"no walking scene within the first {SearchTicks} ticks (see the guard test)");
        var (tick, run, kits, pc) = scene.Value;
        var seat = new PolicyBot(pc.Policy, new PolicyContext(), pc.Registry, 1, Chanter, PlaytestRunner.TankId);

        var moves = 0;
        var firstMove = -1;
        for (var t = tick; t < tick + Window; t++)
        {
            var state = run.State.Clone();
            state.Tick = t;
            var view = BotViews.BuildBotView(state, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };

            // The scene: the tank is the triage target and the heal pick walks the healer into Hymn's pool.
            Assert.True(SceneHolds(state, run, kits), $"the walking scene lapsed at tick {t} of the window starting {tick}");

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

        // The walk may start after a reaction delay (up to Hands.ReactBaseTicks + Hands.ReactSpreadTicks - 1 ticks), but once
        // started it moves on every remaining tick of the window: a role asked only on reaction ticks would move once per 6-13.
        Assert.True(firstMove >= 0, "the healer never started the walk");
        Assert.Equal(tick + Window - firstMove, moves);
    }
}
