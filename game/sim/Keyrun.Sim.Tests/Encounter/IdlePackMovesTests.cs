using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// PROOF (lane S1, ruling R-IDLEPACK-1 amendment, seq 145): on the shipped host run, an un-engaged pack walks while it is
/// idle, and the animator picks Move on a tick it moved and Idle on a pause tick. The run is built the way the host builds
/// it (<see cref="HostRunSetup.Build"/>, then <see cref="HostRunFactory.Create"/>) and stepped <see cref="Ticks"/> ticks with
/// no input. Sim.Tests references Keyrun.HostCore, so the host builder is in scope here.
/// </summary>
public sealed class IdlePackMovesTests
{
    private const long Seed = 1234;
    private const string DungeonId = "hollow-vault";
    private const int Key = 2;
    private const string HumanClassId = "warden";
    private const int Ticks = 400;

    /// <summary>
    /// Bite switch. The shipped value is <c>true</c> (HostRunSetup.cs sets IdleWander = true). The bite proof flips it to
    /// <c>false</c> to show assertion (1) goes red, then restores it.
    /// </summary>
    private const bool WanderOn = true;

    /// <summary>A pack member's position, idle flag and clip facts read on one tick.</summary>
    /// <param name="Moved">True when the member's position changed this tick (the walking speed the view would see).</param>
    /// <param name="Decision">What <see cref="AnimRule.Decide"/> returns for those facts.</param>
    private sealed record Sample(bool Moved, AnimClip Decision);

    /// <summary>Builds the shipped host run (the same path HostRunFactory takes for a plain host start).</summary>
    /// <returns>The created run with its IdleWander flag set by <see cref="WanderOn"/>.</returns>
    private static Run BuildRun()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var host = HostRunFactory.Create(setup, new RunRequest(Seed, registry, HumanClassId));
        return host.Run with { Content = host.Run.Content with { IdleWander = WanderOn } };
    }

    /// <summary>
    /// Steps the run <see cref="Ticks"/> ticks with no input and collects, for every un-engaged, living, idle pack member,
    /// whether it walked away from its tick-0 spot and the animator's clip on its first moving tick and on its first pause
    /// tick after it had walked.
    /// </summary>
    /// <param name="run">The run at tick 0.</param>
    /// <param name="packs">The un-engaged-capable pack groups (non-boss, non-patrol).</param>
    /// <param name="moveSample">The first tick a pack member moved while its pack was un-engaged.</param>
    /// <param name="pauseSample">The first pause tick after a pack member had walked while its pack was un-engaged.</param>
    /// <returns>True when some pack member stood away from its tick-0 position while its pack was un-engaged.</returns>
    private static bool Walk(Run run, IReadOnlyList<AggroGroup> packs, out Sample? moveSample, out Sample? pauseSample)
    {
        var tick0 = run.State.Actors.Values.ToDictionary(a => a.Id, a => a.Pos);
        var prev = new Dictionary<int, Vec3>(tick0);
        var hasMoved = new HashSet<int>();
        var walkedWhileUnengaged = false;
        moveSample = null;
        pauseSample = null;

        for (var i = 0; i < Ticks && run.RunEnd.Result is null; i++)
        {
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
            foreach (var g in packs)
            {
                if (Pulls.IsEngaged(run.Encounter, g.Id))
                {
                    continue;
                }
                foreach (var id in g.ActorIds)
                {
                    if (!run.State.Actors.TryGetValue(id, out var a) || !a.Alive || !a.Idle)
                    {
                        continue;
                    }
                    var movedNow = a.Pos != prev[id];
                    if (a.Pos != tick0[id])
                    {
                        walkedWhileUnengaged = true;
                    }
                    if (movedNow)
                    {
                        hasMoved.Add(id);
                        moveSample ??= new Sample(true, Decide(movedNow));
                    }
                    else if (hasMoved.Contains(id))
                    {
                        pauseSample ??= new Sample(false, Decide(movedNow));
                    }
                    prev[id] = a.Pos;
                }
            }
        }

        return walkedWhileUnengaged;
    }

    /// <summary>The animator's decision for a live, idle, un-engaged pack member on a new tick.</summary>
    /// <param name="moving">True when the member moved this tick.</param>
    /// <returns>The clip <see cref="AnimRule.Decide"/> returns.</returns>
    private static AnimClip Decide(bool moving) =>
        AnimRule.Decide(new AnimFacts(
            Alive: true,
            NewTick: true,
            Moving: moving,
            Casting: false,
            Acted: false,
            Hurt: false,
            Staggered: false,
            Playing: AnimClip.None)).Clip;

    /// <summary>
    /// Assertion (1): an un-engaged non-boss pack member stands away from its tick-0 position while its pack is still
    /// un-engaged. Assertion (2): AnimRule returns Move on a tick it moved and Idle on a pause tick.
    /// </summary>
    [Fact]
    public void UnengagedPackMemberWalksAndAnimatesMoveOnMovingTicks()
    {
        var run = BuildRun();
        var packIds = (run.Content.Packs ?? Array.Empty<PackGroup>()).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var packs = Aggro.GroupsOf(run.Dungeon, run.State.Tick, run.Content.Packs, run.Content.Layout, run.Content.Patrols)
            .Where(g => packIds.Contains(g.Id) && !IdleWander.IsBossGroup(g.Id))
            .ToList();
        Assert.NotEmpty(packs);

        var walked = Walk(run, packs, out var moveSample, out var pauseSample);

        Assert.True(walked, "(1) no un-engaged non-boss pack member stood away from its tick-0 Pos within the first " + Ticks + " ticks");
        Assert.NotNull(moveSample);
        Assert.True(moveSample!.Moved);
        Assert.Equal(AnimClip.Move, moveSample.Decision);
        Assert.NotNull(pauseSample);
        Assert.False(pauseSample!.Moved);
        Assert.Equal(AnimClip.Idle, pauseSample.Decision);
    }
}
