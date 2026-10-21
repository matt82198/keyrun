using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// MPLUS-PATROLS (BIBLE section 9.1 "it does not move unless it is a patrol"; Matt 2026-10-20 "lean into m+ design
/// heavily while in the dungeon"): the shipped layout declares one patrol pack that walks corridor-2 between two points
/// inside it. The validator fails closed on a patrol whose endpoints leave its corridor, whose legs are diagonal, whose
/// speed is not positive, or whose loop has no length.
/// </summary>
public sealed class LayoutPatrolTests
{
    private const string LayoutFile = "layout.json";

    /// <summary>The shipped patrol, as layout.json declares it (2x choir.melee between (32500,0) and (26500,0) at 50 mm/tick).</summary>
    private const string CorridorPatrol = """
        { "id": "corridor-patrol",
          "enemies": [
            { "enemyId": "choir.melee", "count": 1, "spawn": { "x": 0, "z": -600 } },
            { "enemyId": "choir.melee", "count": 1, "spawn": { "x": 0, "z": 600 } }
          ],
          "waypoints": [ { "x": 32500, "z": 0 }, { "x": 26500, "z": 0 } ],
          "speedMmPerTick": 50 }
        """;

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    private static MutableSource WithPatrol(string patrolJson) =>
        new MutableSource().Edit(LayoutFile, n => n["patrols"] = new JsonArray(JsonNode.Parse(patrolJson)));

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): the shipped layout declares the corridor-2 patrol pack with its two members, its loop and its speed")]
    public void Shipped_layout_declares_the_corridor_patrol()
    {
        ContentRegistry reg = ContentLoader.Load(new MutableSource().Build());
        LayoutPatrol patrol = Assert.Single(reg.Layout.Patrols);
        Assert.Equal("corridor-patrol", patrol.Id);
        Assert.Equal(50, patrol.SpeedMmPerTick);
        Assert.Equal(2, patrol.Waypoints.Count());
        Assert.Equal(2, patrol.Enemies.Count());
        Assert.All(patrol.Enemies, e => Assert.Equal("choir.melee", e.EnemyId));
    }

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): a waypoint outside the corridor the patrol walks fails the load")]
    public void A_waypoint_outside_its_corridor_fails()
    {
        string outside = CorridorPatrol.Replace("\"x\": 26500", "\"x\": 40000");
        LoadFails(WithPatrol(outside));
    }

    [Fact(DisplayName = "Patrol (R-PATROL-1): a patrol whose aggro ring meets the Choir room or any pull's engagement ring at some tick fails the load")]
    public void A_patrol_ring_that_meets_a_pull_ring_or_the_choir_room_fails()
    {
        // The corridor-2 patrol as it stood before R-PATROL-1: its 4000 mm ring reaches the Choir room (x <= 26000).
        LoadFails(WithPatrol(CorridorPatrol));
    }

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): a non-positive speed fails the load")]
    public void A_zero_speed_fails()
    {
        string still = CorridorPatrol.Replace("\"speedMmPerTick\": 50", "\"speedMmPerTick\": 0");
        LoadFails(WithPatrol(still));
    }

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): a loop with no length (every waypoint the same point) fails the load")]
    public void A_zero_length_loop_fails()
    {
        string nowhere = CorridorPatrol.Replace("\"x\": 26500", "\"x\": 32500");
        LoadFails(WithPatrol(nowhere));
    }

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): a diagonal leg fails the load (the walk is exact integer mm along axis-aligned legs only)")]
    public void A_diagonal_leg_fails()
    {
        string diagonal = CorridorPatrol.Replace("\"x\": 26500, \"z\": 0", "\"x\": 26500, \"z\": 600");
        LoadFails(WithPatrol(diagonal));
    }

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): a member the content does not define fails the load")]
    public void A_member_with_an_unknown_enemy_fails()
    {
        string unknown = CorridorPatrol.Replace("choir.melee", "no.such.enemy");
        LoadFails(WithPatrol(unknown));
    }
}
