using System;
using System.Collections.Generic;

namespace Keyrun.Sim.Dungeon;

/// <summary>A teaching pull the branch fill places (the dungeon's pulls before the boss, in dungeon order).</summary>
/// <param name="Id">Pull id, e.g. "choir".</param>
/// <param name="PullIndex">Its index in the dungeon's pull list (0-based).</param>
/// <param name="Points">Its forces points (sum of its members' values).</param>
public sealed record TeachingPull(string Id, int PullIndex, int Points);

/// <summary>The branch fill's tunables. Integers only.</summary>
public sealed record BranchFillParams
{
    /// <summary>The default parameters.</summary>
    public static readonly BranchFillParams Default = new();

    /// <summary>The forces bar (BIBLE section 9.2: 1000 points = 100%).</summary>
    public int ForcesRequired { get; init; } = 1000;

    /// <summary>What each branch carries at least, basis points of <see cref="ForcesRequired"/> (12000 = 120%).</summary>
    public int BranchTargetBp { get; init; } = 12000;

    /// <summary>Points each branch carries at least: ceil(required * bp / 10000).</summary>
    public int TargetPoints => 0;
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
    /// <summary>A placement id unique in the dungeon: pack id, room and slot.</summary>
    public string InstanceId => string.Empty;
}

/// <summary>One branch: its rooms from the entrance's successor to the room before the boss.</summary>
/// <param name="Index">0 or 1 (the entrance's successors in ascending room id).</param>
/// <param name="Rooms">Room ids in walking order.</param>
/// <param name="SecondPull">The teaching pull dealt to this branch after the first.</param>
/// <param name="Points">Forces points placed in the branch.</param>
public sealed record DungeonBranch(int Index, IReadOnlyList<int> Rooms, string SecondPull, int Points);

/// <summary>The branch fill's output.</summary>
/// <param name="Branches">Both branches.</param>
/// <param name="Packs">Every placement, branch then room then slot order.</param>
/// <param name="PairBranch">The branch dealt the second teaching pull (the seeded draw).</param>
/// <param name="TotalPoints">Forces points across the dungeon.</param>
public sealed record BranchFillResult(IReadOnlyList<DungeonBranch> Branches, IReadOnlyList<FilledPack> Packs, int PairBranch, int TotalPoints);

/// <summary>Stage 4 for a forked dungeon (D45/D46). STUB (red step).</summary>
public static class BranchFill
{
    /// <summary>The two branches of <paramref name="graph"/>. STUB.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> Branches(RoomGraph graph) => Array.Empty<IReadOnlyList<int>>();

    /// <summary>Fills both branches. STUB.</summary>
    public static BranchFillResult Fill(long seed, RoomGraph graph, IReadOnlyList<TeachingPull> teaching, IReadOnlyList<RosterPack> optional, BranchFillParams? p = null) =>
        new(Array.Empty<DungeonBranch>(), Array.Empty<FilledPack>(), -1, 0);
}
