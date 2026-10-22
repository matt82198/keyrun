using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Dungeon;

/// <summary>A teaching pull the branch fill places (the dungeon's pulls before the boss, in dungeon order).</summary>
/// <param name="Id">Pull id, e.g. "choir".</param>
/// <param name="PullIndex">Its index in the dungeon's pull list (0-based).</param>
/// <param name="Points">Its forces points (sum of its members' values).</param>
public sealed record TeachingPull(string Id, int PullIndex, int Points);

/// <summary>The branch fill's tunables. Integers only; the defaults are a first cut (D44 "tune later").</summary>
public sealed record BranchFillParams
{
    /// <summary>The default parameters.</summary>
    public static readonly BranchFillParams Default = new();

    /// <summary>The forces bar (BIBLE section 9.2: 1000 points = 100%).</summary>
    public int ForcesRequired { get; init; } = 1000;

    /// <summary>What each branch carries at least, basis points of <see cref="ForcesRequired"/> (12000 = 120%).</summary>
    public int BranchTargetBp { get; init; } = 12000;

    /// <summary>Points each branch carries at least: ceil(required * bp / 10000).</summary>
    public int TargetPoints => (int)IntMath.CeilDiv((long)ForcesRequired * BranchTargetBp, 10000);
}

/// <summary>One pack the branch fill placed.</summary>
/// <param name="RoomId">Its room.</param>
/// <param name="Branch">Its branch index (0 or 1).</param>
/// <param name="Slot">Its slot in the room (0 .. <see cref="PackSlots.MaxSlotsPerRoom"/> - 1).</param>
/// <param name="PackId">A teaching pull id or a roster pack id.</param>
/// <param name="PullIndex">The teaching pull's dungeon index, or -1 for an optional pack.</param>
/// <param name="Points">Its forces points.</param>
public sealed record FilledPack(int RoomId, int Branch, int Slot, string PackId, int PullIndex, int Points)
{
    /// <summary>A placement id unique in the dungeon: pack id, room and slot (e.g. "hall-sentries@r3s1").</summary>
    public string InstanceId => string.Create(CultureInfo.InvariantCulture, $"{PackId}@r{RoomId}s{Slot}");
}

/// <summary>One branch: its rooms from the entrance's successor to the room before the boss.</summary>
/// <param name="Index">0 or 1 (the entrance's successors in ascending room id).</param>
/// <param name="Rooms">Room ids in walking order.</param>
/// <param name="SecondPull">The teaching pull dealt to this branch after the first.</param>
/// <param name="Points">Forces points placed in the branch.</param>
public sealed record DungeonBranch(int Index, IReadOnlyList<int> Rooms, string SecondPull, int Points);

/// <summary>The branch fill's output.</summary>
/// <param name="Branches">Both branches.</param>
/// <param name="Packs">Every placement, branch then room-walk then slot order.</param>
/// <param name="PairBranch">The branch dealt the second teaching pull (the seeded draw).</param>
/// <param name="TotalPoints">Forces points across the dungeon.</param>
public sealed record BranchFillResult(IReadOnlyList<DungeonBranch> Branches, IReadOnlyList<FilledPack> Packs, int PairBranch, int TotalPoints);

/// <summary>
/// Stage 4 for a forked dungeon (STATE.md D45/D46, BIBLE section 9: forward-only, never linear, each pull teaches one
/// thing). The graph forks at the entrance into two chains that converge on the boss. Rule:
/// <list type="number">
/// <item>The first teaching pull (the Choir) opens room 1 of both branches, so either route teaches it first.</item>
/// <item>The later teaching pulls (the Pair, the Seed) are dealt one per branch into room 2, alternating from a seeded
/// branch (<see cref="BranchFillResult.PairBranch"/>), so the two routes teach different second lessons.</item>
/// <item>Optional roster packs fill the free slots, rooms past the teaching rooms first, one pack per room per pass,
/// dealt round robin from a seeded roster offset, until the branch carries <see cref="BranchFillParams.TargetPoints"/>.
/// So each branch alone reaches the bar and the party must pull most of its branch to get there.</item>
/// </list>
/// Pure over (seed, graph, roster, params); fails closed on any other graph shape.
/// </summary>
public static class BranchFill
{
    /// <summary>The PRNG stage this fill draws from (4, the fill stage; <see cref="DungeonPipeline.StageRng"/>).</summary>
    public const uint Stage = 4;

