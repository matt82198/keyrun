using System;
using System.Collections.Generic;
using Keyrun.Sim.Dungeon;
using C = Keyrun.Content;

namespace Keyrun.Bots.Shared;

/// <summary>Content -> the branch fill's roster (D45/D46). STUB (red step).</summary>
public static class SeededRoster
{
    /// <summary>The dungeon's teaching pulls. STUB.</summary>
    public static IReadOnlyList<TeachingPull> Teaching(C.ContentRegistry reg) => Array.Empty<TeachingPull>();

    /// <summary>The layout's optional packs. STUB.</summary>
    public static IReadOnlyList<RosterPack> Optional(C.ContentRegistry reg) => Array.Empty<RosterPack>();

    /// <summary>The fill params from the forces table. STUB.</summary>
    public static BranchFillParams Params(C.ContentRegistry reg) => BranchFillParams.Default;
}
