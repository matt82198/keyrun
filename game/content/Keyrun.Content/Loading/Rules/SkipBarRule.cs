using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class SkipBarRule : IContentRule
{
    /// <summary>
    /// S4 SKIP-BAR (FORCES-ROUTE-PLAN ruling R-PACKS-1): on one path the route choice is which optional packs to pull, so at
    /// least two DISJOINT sets of optional packs must each carry the walking line (teaching pulls plus required packs) to
    /// the forces bar. Points are never negative, so that holds exactly when some split of the optional packs into two
    /// sides has both sides reaching; an integer subset-sum over the optional pack points decides it.
    /// </summary>
    public void Check(ContentRegistry reg)
    {
        LayoutDef layout = reg.Layout;
        if (!layout.Packs.Any(p => p.Optional)) return;
        ForcesTable forces = reg.ForcesFor(layout.Dungeon);
        DungeonDef dungeon = reg.Dungeons[layout.Dungeon];
        int line = RouteForces.CriticalPathPoints(forces, dungeon)
            + layout.Packs.Where(p => !p.Optional).Sum(p => RouteForces.LayoutPackPoints(forces, p));
        int need = forces.Required - line;
        if (need <= 0) return;

        var sums = new HashSet<int> { 0 };
        int all = 0;
        foreach (LayoutPack pack in layout.Packs.Where(p => p.Optional))
        {
            int points = RouteForces.LayoutPackPoints(forces, pack);
            all += points;
            foreach (int s in sums.ToList()) sums.Add(s + points);
        }
        int best = sums.Max(s => System.Math.Min(s, all - s));
        if (best < need)
            Fail("no two disjoint optional pack sets reach the " + forces.Required + " forces required: the walking line gives " + line
                + ", the packs need " + need + " more and the best split of " + all + " gives " + best);
    }
}
