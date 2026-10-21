using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// S4 SKIP-BAR (game/design/FORCES-ROUTE-PLAN.md, ruling R-PACKS-1; finding f-82d9390e): on one forward path the route
/// choice is which optional packs to pull, so the load fails unless at least two DISJOINT sets of optional packs each
/// carry the walking line (teaching pulls plus required packs) to the forces bar. One reaching set alone would make
/// every route the same route. The fixtures are built at runtime from the shipped content: the bar is moved to the
/// exact balance point of the shipped optional packs (loads) and one point past it (fails), so the pair keeps biting
/// whatever layout.json ships.
/// </summary>
public sealed class SkipBarTests
{
    private const string ForcesFile = "forces.json";

    /// <summary>The shipped walking line, optional pack points, and their best balanced split (test-side oracle).</summary>
    private static (int Line, List<int> Optional, int Balanced) Shipped()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable forces = reg.Forces;
        DungeonDef dungeon = reg.Dungeons[reg.Layout.Dungeon];
        int line = RouteForces.CriticalPathPoints(forces, dungeon)
            + reg.Layout.Packs.Where(p => !p.Optional).Sum(p => RouteForces.LayoutPackPoints(forces, p));
        List<int> optional = reg.Layout.Packs.Where(p => p.Optional).Select(p => RouteForces.LayoutPackPoints(forces, p)).ToList();
        Assert.InRange(optional.Count, 2, 20);
        int all = optional.Sum();
        // Best balanced split: the largest min(side A, side B) over every assignment of packs to two sides.
        int balanced = 0;
        for (int mask = 0; mask < (1 << optional.Count); mask++)
        {
            int a = 0;
            for (int i = 0; i < optional.Count; i++)
                if ((mask & (1 << i)) != 0) a += optional[i];
            balanced = Math.Max(balanced, Math.Min(a, all - a));
        }
        return (line, optional, balanced);
    }

    private static MutableSource WithBar(int required) =>
        new MutableSource().Edit(ForcesFile, n => n["required"] = required);

    [Fact]
    public void A_layout_without_two_disjoint_pack_sets_that_reach_the_bar_fails_the_load()
    {
        (int line, List<int> optional, int balanced) = Shipped();
        int bar = line + balanced + 1;
        // The fixture must leave every older rule passing, so only the skip bar can reject it: each optional pack is
        // still skippable (all packs minus the biggest reach the bar) and the walking line alone falls short.
        Assert.True(line + optional.Sum() - optional.Max() >= bar,
            $"fixture bar {bar} would trip the skippable rule first (line {line}, packs {string.Join("+", optional)})");
        Assert.True(line < bar);

        var ex = Assert.Throws<ContentLoadException>(() => ContentLoader.Load(WithBar(bar).Build()));
        Assert.Contains("no two disjoint optional pack sets", ex.Message);
    }

    [Fact]
    public void A_layout_with_two_disjoint_pack_sets_that_reach_the_bar_loads()
    {
        (int line, _, int balanced) = Shipped();
        ContentRegistry reg = ContentLoader.Load(WithBar(line + balanced).Build());
        Assert.Equal(line + balanced, reg.Forces.Required);
    }

    [Fact]
    public void The_shipped_layout_has_two_disjoint_pack_sets_that_reach_the_bar()
    {
        (int line, _, int balanced) = Shipped();
        Assert.True(line + balanced >= ContentLoader.Load().Forces.Required,
            $"walking line {line} plus the smaller side of the best split {balanced} is short of the bar");
    }
}
