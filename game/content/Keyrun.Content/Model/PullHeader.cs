using System;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// The pull header the HUD objective line shows top left (Godot-free; game/src/Hud/Objective.cs draws it): the pull's
/// number in the full content dungeon (boss last) and its player-facing display name. Content <c>teaches</c> and the
/// boss's mechanic ids are designer intent (BIBLE s9 "Each pull teaches one thing"), never player copy, so the header
/// does not carry them.
/// </summary>
/// <param name="Number">1-based position of the pull in the dungeon, boss included and last.</param>
/// <param name="Total">Pulls in the dungeon, boss included.</param>
/// <param name="Name">The pull's content display name (the boss enemy's name for the boss pull).</param>
public sealed record PullHeader(int Number, int Total, string Name)
{
    /// <summary>The sim's pull id for the boss pull.</summary>
    public const string BossPullId = "boss";

    /// <summary>The line a player reads: "Pull 1/4  The Choir".</summary>
    public string Title => FormattableString.Invariant($"Pull {Number}/{Total}  {Name}");

    /// <summary>The header for one pull of a content dungeon, or null when the dungeon has no such pull.</summary>
    /// <param name="registry">Content (the boss pull's name is its enemy's name).</param>
    /// <param name="dungeon">The content dungeon.</param>
    /// <param name="pullId">A trash pull id, or <see cref="BossPullId"/>.</param>
    /// <returns>The header, or null.</returns>
    public static PullHeader? Of(ContentRegistry registry, DungeonDef dungeon, string pullId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(dungeon);
        // R-MULTIBOSS-1 (f-82f89359): a multi-segment dungeon has a boss pull per segment (boss-s1, boss-s2, ..., boss), each
        // named for its segment's boss and numbered after the trash pulls; every other dungeon has the one boss pull.
        var bossIds = LayoutDef.BossPullIds(dungeon);
        var total = dungeon.Pulls.Count + bossIds.Count;
        var index = dungeon.Pulls.Select(p => p.Id).ToList().IndexOf(pullId);
        if (index >= 0)
        {
            return new PullHeader(index + 1, total, dungeon.Pulls[index].Name);
        }
        var bossAt = dungeon.Segments.Count > 1 ? bossIds.ToList().IndexOf(pullId) : -1;
        if (bossAt >= 0 && registry.Enemies.TryGetValue(dungeon.Segments[bossAt].Boss.EnemyId, out var segmentBoss))
        {
            return new PullHeader(dungeon.Pulls.Count + bossAt + 1, total, segmentBoss.Name);
        }
        if (pullId == BossPullId && registry.Enemies.TryGetValue(dungeon.Boss.EnemyId, out var boss))
        {
            return new PullHeader(total, total, boss.Name);
        }
        return null;
    }
}
