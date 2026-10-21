using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// PATH-PACKS (finding f-82d9390e; BIBLE section 9; Matt 2026-10-20 "no more gates, just path to boss with packs as you
/// said" and "honestly just lean into m+ design heavily while in the dungeon"): the Hollow Vault is a forward path with
/// trash on it. Walking the line from the start, the run meets a teaching pull, then a required pack standing on the
/// line, then the next teaching pull, and the boss last. The line alone (teaching pulls plus required packs) leaves the
/// forces bar short, so the bar reaches 100% only once the party pulls enough packs, and any one optional pack is
/// enough. The validator fails closed on a required pack off the line and on a line that fills the bar by itself.
/// </summary>
public sealed class PathPackTests
{
    private const string LayoutFile = "layout.json";

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    private static JsonObject Pack(JsonNode root, string id) =>
        root["packs"]!.AsArray().Select(p => p!.AsObject()).First(p => (string)p["id"]! == id);

    private static List<LayoutPoint> Line(LayoutDef layout) => new[] { layout.Start }.Concat(layout.Route).ToList();

    /// <summary>Distance along the walking line of the point nearest <paramref name="p"/>, and how far off the line it is (test-side doubles).</summary>
    private static (double Arc, double Off) Project(IReadOnlyList<LayoutPoint> line, LayoutPoint p)
    {
        double bestArc = 0, bestOff = double.MaxValue, walked = 0;
        for (int k = 0; k + 1 < line.Count; k++)
        {
            LayoutPoint a = line[k], b = line[k + 1];
            double abx = b.X - a.X, abz = b.Z - a.Z, len = Math.Sqrt((abx * abx) + (abz * abz));
            double t = len == 0 ? 0 : Math.Clamp((((p.X - a.X) * abx) + ((p.Z - a.Z) * abz)) / (len * len), 0, 1);
            double dx = p.X - (a.X + (t * abx)), dz = p.Z - (a.Z + (t * abz)), off = Math.Sqrt((dx * dx) + (dz * dz));
            if (off < bestOff)
            {
                bestOff = off;
                bestArc = walked + (t * len);
            }
            walked += len;
        }
        return (bestArc, bestOff);
    }

    /// <summary>Where each pull's first group stands (room origin plus its offset; the boss at its origin).</summary>
    private static Dictionary<string, LayoutPoint> PullPoints(ContentRegistry reg)
    {
        LayoutDef layout = reg.Layout;
        DungeonDef dungeon = reg.Dungeons[layout.Dungeon];
        var points = new Dictionary<string, LayoutPoint>(StringComparer.Ordinal);
        foreach (LayoutBox room in layout.Boxes.Where(b => b.Pull is not null))
        {
            PullDef? pull = dungeon.Pulls.FirstOrDefault(p => p.Id == room.Pull);
            int dx = pull?.Enemies[0].Spawn.X ?? 0, dz = pull?.Enemies[0].Spawn.Z ?? 0;
            points[room.Pull!] = new LayoutPoint { X = room.Origin!.X + dx, Z = room.Origin.Z + dz };
        }
        return points;
    }

    [Fact]
    public void The_shipped_layout_has_a_required_pack_standing_on_the_walking_line()
    {
        LayoutDef layout = ContentLoader.Load().Layout;
        var line = Line(layout);
        var required = layout.Packs.Where(p => !p.Optional).ToList();
        Assert.NotEmpty(required);
        foreach (LayoutPack pack in required)
        {
            double off = Project(line, pack.Center).Off;
            Assert.True(off <= pack.AggroMm, $"required pack {pack.Id} is {off:F0} mm off the walking line, outside its {pack.AggroMm} mm ring");
        }
    }

    [Fact]
    public void Walking_the_line_meets_a_pull_then_a_pack_then_a_pull_and_the_boss_last()
    {
        ContentRegistry reg = ContentLoader.Load();
        LayoutDef layout = reg.Layout;
        var line = Line(layout);
        var stops = PullPoints(reg).Select(kv => (Id: kv.Key, Pack: false, Arc: Project(line, kv.Value).Arc))
            .Concat(layout.Packs.Where(p => !p.Optional).Select(p => (Id: p.Id, Pack: true, Arc: Project(line, p.Center).Arc)))
            .OrderBy(s => s.Arc)
            .ToList();
        string order = string.Join(" -> ", stops.Select(s => s.Id));

        Assert.False(stops[0].Pack, "the route opens on a pack, not a teaching pull: " + order);
        Assert.Equal(LayoutDef.BossPullId, stops[^1].Id);
        var pulls = stops.Where(s => !s.Pack).Select(s => s.Id).ToList();
        Assert.Equal(reg.Dungeons[layout.Dungeon].Pulls.Select(p => p.Id).Append(LayoutDef.BossPullId), pulls);
        bool sandwiched = Enumerable.Range(1, stops.Count - 2).Any(i => stops[i].Pack && !stops[i - 1].Pack && !stops[i + 1].Pack && stops[i + 1].Id != LayoutDef.BossPullId);
        Assert.True(sandwiched, "no required pack stands between two teaching pulls: " + order);
    }

    [Fact]
    public void The_forces_bar_fills_only_with_enough_packs()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable forces = reg.Forces;
        LayoutDef layout = reg.Layout;
        int teaching = RouteForces.CriticalPathPoints(forces, reg.Dungeons[layout.Dungeon]);
        int line = teaching + layout.Packs.Where(p => !p.Optional).Sum(p => RouteForces.LayoutPackPoints(forces, p));
        Assert.Contains(layout.Packs, p => !p.Optional);
        Assert.True(line > teaching, "the required packs add no forces");
        Assert.True(line < forces.Required, $"the walking line alone banks {line} of {forces.Required}: no optional pack is ever needed");
        var optional = layout.Packs.Where(p => p.Optional).ToList();
        Assert.NotEmpty(optional);
        foreach (LayoutPack pack in optional)
        {
            int with = line + RouteForces.LayoutPackPoints(forces, pack);
            Assert.True(with >= forces.Required, $"the line plus {pack.Id} banks {with} of {forces.Required}");
        }
    }

    [Fact]
    public void A_required_pack_off_the_walking_line_is_rejected()
    {
        string id = ContentLoader.Load().Layout.Packs.First(p => !p.Optional).Id;
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Pack(n, id)["center"]!["z"] = 4500));
        Assert.Contains("required pack", ex.Message);
        Assert.Contains("walking line", ex.Message);
    }

    [Fact]
    public void A_walking_line_that_fills_the_bar_by_itself_is_rejected()
    {
        string id = ContentLoader.Load().Layout.Packs.First(p => !p.Optional).Id;
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n =>
            Pack(n, id)["enemies"]!.AsArray()[0]!["count"] = 40));
        Assert.Contains("without an optional pack", ex.Message);
    }
}
