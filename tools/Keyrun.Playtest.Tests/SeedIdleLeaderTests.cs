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
    private const string LogPath = "C:/Users/Mattt/AppData/Local/Temp/claude/C--Users-Mattt-aesop/0198324a-b702-41b5-882f-d2628aac95cc/scratchpad/lanes/s1-seed4-idle.log";

    [Theory(DisplayName = "Seeded branch 0 (key 2): a leader with a reachable unengaged active pull walks toward it within 400 ticks")]
    [InlineData(4L)]
    [InlineData(1L)]
    public void Leader_walks_toward_the_active_pull(long seed)
    {
        var reg = ContentLoader.Load();
        var armed = SeededLayout.ArmSegmented(reg, Dungeon, seed);
        var moves = 0;
        var seen = false;
        var targetId = -1;
        double startDist = -1;
        double endDist = -1;
        var options = new PlaytestOptions(2, seed, null)
        {
            DungeonId = Dungeon,
            LeaderBranch = 0,
            MaxTicks = WindowStart + WindowTicks + 1,
            Observe = (tick, run, cmds) =>
            {
                if (tick < WindowStart || tick > WindowStart + WindowTicks)
                {
                    return;
                }
                var leader = Follow.Leader(run.State);
                if (leader is null)
                {
                    return;
                }
                if (tick == WindowStart)
                {
                    var idx = run.Encounter.PullIndex;
                    if (idx < 0 || idx >= run.Dungeon.Pulls.Count)
                    {
                        return;
                    }
                    var ids = run.Dungeon.Pulls[idx].Spawns.Select(s => s.ActorId).ToHashSet();
                    var member = run.State.Actors.Values
                        .Where(a => a.Kind != ActorKind.Player && a.Alive && ids.Contains(a.Id))
                        .OrderBy(a => a.Id)
                        .FirstOrDefault();
                    if (member is null)
                    {
                        return;
                    }
                    targetId = member.Id;
                    seen = true;
                    startDist = Dist(leader.Pos, member.Pos);
                }
                if (!seen)
                {
                    return;
                }
                if (tick < WindowStart + WindowTicks)
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

        var line = $"seed {seed}: tick {WindowStart}..{WindowStart + WindowTicks} target={targetId} leaderMoveCommands={moves} distStart={startDist:F0} distEnd={endDist:F0}";
        System.IO.File.AppendAllText(LogPath, line + "\n");
        Assert.True(seen, $"seed {seed}: no living active-pull member at tick {WindowStart} (control would be vacuous)");
        Assert.True(
            moves > 0 || endDist < startDist,
            $"{line}: the leader idled with a reachable unengaged active pull");
    }

    private static double Dist(Vec3 a, Vec3 b)
    {
        var dx = (double)(a.X - b.X);
        var dz = (double)(a.Z - b.Z);
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
