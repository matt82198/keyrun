using System;
using System.Collections.Generic;

namespace Keyrun.Sim.Runs;

/// <summary>What Queue hands the host. RED stub.</summary>
public sealed record QueueRequest(string DungeonId, int KeyLevel, string ClauseId)
{
    /// <summary>The run options for the queued run.</summary>
    public RunOptions ToRunOptions() => throw new NotImplementedException();
}

/// <summary>The Key band (BIBLE s10). RED stub.</summary>
public sealed record ScreenState(string DungeonId, IReadOnlyList<string> ClauseIds, int KeyLevel, ClauseChoice Clause, bool Locked)
{
    /// <summary>Lowest key.</summary>
    public const int MinKey = 2;

    /// <summary>Highest key.</summary>
    public const int MaxKey = 20;

    /// <summary>Opens the band.</summary>
    public static ScreenState Open(string dungeonId, IReadOnlyList<string> clauseIds, int keyLevel) => throw new NotImplementedException();

    /// <summary>Steps the key.</summary>
    public ScreenState StepKey(int delta) => throw new NotImplementedException();

    /// <summary>Picks a Clause.</summary>
    public ScreenState PickClause(string clauseId) => throw new NotImplementedException();

    /// <summary>Queues.</summary>
    public (ScreenState State, QueueRequest? Request) Queue() => throw new NotImplementedException();
}
