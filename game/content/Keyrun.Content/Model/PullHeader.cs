using System;
using System.Collections.Generic;
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

    /// <summary>
    /// The pull ids of a content dungeon in route order (the order a <c>--pull N</c> start counts): the trash pulls, then
    /// every boss pull in fight order (R-MULTIBOSS-1: one per segment, the final boss last).
    /// </summary>
    /// <param name="dungeon">The content dungeon.</param>
    /// <returns>The ids, first pull first.</returns>
    public static IReadOnlyList<string> RouteOrder(DungeonDef dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        return dungeon.Pulls.Select(p => p.Id).Concat(LayoutDef.BossPullIds(dungeon)).ToList();
    }

    /// <summary>The header for one pull of a content dungeon, or null when the dungeon has no such pull.</summary>
    /// <param name="registry">Content (a boss pull's name is its boss enemy's name).</param>
    /// <param name="dungeon">The content dungeon.</param>
    /// <param name="pullId">A trash pull id, or a boss pull id (<see cref="LayoutDef.BossPullIds"/>).</param>
    /// <returns>The header, or null.</returns>
    public static PullHeader? Of(ContentRegistry registry, DungeonDef dungeon, string pullId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(dungeon);
        var route = RouteOrder(dungeon);
        var index = route.ToList().IndexOf(pullId);
        if (index < 0)
        {
            return null;
        }
        var total = route.Count;
        if (index < dungeon.Pulls.Count)
        {
            return new PullHeader(index + 1, total, dungeon.Pulls[index].Name);
        }
        var bossEnemyId = BossEnemyIdOf(dungeon, pullId);
        return bossEnemyId is not null && registry.Enemies.TryGetValue(bossEnemyId, out var boss)
            ? new PullHeader(index + 1, total, boss.Name)
            : null;
    }

    /// <summary>The boss enemy a boss pull fights: the final boss for <see cref="BossPullId"/>, else its segment's boss.</summary>
    private static string? BossEnemyIdOf(DungeonDef dungeon, string pullId)
    {
        if (pullId == BossPullId)
        {
            return dungeon.Boss.EnemyId;
        }
        foreach (var segment in dungeon.Segments)
        {
            if (LayoutDef.BossPullPrefix + segment.Id == pullId)
            {
                return segment.Boss.EnemyId;
            }
        }
        return null;
    }
}
