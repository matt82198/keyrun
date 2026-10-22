using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>
/// Dungeon references, plus the D65/D66 dungeon shape: segments that each end at a boss (the last one at
/// <see cref="DungeonDef.Boss"/>), at least <see cref="DungeonDef.MinSegmentsPastTutorial"/> of them past the tutorial,
/// and a pack pool that names this dungeon's roster packs (or, for the tutorial, its layout packs). Every roster pack
/// belongs to a known dungeon, teaches something, fields known enemies, and pairs its chain partners both ways.
/// Registered in <see cref="ContentRules"/>.
/// </summary>
internal sealed class DungeonsRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        foreach (DungeonDef dungeon in reg.Dungeons.Values)
        {
            foreach (PullDef pull in dungeon.Pulls)
                foreach (PullEnemy enemy in pull.Enemies)
                    if (!reg.Enemies.ContainsKey(enemy.EnemyId))
                        Fail("dungeon \"" + dungeon.Id + "\" pull \"" + pull.Id + "\" references unknown enemy \"" + enemy.EnemyId + "\"");
            if (!reg.Enemies.ContainsKey(dungeon.Boss.EnemyId))
                Fail("dungeon \"" + dungeon.Id + "\" boss references unknown enemy \"" + dungeon.Boss.EnemyId + "\"");
            CheckSegments(reg, dungeon);
            CheckPool(reg, dungeon);
        }
        foreach (RosterPack pack in reg.Roster.Values)
            CheckRosterPack(reg, pack);
    }

    private static void CheckSegments(ContentRegistry reg, DungeonDef dungeon)
    {
        string where = "dungeon \"" + dungeon.Id + "\"";
        if (dungeon.Segments.Count == 0) Fail(where + " has no segments");
        if (!dungeon.Tutorial && dungeon.Segments.Count < DungeonDef.MinSegmentsPastTutorial)
            Fail(where + " is not the tutorial and has " + dungeon.Segments.Count + " segments, needs at least " + DungeonDef.MinSegmentsPastTutorial + " (one boss each)");
        var ids = new HashSet<string>(System.StringComparer.Ordinal);
        var bosses = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (DungeonSegment segment in dungeon.Segments)
        {
            if (!ids.Add(segment.Id)) Fail(where + " repeats segment id \"" + segment.Id + "\"");
            if (!reg.Enemies.TryGetValue(segment.Boss.EnemyId, out EnemyDef? boss))
                Fail(where + " segment \"" + segment.Id + "\" boss references unknown enemy \"" + segment.Boss.EnemyId + "\"");
            else if (boss.Role != EnemyRole.Boss)
                Fail(where + " segment \"" + segment.Id + "\" boss \"" + boss.Id + "\" has role " + boss.Role.ToString().ToLowerInvariant() + ", not boss");
            if (!bosses.Add(segment.Boss.EnemyId)) Fail(where + " fights boss \"" + segment.Boss.EnemyId + "\" in two segments");
            if (segment.Boss.Mechanics.Count == 0) Fail(where + " segment \"" + segment.Id + "\" boss names no mechanics");
        }
        if (dungeon.Segments[^1].Boss != dungeon.Boss)
            Fail(where + " boss \"" + dungeon.Boss.EnemyId + "\" is not its last segment's boss \"" + dungeon.Segments[^1].Boss.EnemyId + "\"");
    }

    private static void CheckPool(ContentRegistry reg, DungeonDef dungeon)
    {
        string where = "dungeon \"" + dungeon.Id + "\"";
        if (!dungeon.Tutorial && dungeon.PackPool.Count == 0) Fail(where + " is seeded and has an empty pack pool");
        var layoutPacks = string.Equals(reg.Layout.Dungeon, dungeon.Id, System.StringComparison.Ordinal)
            ? new HashSet<string>(reg.Layout.Packs.Select(p => p.Id), System.StringComparer.Ordinal)
            : new HashSet<string>(System.StringComparer.Ordinal);
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (string id in dungeon.PackPool)
        {
            if (!seen.Add(id)) Fail(where + " pack pool repeats \"" + id + "\"");
            bool roster = reg.Roster.TryGetValue(id, out RosterPack? pack) && pack is not null && string.Equals(pack.Dungeon, dungeon.Id, System.StringComparison.Ordinal);
            if (!roster && !layoutPacks.Contains(id))
                Fail(where + " pack pool names \"" + id + "\", which is neither one of its roster packs nor one of its layout packs");
        }
    }

    private static void CheckRosterPack(ContentRegistry reg, RosterPack pack)
    {
        string where = "roster pack \"" + pack.Id + "\"";
        if (!reg.Dungeons.ContainsKey(pack.Dungeon)) Fail(where + " names unknown dungeon \"" + pack.Dungeon + "\"");
        if (string.IsNullOrWhiteSpace(pack.Teaches)) Fail(where + " teaches nothing");
        if (pack.AggroMm <= 0) Fail(where + " aggroMm must be positive, got " + pack.AggroMm);
        if (pack.Enemies.Count == 0) Fail(where + " has no enemies");
        foreach (PullEnemy group in pack.Enemies)
        {
            if (!reg.Enemies.TryGetValue(group.EnemyId, out EnemyDef? enemy)) Fail(where + " references unknown enemy \"" + group.EnemyId + "\"");
            else if (enemy.Role == EnemyRole.Boss) Fail(where + " fields boss \"" + enemy.Id + "\"");
            if (group.Count <= 0) Fail(where + " enemy \"" + group.EnemyId + "\" count must be positive, got " + group.Count);
        }
        foreach (string partner in pack.ChainWith ?? new ValueList<string>())
        {
            if (partner == pack.Id) Fail(where + " chains with itself");
            RosterPack? other = reg.Roster.TryGetValue(partner, out RosterPack? found) ? found : null;
            if (other is null || other.Dungeon != pack.Dungeon)
                Fail(where + " chains with \"" + partner + "\", which is not a roster pack of \"" + pack.Dungeon + "\"");
            else if (other.ChainWith?.Contains(pack.Id) != true)
                Fail(where + " chains with \"" + partner + "\", which does not chain back");
        }
    }
}
