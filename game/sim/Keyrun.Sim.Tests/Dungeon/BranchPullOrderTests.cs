using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Dungeon;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Dungeon;

/// <summary>
/// D45 (the player chooses the route; f-46043bc6 follow-up "untaken branch not walkable"): over the seeded graph and
/// fill, the run's pulls are every branch's teaching pulls (each tagged with its branch) then the boss. Whichever branch
/// the party engages first, clearing that branch's pulls and the boss walks the pull index to the end, with one
/// pull_clear per pull the party killed and none for the other branch: pull order is branch-agnostic.
/// </summary>
public sealed class BranchPullOrderTests
{
    private const int BossActor = 900;

    private static DungeonDef DungeonOf(long seed)
    {
        var g = RoomGraphGen.Generate(seed, DungeonGenParams.Default);
        var fill = BranchFill.Fill(seed, g, BranchFillTests.Teaching, BranchFillTests.Optional);
        var pulls = new List<PullDef>();
        var actor = 100;
        foreach (var br in fill.Branches)
        {
            foreach (var p in fill.Packs.Where(p => p.Branch == br.Index && p.PullIndex >= 0))
            {
                var spawn = new PullSpawn(actor++, p.PackId, Vec3.Zero, 0, 100);
                pulls.Add(new PullDef(string.Create(CultureInfo.InvariantCulture, $"{p.PackId}@{br.Index}"), new[] { spawn })
                {
                    Branch = br.Index.ToString(CultureInfo.InvariantCulture),
                });
            }
        }
        pulls.Add(new PullDef("boss", new[] { new PullSpawn(BossActor, "boss", Vec3.Zero, 0, 100) }));
        return new DungeonDef("seeded", pulls);
    }

    private static ActorState Enemy(int id) => new()
    {
        Id = id,
        Kind = ActorKind.Enemy,
        ClassId = "e",
        Role = ActorRole.None,
        Pos = Vec3.Zero,
        Yaw = 0,
        Health = 100,
        MaxHealth = 100,
        Resource = 0,
        MaxResource = 0,
        Alive = true,
    };

    /// <summary>Plays the party engaging and killing <paramref name="taken"/>'s pulls in order, then the boss.</summary>
    private static (EncounterState End, List<string> Cleared) Clear(DungeonDef dungeon, string taken)
    {
        var state = SimState.Create(1);
        foreach (var s in dungeon.Pulls.SelectMany(p => p.Spawns))
        {
            state.Actors[s.ActorId] = Enemy(s.ActorId);
        }
        var cleared = new List<string>();
        var enc = new EncounterState();
        var tick = 0;
        void Settle()
        {
            for (var i = 0; i < dungeon.Pulls.Count + 2; i++)
            {
                enc = Pulls.CheckPullAdvance(state, dungeon, enc, tick++, e =>
                {
                    if (e.Type == "pull_clear" && e.Get("pullId") is JsString id)
                    {
                        cleared.Add(id.Value);
                    }
                });
            }
        }
        foreach (var pull in dungeon.Pulls.Where(p => p.Branch == taken).Append(dungeon.Pulls[^1]))
        {
            enc = enc with { Engaged = enc.Engaged.Append(pull.Id).ToList() };
            Settle();
            foreach (var s in pull.Spawns)
            {
                state.Actors[s.ActorId].Alive = false;
            }
            Settle();
        }
        return (enc, cleared);
    }

    [Fact(DisplayName = "Branch pull order: seeds 1-50, either branch cleared then the boss ends the pull order, one clear per killed pull")]
    public void Pull_order_is_branch_agnostic()
    {
        for (long seed = 1; seed <= 50; seed++)
        {
            var dungeon = DungeonOf(seed);
            Assert.Equal(5, dungeon.Pulls.Count);
            foreach (var taken in new[] { "0", "1" })
            {
                var (end, cleared) = Clear(dungeon, taken);
                Assert.True(end.PullIndex == dungeon.Pulls.Count, $"seed {seed} branch {taken}: pull index {end.PullIndex} of {dungeon.Pulls.Count}");
                var want = dungeon.Pulls.Where(p => p.Branch == taken).Select(p => p.Id).Append("boss").ToList();
                Assert.Equal(want, cleared);
            }
        }
    }

    [Fact(DisplayName = "Branch pull order: an untagged dungeon still waits on every pull in order (hand-built runs unchanged)")]
    public void Untagged_pulls_are_never_skipped()
    {
        var dungeon = DungeonOf(1);
        var untagged = dungeon with { Pulls = dungeon.Pulls.Select(p => p with { Branch = null }).ToList() };
        var (end, cleared) = Clear(untagged, "1");
        // Only the boss was killed: the index stays on pull 0, which still stands.
        Assert.Equal(0, end.PullIndex);
        Assert.Empty(cleared);
    }
}
