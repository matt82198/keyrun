using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// MPLUS-PATROLS: layout.json <c>patrols</c> loads into <see cref="LayoutDef.Patrols"/> (the shipped corridor-2 patrol
/// between the Choir and the Pair) and fails closed on a patrol the sim cannot walk exactly or that would not bank
/// forces: a diagonal leg, a leg outside every box, an enemy with no forces value, a non-positive speed, a pull's id.
/// </summary>
public sealed class LayoutPatrolTests
{
    private const string LayoutFile = "layout.json";

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    private static JsonObject Patrol(JsonNode root) => root["patrols"]!.AsArray()[0]!.AsObject();

    [Fact(DisplayName = "Layout patrols: the shipped layout has one patrol, two choir.melee walking corridor-2 between the Choir and the Pair at 50 mm/tick")]
    public void Loads_the_shipped_corridor_patrol()
    {
        LayoutDef layout = ContentLoader.Load().Layout;
        var patrol = Assert.Single(layout.Patrols);
        Assert.Equal("corridor-patrol", patrol.Id);
        Assert.Equal(new[] { ("choir.melee", 1), ("choir.melee", 1) }, patrol.Enemies.Select(e => (e.EnemyId, e.Count)).ToArray());
        Assert.Equal(new[] { (32500, 0), (26500, 0) }, patrol.Waypoints.Select(p => (p.X, p.Z)).ToArray());
        Assert.Equal(50, patrol.SpeedMmPerTick);
        Assert.Null(patrol.AggroMm);
        var corridor = layout.Boxes.Single(b => b.Id == "corridor-2");
        Assert.All(patrol.Waypoints, p => Assert.True(p.X >= corridor.MinX && p.X <= corridor.MaxX && p.Z >= corridor.MinZ && p.Z <= corridor.MaxZ));
    }

    [Fact(DisplayName = "Layout patrols: a layout without the patrols key loads with none")]
    public void A_layout_without_patrols_loads_with_none()
    {
        var reg = ContentLoader.Load(new MutableSource().Edit(LayoutFile, n => n.AsObject().Remove("patrols")).Build());
        Assert.Empty(reg.Layout.Patrols);
    }

    [Fact(DisplayName = "Layout patrols: a diagonal leg is rejected (the walk must be exact integer mm)")]
    public void A_diagonal_leg_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Patrol(n)["waypoints"]![1]!["z"] = 500));
        Assert.Contains("must move along exactly one axis", ex.Message);
    }

    [Fact(DisplayName = "Layout patrols: a leg outside every layout box is rejected")]
    public void A_leg_outside_the_layout_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n =>
        {
            Patrol(n)["waypoints"]![0]!["z"] = 5000;
            Patrol(n)["waypoints"]![1]!["z"] = 5000;
        }));
        Assert.Contains("is not inside one layout box", ex.Message);
    }

    [Fact(DisplayName = "Layout patrols: a member with no forces value is rejected (a patrol banks forces like any pack)")]
    public void A_member_without_forces_is_rejected()
    {
        // hollowWarden.twinA is a known enemy with no forces.json entry (a boss add banks 0).
        var ex = LoadFails(new MutableSource().Edit(LayoutFile, n => Patrol(n)["enemies"]![0]!["enemyId"] = "hollowWarden.twinA"));
        Assert.Contains("has no forces value", ex.Message);
    }

    [Fact(DisplayName = "Layout patrols: a non-positive speed and a pull's id are rejected")]
    public void Bad_speed_and_pull_id_are_rejected()
    {
        Assert.Contains("speedMmPerTick must be positive", LoadFails(new MutableSource().Edit(LayoutFile, n => Patrol(n)["speedMmPerTick"] = 0)).Message);
        Assert.Contains("has the id of a pull", LoadFails(new MutableSource().Edit(LayoutFile, n => Patrol(n)["id"] = "pair")).Message);
    }
}
