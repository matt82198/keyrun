using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// PR #308 HudSceneTest run B (<c>--autorun --ticks 600 --pull 2 --solo</c>): the solo Warden in the Pair's hall, built
/// headless as SimDriver.StartRun builds it (<see cref="HostRunSetup.Build"/> at key 2, seed 1, no bots, the dungeon from
/// pull 2 on, <see cref="RunRoot.EngageStartPull"/>, <see cref="BossRules.Arm"/>), seat 1 the autorun
/// <see cref="ScriptedWarden"/>. With the room gates gone (STATE.md D44) the Pair's melee bodies pushed the standing
/// Warden about 18 m down the corridor, fewer of them landed hits, and he lived to tick 354, past the Pair's first mark
/// telegraph (about tick 296), so the HUD called SPREAD for a cast that never starts (check 8, wrong data shown).
/// ENEMY-NO-PUSH (STATE.md D50): as in WoW, an enemy body never displaces a player (and a player never shoves an enemy);
/// enemy-enemy and player-player push-out are unchanged.
/// </summary>
public sealed class RunBSoloPairTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int RunBTicks = 600; // run B's --ticks (HudSceneTest $PingTicks)

    // The mark effects a SPREAD/STACK call reads (game/src/Hud/Pings.cs PingCalls.SpreadEffectId/StackEffectId; that file
    // is Godot-bound, so its rule is restated here: a pending telegraph whose ability applies a mark, or a live actor
    // carrying one).
    private static readonly HashSet<string> MarkEffects = new(StringComparer.Ordinal) { "spreadMarked", "stackMarked" };

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public RunBSoloPairTests(ITestOutputHelper output) => _output = output;

    private static (Run Run, IReadOnlyList<int> PairIds) StartRunB()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: false);
        var dungeon = setup.Dungeon with { Pulls = setup.Dungeon.Pulls.Skip(1).ToList() }; // --pull 2
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var created = RunRoot.EngageStartPull(RunRoot.CreateRun(Seed, setup.PartyFor(dungeon), dungeon, setup.Content, options));
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var pairIds = dungeon.Pulls.Single(p => p.Id == "pair").Spawns.Select(s => s.ActorId).ToList();
        return (run, pairIds);
    }

    private static bool AnyMarkCall(Run run)
    {
        var now = run.State.Tick;
        foreach (var t in run.Encounter.Telegraphs)
        {
            if (run.Content.Abilities.TryGetValue(t.AbilityId, out var def) &&
                def.Effects.Any(op => op is Keyrun.Sim.Combat.ApplyEffectOp a && MarkEffects.Contains(a.EffectId)))
            {
                return true;
            }
        }
        return run.State.Actors.Values.Any(a => a.Alive && a.Effects.Any(e => e.EndsTick > now && MarkEffects.Contains(e.EffectId)));
    }

    [Fact(DisplayName = "ENEMY-NO-PUSH: the crowding Pair pack never moves a stationary Warden")]
    public void Crowding_pair_never_moves_a_stationary_player()
    {
        var (run, pairIds) = StartRunB();
        var warden = run.State.Actors[HostRunSetup.HumanId];
        var start = warden.Pos;
        var touch = 2 * run.Content.ActorRadiusMm;
        var closest = long.MaxValue;
        var touching = 0;
        Vec3 last = start;
        var ticks = 0;
        while (run.State.Tick < RunBTicks && run.State.Actors[HostRunSetup.HumanId].Alive)
        {
            run = RunRoot.StepRun(run, new List<Command>()).Run;
            ticks++;
            var me = run.State.Actors[HostRunSetup.HumanId];
            var near = pairIds.Select(id => run.State.Actors[id]).Where(a => a.Alive).Select(a => Keyrun.Sim.Encounter.ScriptTarget.RangeMm(me, a)).ToList();
            closest = Math.Min(closest, near.DefaultIfEmpty(long.MaxValue).Min());
            touching = Math.Max(touching, near.Count(d => d <= touch + 1000));
            last = me.Pos;
            Assert.True(me.Pos.X == start.X && me.Pos.Z == start.Z,
                $"tick {run.State.Tick}: the Warden stood still but was moved from ({start.X},{start.Z}) to ({me.Pos.X},{me.Pos.Z})");
        }
        _output.WriteLine($"ticks={ticks} start=({start.X},{start.Z}) last=({last.X},{last.Z}) closestPair={closest} maxPairWithinTouchPlus1m={touching} touch={touch}");
        // Non-vacuity: the pack really crowded him (several bodies at touching range at once).
        Assert.True(touching >= 2, $"the Pair never crowded the Warden (at most {touching} bodies within {touch + 1000} mm)");
    }

    [Fact(DisplayName = "Run B (--pull 2 --solo): the solo Warden is down before the Pair's first mark, so no SPREAD/STACK call ever shows (HudSceneTest check 8)")]
    public void Run_b_never_calls_spread_or_stack()
    {
        var (run, _) = StartRunB();
        var warden = new ScriptedWarden();
        int? deathTick = null;
        int? firstCall = null;
        long? wardenXAtDeath = null;
        for (var i = 0; i < RunBTicks; i++)
        {
            var commands = new List<Command>();
            warden.Sample(run.State.Tick, run, commands);
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            var me = run.State.Actors[HostRunSetup.HumanId];
            if (deathTick is null && !me.Alive)
            {
                deathTick = run.State.Tick;
                wardenXAtDeath = me.Pos.X;
            }
            if (firstCall is null && AnyMarkCall(run))
            {
                firstCall = run.State.Tick;
            }
        }
        _output.WriteLine($"warden death tick={deathTick?.ToString() ?? "none"} x at death={wardenXAtDeath?.ToString() ?? "none"} first SPREAD/STACK call tick={firstCall?.ToString() ?? "none"} runEnd={run.RunEnd.Result?.ToString() ?? "none"}@{run.RunEnd.ResultTick?.ToString() ?? "none"}");
        Assert.True(deathTick is not null, "the solo Warden survived run B's 600 ticks");
        Assert.True(firstCall is null, $"a SPREAD/STACK call showed at tick {firstCall} (the Warden died at tick {deathTick})");
    }
}
