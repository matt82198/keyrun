using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class BranchTotalsRule : IContentRule
{
    /// <summary>
    /// R7 branch totals: every route through a branch (a layout room named by some pack's <c>branch</c>) must reach the
    /// forces bar with the critical path, the branch-neutral packs and that branch's own packs alone. A short branch fails
    /// naming the branch and the shortfall.
    /// </summary>
    public void Check(ContentRegistry reg)
    {
        ForcesTable forces = reg.Forces;
        DungeonDef dungeon = reg.Dungeons[forces.Route.DungeonId];
        foreach (string branch in reg.Layout.Packs.Where(p => p.Branch is not null).Select(p => p.Branch!).Distinct(System.StringComparer.Ordinal))
        {
            int total = RouteForces.BranchTotalPoints(forces, dungeon, reg.Layout.Packs, branch);
            if (total < forces.Required)
                Fail("branch \"" + branch + "\" reaches " + total + " of the " + forces.Required + " forces required (short by " + (forces.Required - total) + ")");
        }
    }
}
