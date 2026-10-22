using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// SKIPPABLE-PACKS (Mythic+ route choice, BIBLE section 9.1; Matt 2026-10-20 "lean into m+ design heavily", "no more
/// gates, just path to boss with packs"): the shipped layout carries optional packs beside the walking line, a party
/// walking straight from the start to the boss passes each one outside its aggro ring, the forces bar can be filled
/// both with and without each optional pack, and the teaching pulls alone cannot fill it (so the route is a decision).
/// The validator fails closed on a pack that breaks any of those.
/// </summary>
public sealed class SkippablePackTests
{

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    private static JsonObject Pack(JsonNode root, string id) =>
        root["packs"]!.AsArray().Select(p => p!.AsObject()).First(p => (string)p["id"]! == id);

    private static int PackPoints(ForcesTable forces, LayoutPack pack) =>
        pack.Enemies.Sum(e => e.Count * forces.PointsOf(e.EnemyId));

    /// <summary>Distance from <paramref name="p"/> to the segment a-b, mm (test-side geometry, doubles are fine here).</summary>
    private static double SegmentDistance(LayoutPoint a, LayoutPoint b, LayoutPoint p)
    {
        double abx = b.X - a.X, abz = b.Z - a.Z, apx = p.X - a.X, apz = p.Z - a.Z;
        double len2 = (abx * abx) + (abz * abz);
        double t = len2 == 0 ? 0 : Math.Clamp(((apx * abx) + (apz * abz)) / len2, 0, 1);
        double dx = apx - (t * abx), dz = apz - (t * abz);
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    [Fact]
    public void The_shipped_layout_has_at_least_one_optional_pack()
    {
        LayoutDef layout = ContentLoader.Load().Layout;
        Assert.Contains(layout.Packs, p => p.Optional);
    }

    [Fact]
    public void A_straight_line_from_start_to_boss_passes_every_optional_pack_outside_its_ring()
    {
        LayoutDef layout = ContentLoader.Load().Layout;
        LayoutPoint boss = layout.Boxes.First(b => b.Pull == LayoutDef.BossPullId).Origin!;
        int widestSlot = layout.Formation.Max(s => Math.Abs(s.SideMm));
        var optional = layout.Packs.Where(p => p.Optional).ToList();
        Assert.NotEmpty(optional);
        foreach (LayoutPack pack in optional)
        {
            double d = SegmentDistance(layout.Start, boss, pack.Center);
            // The whole party in formation (slots up to widestSlot to the side) stays outside the ring.
            Assert.True(d > pack.AggroMm + widestSlot,
                $"pack {pack.Id} at ({pack.Center.X}, {pack.Center.Z}) is {d:F0} mm from the start-boss line; ring {pack.AggroMm} + slot {widestSlot}");
        }
    }

    [Fact]
    public void The_forces_bar_fills_both_with_and_without_each_optional_pack()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable forces = reg.Forces;
        int critical = RouteForces.CriticalPathPoints(forces, reg.Dungeons[reg.Layout.Dungeon]);
        var points = reg.Layout.Packs.ToDictionary(p => p.Id, p => PackPoints(forces, p));
        int all = critical + points.Values.Sum();
        Assert.Contains(reg.Layout.Packs, p => p.Optional);

        // Some packs are needed: the teaching pulls alone never fill the bar.
        Assert.True(critical < forces.Required, $"critical path {critical} already fills {forces.Required}");
        foreach (LayoutPack pack in reg.Layout.Packs.Where(p => p.Optional))
        {
            Assert.True(points[pack.Id] > 0, $"pack {pack.Id} is worth no forces");
            // With it: the route that pulls every pack (this one included) fills the bar.
            Assert.True(all >= forces.Required, $"with {pack.Id}: {all} of {forces.Required}");
            // Without it: every other pack still fills the bar.
            Assert.True(all - points[pack.Id] >= forces.Required, $"without {pack.Id}: {all - points[pack.Id]} of {forces.Required}");
        }
    }

    [Fact]
    public void No_gate_names_a_pack()
    {
        LayoutDef layout = ContentLoader.Load().Layout;
        var packIds = layout.Packs.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(packIds);
        Assert.DoesNotContain(layout.Boxes, b => b.Gate is { } g && packIds.Contains(g));
    }

    [Fact]
    public void An_optional_pack_whose_ring_reaches_the_walking_line_is_rejected()
    {
        string id = ContentLoader.Load().Layout.Packs.First(p => p.Optional).Id;
        var ex = LoadFails(new MutableSource().EditLayout(n => Pack(n, id)["center"]!["z"] = 2000));
        Assert.Contains("walking line", ex.Message);
    }

    [Fact]
    public void An_optional_pack_the_route_cannot_skip_is_rejected()
    {
        // Strip every other pack to one member: skipping this one now leaves the bar short. The member is an acolyte,
        // except that a pack fielding the pack-only Hall Sentry keeps one Hall Sentry (hall.sentry is in no pull, and an
        // enemy worth forces in no pull or pack is its own rejection; STATE.md D49 fixture, not a loosening).
        var layout = ContentLoader.Load().Layout;
        string keep = layout.Packs.First(p => p.Optional).Id;
        var ex = LoadFails(new MutableSource().EditLayout(n =>
        {
            foreach (JsonObject p in n["packs"]!.AsArray().Select(x => x!.AsObject()).Where(x => (string)x["id"]! != keep))
            {
                string member = p["enemies"]!.AsArray().Any(e => (string)e!["enemyId"]! == "hall.sentry") ? "hall.sentry" : "choir.melee";
                p["enemies"] = JsonNode.Parse("[{\"enemyId\":\"" + member + "\",\"count\":1,\"spawn\":{\"x\":0,\"z\":0}}]");
            }
        }));
        Assert.Contains("without optional pack", ex.Message);
    }

    [Fact]
    public void A_gate_on_a_pack_is_rejected()
    {
        string id = ContentLoader.Load().Layout.Packs.First().Id;
        var ex = LoadFails(new MutableSource().EditLayout(n =>
            n["boxes"]!.AsArray().Select(b => b!.AsObject()).First(b => (string)b["id"]! == "corridor-4")["gate"] = id));
        Assert.Contains("gated on unknown pull", ex.Message);
    }

    [Fact]
    public void A_pack_member_with_no_forces_value_is_rejected()
    {
        string id = ContentLoader.Load().Layout.Packs.First().Id;
        var ex = LoadFails(new MutableSource().EditLayout(n =>
            Pack(n, id)["enemies"]!.AsArray()[0]!["enemyId"] = "hollowWarden.boss"));
        Assert.Contains("has no forces value", ex.Message);
    }

    [Fact]
    public void A_pack_outside_its_room_is_rejected()
    {
        string id = ContentLoader.Load().Layout.Packs.First().Id;
        var ex = LoadFails(new MutableSource().EditLayout(n => Pack(n, id)["center"]!["x"] = -100000));
        Assert.Contains("outside room", ex.Message);
    }
}
