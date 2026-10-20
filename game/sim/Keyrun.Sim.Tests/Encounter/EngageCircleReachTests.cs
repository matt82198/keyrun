using System.Collections.Generic;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// DUNGEON-FEEL: the Hollow Warden's and the Twins' slams became caster-centred circles (MECHANICS-PUNISH G1). A
/// hostile circle with a range above 0 engages at that range, so the boss still closes on the party before it slams;
/// without this the boss has no target-shaped cast left, no engage range, and stands where it spawns. A range-0
/// circle (an aura such as the Seed's slam or a clause test's wave) never chases.
/// </summary>
public sealed class EngageCircleReachTests
{
    [Fact]
    public void A_ranged_hostile_circle_engages_at_its_range()
    {
        var abilities = new Dictionary<string, ScriptAbility>
        {
            ["slam"] = new("slam", 3000, "circle", false),
            ["cleave"] = new("cleave", 4000, "cone", false),
            ["lock"] = new("lock", 0, "self", false),
        };
        var boss = new[] { ScriptStep.Loop(300, "slam"), ScriptStep.Loop(160, "cleave"), ScriptStep.Loop(600, "lock") };
        Assert.Equal(3000, Chase.EngageRangeMm(boss, abilities));
    }

    [Fact]
    public void A_range_zero_circle_and_a_healing_circle_never_engage_and_the_shortest_reach_wins()
    {
        var abilities = new Dictionary<string, ScriptAbility>
        {
            ["aura"] = new("aura", 0, "circle", false),
            ["mend"] = new("mend", 2000, "circle", true),
            ["swing"] = new("swing", 3000, "target", false),
            ["slam"] = new("slam", 2500, "circle", false),
        };
        Assert.Null(Chase.EngageRangeMm(new[] { ScriptStep.Loop(80, "aura"), ScriptStep.Loop(90, "mend") }, abilities));
        Assert.Equal(2500, Chase.EngageRangeMm(new[] { ScriptStep.Loop(150, "swing"), ScriptStep.Loop(300, "slam") }, abilities));
    }
}
