using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// MVP-U8 (MVP-MAP G5, BIBLE sections 9.1, 9.2, 9.5; WoW Mythic+ way): the three teaching pulls and the boss are the only
/// mandatory encounters. The Hall Sentries are an OPTIONAL layout pack (layout.json, the same shape as Cantor Vigil and
/// Warrior Remnant), standing in pair-hall off the walking line so a party in formation skips it. The critical path alone
/// stays under the 1000-point bar. The content loader fails closed on a malformed layout pack entry.
/// </summary>
public sealed class OptionalBranchForcesTests
{
    private const string LayoutFile = "layout.json";
    private const string HallSentries = "hall-sentries";

    private static JsonObject Pack(JsonNode root, string id) =>
        root["packs"]!.AsArray().Select(p => p!.AsObject()).First(p => (string)p["id"]! == id);

    /// <summary>The least distance, mm, from a point to the walking line (the start, then every route point, in order).</summary>
    private static double DistanceToWalkingLine(LayoutDef layout, LayoutPoint p)
    {
        var line = new List<LayoutPoint> { layout.Start };
        line.AddRange(layout.Route);
        double best = double.MaxValue;
        for (int k = 0; k + 1 < line.Count; k++)
        {
            double ax = line[k].X, az = line[k].Z;
            double dx = line[k + 1].X - ax, dz = line[k + 1].Z - az;
            double len2 = (dx * dx) + (dz * dz);
            double t = len2 == 0 ? 0 : Math.Clamp((((p.X - ax) * dx) + ((p.Z - az) * dz)) / len2, 0, 1);
            double cx = ax + (t * dx) - p.X, cz = az + (t * dz) - p.Z;
            best = Math.Min(best, Math.Sqrt((cx * cx) + (cz * cz)));
        }
        return best;
    }

    [Fact]
    public void No_layout_pack_is_mandatory_so_the_walking_line_carries_no_trash()
    {
        LayoutDef layout = ContentLoader.Load().Layout;

        Assert.NotEmpty(layout.Packs);
        Assert.DoesNotContain(layout.Packs, p => !p.Optional);
        Assert.Contains(layout.Packs, p => p.Id == HallSentries && p.Optional);
    }

    [Fact]
    public void The_hall_sentries_are_an_optional_layout_pack_with_forces_values()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable forces = reg.Forces;
        LayoutPack sentries = reg.Layout.Packs.Single(p => p.Id == HallSentries);

        Assert.Equal("Hall Sentries", sentries.Name);
        Assert.Equal("pair-hall", sentries.Room);
        Assert.All(sentries.Enemies, e => Assert.True(forces.Has(e.EnemyId), $"{e.EnemyId} has no forces value"));
        Assert.Equal(240, RouteForces.LayoutPackPoints(forces, sentries));
    }

    [Fact]
    public void The_critical_path_alone_is_under_the_bar_and_the_teaching_pulls_stay_forward_only()
    {
        ContentRegistry reg = ContentLoader.Load();
        DungeonDef vault = reg.Dungeons[reg.Layout.Dungeon];

        Assert.Equal(new[] { "choir", "pair", "seed", "boss" }, reg.Layout.Boxes.Where(b => b.Pull is not null).Select(b => b.Pull!).ToArray());
        Assert.Equal(600, RouteForces.CriticalPathPoints(reg.Forces, vault));
        Assert.True(RouteForces.CriticalPathPoints(reg.Forces, vault) < reg.Forces.Required);
    }

    [Fact]
    public void The_critical_path_plus_at_least_one_optional_pack_reaches_the_bar()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable forces = reg.Forces;
        DungeonDef vault = reg.Dungeons[reg.Layout.Dungeon];
        int critical = RouteForces.CriticalPathPoints(forces, vault);

        // Cantor Vigil (1000) and Warrior Remnant (1020) each take the critical path to the bar on their own. The Hall
        // Sentries (240) do not: critical plus Hall Sentries is 840 (see the hand-back on the "any one optional pack" ruling).
        var fillers = reg.Layout.Packs.Where(p => p.Optional && critical + RouteForces.LayoutPackPoints(forces, p) >= forces.Required).ToList();
        Assert.NotEmpty(fillers);
        Assert.DoesNotContain(fillers, p => p.Id == HallSentries);
    }

    [Fact]
    public void Every_optional_pack_stands_off_the_walking_line_by_more_than_its_ring_and_a_formation_slot()
    {
        LayoutDef layout = ContentLoader.Load().Layout;
        int widestSlot = layout.Formation.Select(s => Math.Abs(s.SideMm)).DefaultIfEmpty(0).Max();
        var optional = layout.Packs.Where(p => p.Optional).ToList();

        Assert.NotEmpty(optional);
        Assert.All(optional, p => Assert.True(
            DistanceToWalkingLine(layout, p.Center) > p.AggroMm + widestSlot,
            $"optional pack {p.Id} is {DistanceToWalkingLine(layout, p.Center):F0} mm from the walking line; ring {p.AggroMm} + slot {widestSlot}"));
    }

    [Fact]
    public void A_layout_pack_with_an_unknown_property_fails_closed()
    {
        var ex = Assert.Throws<ContentLoadException>(() => ContentLoader.Load(new MutableSource()
            .Edit(LayoutFile, n => Pack(n, HallSentries)["ringMm"] = 3000)
            .Build()));

        Assert.Contains(LayoutFile, ex.Message);
    }

    [Fact]
    public void A_layout_pack_missing_its_aggro_radius_fails_closed()
    {
        var ex = Assert.Throws<ContentLoadException>(() => ContentLoader.Load(new MutableSource()
            .Edit(LayoutFile, n => Pack(n, HallSentries).Remove("aggroMm"))
            .Build()));

        Assert.Contains(LayoutFile, ex.Message);
    }
}