    /// <summary>
    /// The two branches of <paramref name="graph"/>: from each of the entrance's two successors (ascending id), the chain
    /// of single-successor rooms up to the boss.
    /// </summary>
    /// <exception cref="ArgumentException">The entrance does not fork in two, a branch room forks or rejoins, or a room lies on neither branch.</exception>
    public static IReadOnlyList<IReadOnlyList<int>> Branches(RoomGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var roots = graph.Successors(graph.EntranceId);
        if (roots.Count != 2)
        {
            throw new ArgumentException($"branch fill: the entrance must fork into two branches (D45), it has {roots.Count} exits", nameof(graph));
        }
        var branches = new List<IReadOnlyList<int>>();
        foreach (var root in roots)
        {
            var chain = new List<int>();
            var at = root;
            while (at != graph.BossId)
            {
                if (graph.Nodes[at].Kind != RoomKind.Trash || graph.Predecessors(at).Count != 1 || graph.Successors(at).Count != 1)
                {
                    throw new ArgumentException($"branch fill: room {at} forks, rejoins or is no trash room; each branch must be one chain to the boss", nameof(graph));
                }
                chain.Add(at);
                at = graph.Successors(at)[0];
            }
            branches.Add(chain);
        }
        if (branches.Sum(b => b.Count) != graph.Nodes.Count - 2)
        {
            throw new ArgumentException("branch fill: a room lies on neither branch", nameof(graph));
        }
        return branches;
    }

    /// <summary>Fills both branches of <paramref name="graph"/>.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="graph">A D45 graph (see <see cref="Branches"/>).</param>
    /// <param name="teaching">The teaching pulls in dungeon order: the first opens both branches, then one per branch.</param>
    /// <param name="optional">The optional pack roster.</param>
    /// <param name="p">Params, or null for <see cref="BranchFillParams.Default"/>.</param>
    /// <returns>The fill.</returns>
    /// <exception cref="ArgumentException">A bad graph shape, a teaching list that is not 1 + one per branch, a bad roster, or a branch too short for its pulls.</exception>
    /// <exception cref="InvalidOperationException">A branch whose slots cannot carry the target.</exception>
    public static BranchFillResult Fill(long seed, RoomGraph graph, IReadOnlyList<TeachingPull> teaching, IReadOnlyList<RosterPack> optional, BranchFillParams? p = null)
    {
        ArgumentNullException.ThrowIfNull(teaching);
        p ??= BranchFillParams.Default;
        var branches = Branches(graph);
        PackSlots.ValidateRoster(optional);
        if (teaching.Count != 1 + branches.Count)
        {
            throw new ArgumentException($"branch fill: need {1 + branches.Count} teaching pulls (one shared, one per branch), got {teaching.Count}", nameof(teaching));
        }
        if (branches.Any(b => b.Count < 2))
        {
            throw new ArgumentException("branch fill: every branch needs two rooms for its teaching pulls", nameof(graph));
        }
        var target = p.TargetPoints;
        var rng = DungeonPipeline.StageRng(seed, Stage);
        var pairBranch = Draw(ref rng, branches.Count);

        var packs = new List<FilledPack>();
        var result = new List<DungeonBranch>();
        for (var b = 0; b < branches.Count; b++)
        {
            var rooms = branches[b];
            var second = teaching[1 + ((b - pairBranch + branches.Count) % branches.Count)];
            var placed = new List<FilledPack>
            {
                new(rooms[0], b, 0, teaching[0].Id, teaching[0].PullIndex, teaching[0].Points),
                new(rooms[1], b, 0, second.Id, second.PullIndex, second.Points),
            };
            var used = rooms.ToDictionary(r => r, r => placed.Count(x => x.RoomId == r));
            var points = placed.Sum(x => x.Points);
            var order = rooms.Skip(2).Concat(rooms.Take(2)).ToList();
            var next = Draw(ref rng, optional.Count);
            while (points < target)
            {
                var open = order.Where(r => used[r] < PackSlots.MaxSlotsPerRoom).ToList();
                if (open.Count == 0)
                {
                    throw new InvalidOperationException($"branch fill: branch {b} carries {points} of {target} with every slot full");
                }
                foreach (var room in open)
                {
                    if (points >= target)
                    {
                        break;
                    }
                    var pick = optional[next % optional.Count];
                    next++;
                    placed.Add(new FilledPack(room, b, used[room], pick.Id, -1, pick.Points));
                    used[room]++;
                    points += pick.Points;
                }
            }
            packs.AddRange(placed.OrderBy(x => rooms.ToList().IndexOf(x.RoomId)).ThenBy(x => x.Slot));
            result.Add(new DungeonBranch(b, rooms, second.Id, points));
        }
        return new BranchFillResult(result, packs, pairBranch, packs.Sum(x => x.Points));
    }

    private static int Draw(ref PrngState rng, int n)
    {
        var (value, next) = Prng.NextInt(rng, n);
        rng = next;
        return (int)value;
    }
}
