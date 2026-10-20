using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// layout.json (game/content/data/layout.json; STATE.md D18, design/ART-PLAN.md section 3) loads into the registry and
/// fails closed on a layout the sim cannot run: a pull without a room, a trigger that is not its room shrunk from the
/// door (so outside its room), a spawn on top of the door, a gate before its pull, a broken route or chain.
/// </summary>
public sealed class LayoutConfigTests
{
    private const string LayoutFile = "layout.json";

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    private static JsonObject Box(JsonNode root, string id) =>
        root["boxes"]!.AsArray().Select(b => b!.AsObject()).First(b => (string)b["id"]! == id);

    [Fact]
    public void Loads_the_hollow_vault_layout()
    {
        LayoutDef layout = ContentLoader.Load().Layout;
        Assert.Equal("hollow-vault", layout.Dungeon);
        Assert.Equal(
            new[] { "entrance", "corridor-1", "choir-room", "corridor-2", "pair-hall", "corridor-3", "seed-chamber", "corridor-4", "warden-arena" },
            layout.Boxes.Select(b => b.Id).ToArray());
        Assert.Equal(new[] { "choir", "pair", "seed", "boss" }, layout.Boxes.Where(b => b.Pull is not null).Select(b => b.Pull!).ToArray());
        Assert.Equal(new[] { "choir", "pair", "seed" }, layout.Boxes.Where(b => b.Gate is not null).Select(b => b.Gate!).ToArray());
        Assert.Equal(layout.Boxes.Count, layout.Route.Count);
        Assert.Contains(LayoutFile, ContentLoader.Files);
    }

    [Fact]
    public void A_pull_without_a_room_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Box(n, "pair-hall").Remove("pull")));
        Assert.Contains("layout", ex.Message);
    }

    [Fact]
    public void A_trigger_outside_its_room_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Box(n, "choir-room")["trigger"]!["maxX"] = 27000));
        Assert.Contains("trigger must be its room shrunk 2000 mm", ex.Message);
    }

    [Fact]
    public void A_spawn_near_the_door_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Box(n, "seed-chamber")["origin"]!["x"] = 58000));
        Assert.Contains("from the door", ex.Message);
    }

    [Fact]
    public void A_spawn_reachable_within_6_m_on_the_trigger_tick_is_rejected()
    {
        // 9000 mm from the door is inside 8..12 m, but only 7 m past the trigger edge: a dash at the trigger tick lands 3.8 m away.
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Box(n, "warden-arena")["origin"]!["x"] = 91000));
        Assert.Contains("within 6000 mm", ex.Message);
    }

    [Fact]
    public void A_gate_on_a_later_pull_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Box(n, "corridor-2")["gate"] = "seed"));
        Assert.Contains("whose room is not before it", ex.Message);
    }

    [Fact]
    public void A_route_point_outside_its_doorway_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => n["route"]![2]!["x"] = 20000));
        Assert.Contains("route point 2", ex.Message);
    }

    [Fact]
    public void A_gap_between_boxes_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Box(n, "corridor-3")["minX"] = 57000));
        Assert.Contains("does not overlap", ex.Message);
    }

    [Fact]
    public void The_warden_arena_sets_the_boss_aggro_radius_and_no_other_room_does()
    {
        LayoutDef layout = ContentLoader.Load().Layout;
        Assert.Equal(7100, layout.Boxes.Single(b => b.Id == "warden-arena").AggroMm);
        Assert.All(layout.Boxes.Where(b => b.Id != "warden-arena"), b => Assert.Null(b.AggroMm));
    }

    [Fact]
    public void A_non_positive_aggro_radius_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Box(n, "warden-arena")["aggroMm"] = 0));
        Assert.Contains("aggroMm must be positive", ex.Message);
    }

    [Fact]
    public void An_aggro_radius_on_a_box_with_no_pull_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Box(n, "corridor-4")["aggroMm"] = 7100));
        Assert.Contains("aggroMm but no pull", ex.Message);
    }

    [Fact]
    public void Missing_layout_json_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Remove(LayoutFile));
        Assert.Contains("missing file \"layout.json\"", ex.Message);
    }
}
