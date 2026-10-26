using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class ForcesRule : IContentRule
{
    /// <summary>
    /// BIBLE section 9.2 forces + route invariants: a positive bar; every value names a known enemy once, is not
    /// negative, and is earned only by a teaching-pull or layout-pack enemy (nothing farmable); every teaching-pull and
    /// layout-pack enemy has a value; the fork precedes the rejoin; and every hazard stands in a layout room (R7: one
    /// pack layer; the packs themselves are checked by LayoutValidator.CheckPacks).
    /// </summary>
    public void Check(ContentRegistry reg)
    {
        ForcesTable forces = reg.Forces;
        ForcesTable own = reg.ForcesFor(forces.Route.DungeonId);
        if (own.Required <= 0) Fail("forces required must be positive, got " + own.Required);

        var valued = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (ForcesValue value in forces.Values)
        {
            if (!reg.Enemies.ContainsKey(value.EnemyId)) Fail("forces value references unknown enemy \"" + value.EnemyId + "\"");
            if (!valued.Add(value.EnemyId)) Fail("duplicate forces value for enemy \"" + value.EnemyId + "\"");
            if (value.Points < 0) Fail("forces value for enemy \"" + value.EnemyId + "\" is negative, got " + value.Points);
        }

        RouteDef route = forces.Route;
        if (!reg.Dungeons.TryGetValue(route.DungeonId, out DungeonDef? dungeon))
            Fail("route references unknown dungeon \"" + route.DungeonId + "\"");
        var earners = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (PullDef pull in dungeon!.Pulls)
            foreach (PullEnemy group in pull.Enemies)
            {
                if (!valued.Contains(group.EnemyId))
                    Fail("pull \"" + pull.Id + "\" enemy \"" + group.EnemyId + "\" has no forces value");
                earners.Add(group.EnemyId);
            }

        List<string> pullIds = dungeon.Pulls.Select(p => p.Id).ToList();
        int fork = pullIds.IndexOf(route.ForkAfterPull);
        int rejoin = pullIds.IndexOf(route.RejoinPull);
        if (fork < 0) Fail("route forkAfterPull \"" + route.ForkAfterPull + "\" is not a pull of \"" + dungeon.Id + "\"");
        if (rejoin < 0) Fail("route rejoinPull \"" + route.RejoinPull + "\" is not a pull of \"" + dungeon.Id + "\"");
        if (rejoin <= fork)
            Fail("route rejoinPull \"" + route.RejoinPull + "\" must come after forkAfterPull \"" + route.ForkAfterPull + "\"");

        // SKIPPABLE-PACKS: a layout pack's members earn too (LayoutValidator checks each has a value).
        foreach (LayoutPack pack in reg.Layout.Packs)
            foreach (PullEnemy member in pack.Enemies)
                earners.Add(member.EnemyId);

        // R-PATROL-4: a layout patrol's members earn too (LayoutValidator checks each has a value).
        foreach (LayoutPatrol patrol in reg.Layout.Patrols)
            foreach (PullEnemy member in patrol.Enemies)
                earners.Add(member.EnemyId);

        // D46/D66: a seeded dungeon's roster pack members earn too, and each must carry a value (R-PACKS-7: forces ship
        // with the packs that place them, so the fill prices a roster pack from its members alone).
        foreach (RosterPack pack in reg.Roster.Values)
            foreach (PullEnemy member in pack.Enemies)
            {
                if (!valued.Contains(member.EnemyId))
                    Fail("roster pack \"" + pack.Id + "\" enemy \"" + member.EnemyId + "\" has no forces value");
                earners.Add(member.EnemyId);
            }

        foreach (ForcesValue value in forces.Values)
            if (value.Points > 0 && !earners.Contains(value.EnemyId))
                Fail("enemy \"" + value.EnemyId + "\" is worth " + value.Points + " forces but is in no pull or pack");

        // R7: the only pack layer is layout.json, so a hazard's room is one of its Room boxes.
        var rooms = new HashSet<string>(reg.Layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Room).Select(b => b.Id), System.StringComparer.Ordinal);
        foreach (HazardDef hazard in reg.Hazards.Values)
            if (!rooms.Contains(hazard.Room)) Fail("hazard \"" + hazard.Id + "\" is in unknown room \"" + hazard.Room + "\"");
    }
}
