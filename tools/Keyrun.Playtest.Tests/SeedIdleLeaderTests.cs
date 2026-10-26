using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// S1 SPEC for the seed 4 idle-leader defect (ruling seq 239). Rule (WoW M+): when the active pull is reachable and
/// unengaged and no enemy is engaged, the party leader walks toward it. Seed 4 branch 0 stands idle from about tick 11800
/// to 47600 with the active pull (boss-s2, brineMother actor 910) idle, walkable and reachable. Seed 1 is the control: the
/// party progresses, so the same assertion must hold there and is not vacuous.
/// </summary>
public sealed class SeedIdleLeaderTests
{
    private const string Dungeon = "sunken-bellfoundry";
    private const int WindowStart = 12000;
    private const int WindowTicks = 400;

    [Theory(DisplayName = "Seeded branch 0 (key 2): a leader with a reachable unengaged active pull walks toward it within 400 ticks")]
    [InlineData(4L)]
    [InlineData(1L)]
    public void Leader_walks_toward_the_active_pull(long seed)
    {
        var reg = ContentLoader.Load();
        var armed = SeededLayout.ArmSegmented(reg, Dungeon, seed);
        var moves = 0;
        var seen = false;
        var windowStart = -1;
        var lastTick = -1;
        var targetId = -1;
        double startDist = -1;
        double endDist = -1;
        var options = new PlaytestOptions(2, seed, null)
        {
            DungeonId = Dungeon,
            LeaderBranch = 0,
            Observe = (tick, run, cmds) =>
            {
                if (tick < WindowStart)
                {
                    return;
                }
                var leader = Follow.Leader(run.State);
                if (leader is null)
                {
                    return;
                }
                if (!seen)
                {
                    // The window opens at the first tick at or after WindowStart whose active pull is unengaged (every
                    // living member idle) with every seat alive: the state, not a fixed tick, picks the window (R-DEFCAP-4).
                    if (!TryUnengagedPull(run, out var member))
                    {
                        return;
                    }
                    windowStart = tick;
                    targetId = member!.Id;
                    seen = true;
                    startDist = Dist(leader.Pos, member.Pos);
                }
                if (tick > windowStart + WindowTicks)
                {
                    return;
                }
                lastTick = tick;
                if (tick < windowStart + WindowTicks)
                {
                    moves += cmds.OfType<MoveCommand>().Count(c => c.ActorId == leader.Id);
                }
                if (run.State.Actors.TryGetValue(targetId, out var t) && t.Alive)
                {
                    endDist = Dist(leader.Pos, t.Pos);
                }
            },
        };
        PlaytestRunner.Play(options, armed);

        var line = $"seed {seed}: tick {windowStart}..{windowStart + WindowTicks} target={targetId} leaderMoveCommands={moves} distStart={startDist:F0} distEnd={endDist:F0}";
        Assert.True(seen, $"seed {seed}: no tick at or after {WindowStart} with an unengaged active pull and all seats alive before the run ended (control would be vacuous)");
        Assert.True(lastTick >= windowStart + WindowTicks, $"seed {seed}: the run ended at tick {lastTick} before the window {windowStart}..{windowStart + WindowTicks} closed");
        Assert.True(
            moves > 0 || endDist < startDist,
            $"{line}: the leader idled with a reachable unengaged active pull");
    }

    /// <summary>
    /// True when every seat is alive and the active pull has at least one living member and no engaged (non-idle) living
    /// member; the first living member by id is the window's target.
    /// </summary>
    private static bool TryUnengagedPull(Keyrun.Sim.Runs.Run run, out ActorState? member)
    {
        member = null;
        if (run.State.Actors.Values.Any(a => a.Kind == ActorKind.Player && !a.Alive))
        {
            return false;
        }
        var idx = run.Encounter.PullIndex;
        if (idx < 0 || idx >= run.Dungeon.Pulls.Count)
        {
            return false;
        }
        var ids = run.Dungeon.Pulls[idx].Spawns.Select(s => s.ActorId).ToHashSet();
        var pull = run.State.Actors.Values.Where(a => a.Kind != ActorKind.Player && ids.Contains(a.Id)).ToList();
        if (pull.Any(a => a.Alive && !a.Idle))
        {
            return false;
        }
        member = pull.Where(a => a.Alive).OrderBy(a => a.Id).FirstOrDefault();
        return member is not null;
    }

    private static double Dist(Vec3 a, Vec3 b)
    {
        var dx = (double)(a.X - b.X);
        var dz = (double)(a.Z - b.Z);
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
