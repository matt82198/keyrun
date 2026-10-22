using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Finding f-c8ed3a62, ruling R-AOE-1 clause (c) (Matt 2026-10-22: "the goal is normally to kill most of them at the same
/// time / carry them into the next pack to optimize"): the bot tank chain-pulls. Once the current trash pack is nearly dead
/// and the next trash pack is in reach, he walks on and pulls it, so the packs stack and die together. Driven through
/// <see cref="PlaytestRunner"/> at key 2, the headless replica of the autorun, on the Hollow Vault (seed 1) and the Sunken
/// Bellfoundry (seed 7). A group is "engaged" on the first tick any member is no longer idle and "cleared" on the first tick
/// every member is dead; both are read from the state, never from a recorded tick.
/// </summary>
public sealed class BotChainPullTests
{
    private readonly ITestOutputHelper _out;

    public BotChainPullTests(ITestOutputHelper output) => _out = output;

    /// <summary>One enemy group's life: its members, when it first engaged and when its last member died.</summary>
    private sealed class Life
    {
        public Life(string id, IReadOnlyList<int> members, bool trash)
        {
            Id = id;
            Members = members;
            Trash = trash;
        }

        public string Id { get; }

        public IReadOnlyList<int> Members { get; }

        public bool Trash { get; }

        public int Engaged { get; set; } = -1;

        public int Cleared { get; set; } = -1;
    }

    private sealed record Timeline(IReadOnlyList<Life> Lives, PlaytestResult Result, int Wipes, string FinalBossId);

    private static Timeline Drive(string dungeonId, long seed)
    {
        var registry = ContentLoader.Load();
        var lives = new List<Life>();
        var wipes = 0;
        var finalBoss = string.Empty;
        var result = PlaytestRunner.Play(new PlaytestOptions(2, seed, null)
        {
            DungeonId = dungeonId,
            Observe = (tick, run, _) =>
            {
                if (lives.Count == 0)
                {
                    finalBoss = run.Dungeon.Pulls[^1].Id;
                    foreach (var pull in run.Dungeon.Pulls)
                    {
                        lives.Add(new Life(pull.Id, pull.Spawns.Select(s => (int)s.ActorId).ToList(), !LayoutDef.IsBossPull(pull.Id)));
                    }
                    foreach (var pack in run.Content.Packs ?? Array.Empty<Keyrun.Sim.Encounter.PackGroup>())
                    {
                        lives.Add(new Life(pack.Id, pack.Spawns.Select(s => (int)s.ActorId).ToList(), true));
                    }
                    foreach (var patrol in run.Content.Patrols ?? Array.Empty<Keyrun.Sim.Encounter.PatrolPack>())
                    {
                        lives.Add(new Life(patrol.Id, patrol.Spawns.Select(s => (int)s.ActorId).ToList(), true));
                    }
                }
                foreach (var life in lives)
                {
                    var actors = life.Members.Select(id => run.State.Actors.TryGetValue(id, out var a) ? a : null).Where(a => a is not null).ToList();
                    if (life.Engaged < 0 && actors.Any(a => !a!.Idle && a.Alive))
                    {
                        life.Engaged = tick;
                    }
                    if (life.Engaged >= 0 && life.Cleared < 0 && actors.Count > 0 && actors.All(a => !a!.Alive))
                    {
                        life.Cleared = tick;
                    }
                }
                if (!run.State.Actors.Values.Any(a => a.Kind == ActorKind.Player && a.Alive))
                {
                    wipes++;
                }
            },
        }, registry);
        return new Timeline(lives, result, wipes, finalBoss);
    }

    private static readonly Lazy<Timeline> HollowVault = new(() => Drive("hollow-vault", 1));

    private static readonly Lazy<Timeline> Bellfoundry = new(() => Drive("sunken-bellfoundry", 7));

    private void Check(Timeline t, bool requireStack)
    {
        foreach (var l in t.Lives.OrderBy(l => l.Engaged < 0 ? int.MaxValue : l.Engaged))
        {
            _out.WriteLine($"{l.Id,-24} trash={l.Trash} engaged={l.Engaged} cleared={l.Cleared}");
        }
        _out.WriteLine($"run_end {t.Result.Run.RunEnd.Result} at {t.Result.Run.RunEnd.ResultTick}; wipe ticks {t.Wipes}");

        Assert.Equal(0, t.Wipes);
        var boss = t.Lives.Single(l => l.Id == t.FinalBossId);
        Assert.True(boss.Engaged >= 0, "the run never reached the final boss");
        Assert.NotNull(t.Result.Run.RunEnd.ResultTick);
        Assert.All(boss.Members, id => Assert.False(t.Result.Run.State.Actors[id].Alive, "the final boss still lives at run end"));

        // Elites and bosses are never chained into: no trash pack is still alive and engaged on the tick a boss group engages.
        foreach (var b in t.Lives.Where(l => !l.Trash && l.Engaged >= 0))
        {
            var chained = t.Lives.Where(l => l.Trash && l.Engaged >= 0 && l.Engaged <= b.Engaged && (l.Cleared < 0 || l.Cleared > b.Engaged)).Select(l => l.Id).ToList();
            Assert.True(chained.Count == 0, $"{b.Id} engaged at {b.Engaged} while {string.Join(", ", chained)} still lived");
        }

        var trash = t.Lives.Where(l => l.Trash && l.Engaged >= 0).OrderBy(l => l.Engaged).ToList();
        var stacked = trash
            .Zip(trash.Skip(1), (a, b) => (A: a, B: b))
            .Where(p => p.A.Cleared >= 0 && p.B.Engaged < p.A.Cleared)
            .ToList();
        Assert.True(!requireStack || stacked.Count > 0, "no trash pack was pulled while the previous one still lived: " + string.Join(", ", trash.Select(l => $"{l.Id}@{l.Engaged}-{l.Cleared}")));
    }

    [Fact(DisplayName = "Chain pull: on the Hollow Vault the next trash pack engages before the last enemy of the current one dies (key 2, seed 1, no wipe, the boss still falls)")]
    public void The_next_pack_engages_before_the_current_one_is_clear() => Check(HollowVault.Value, requireStack: true);

    // The Bellfoundry's packs stand 43000 mm and more apart (layout centres in the seed 7 run), beyond ChainPull.ReachMm, so
    // nothing stacks there until the pack-density work (R-PACKDENSITY-2) closes the gaps; this pins that the chain does no
    // harm there: no wipe, the final boss falls, and no segment boss is ever chained into.
    [Fact(DisplayName = "Chain pull: on the Sunken Bellfoundry no segment boss is chained into, nobody wipes and the final boss still falls (key 2, seed 7)")]
    public void The_bellfoundry_still_finishes_and_never_chains_into_a_boss() => Check(Bellfoundry.Value, requireStack: false);
}
