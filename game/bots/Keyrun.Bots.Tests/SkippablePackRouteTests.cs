using System;
using System.IO;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Encounter;
using Keyrun.Tools.Replay;
using Xunit;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// SKIPPABLE-PACKS on the sim side (Matt 2026-10-20 "no more gates, just path to boss with packs"): the shipped layout,
/// optional packs included, arms a run whose pull order is the teaching pulls and the boss only, and whose Warden arena
/// opens once the Seed is cleared with no pack killed. A pack is never a pull, so the encounter never waits on one.
/// </summary>
public sealed class SkippablePackRouteTests
{
    private static (LayoutDef Layout, NavLayout Nav, SimDungeon Dungeon) Arm()
    {
        var layout = ContentLoader.Load().Layout;
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), 1);
        return (layout, NavLayouts.ToSim(layout), NavLayouts.PlaceDungeon(layout, setup.Dungeon));
    }

    [Fact]
    public void The_armed_run_pulls_only_the_teaching_pulls_and_the_boss()
    {
        var (layout, _, dungeon) = Arm();
        Assert.Contains(layout.Packs, p => p.Optional);
        Assert.Equal(new[] { "choir", "pair", "seed", LayoutDef.BossPullId }, dungeon.Pulls.Select(p => p.Id).ToArray());
        Assert.DoesNotContain(dungeon.Pulls, p => layout.Packs.Any(k => string.Equals(k.Id, p.Id, StringComparison.Ordinal)));
    }

    [Fact]
    public void The_boss_arena_opens_on_the_seed_clear_with_every_optional_pack_skipped()
    {
        var (layout, nav, dungeon) = Arm();
        Assert.Contains(layout.Packs, p => p.Optional);
        int bossIndex = dungeon.Pulls.Select(p => p.Id).ToList().IndexOf(LayoutDef.BossPullId);
        NavBox corridor = nav.Boxes.First(b => b.Id == "corridor-4");
        NavBox arena = nav.Boxes.First(b => b.Id == "warden-arena");

        // Pull index = boss: choir, pair and seed are cleared; no pack has been touched.
        Assert.True(NavLayout.IsOpen(corridor, dungeon, bossIndex));
        Assert.True(NavLayout.IsOpen(arena, dungeon, bossIndex));
        Assert.All(nav.Boxes, b => Assert.True(NavLayout.IsOpen(b, dungeon, bossIndex), b.Id + " is still shut at the boss"));
        // Whatever gates remain (PR #308 removes them), none waits on a pack.
        Assert.DoesNotContain(nav.Boxes, b => b.GatePullId is { } g && layout.Packs.Any(p => string.Equals(p.Id, g, StringComparison.Ordinal)));
    }
}
