using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// MECHANICS-PUNISH G1 made hollowWarden.slam and twins.slam caster-centred 3000 mm damage circles (range 0).
/// <see cref="Chase"/> counted only target-shaped casts toward an engage range, so the Hollow Warden and the Twins had
/// none and stood at their spawn for the whole fight (LockWinnabilityTests: the boss parked at x 15000, the Warden
/// could never reach melee to Setup the Lock). A caster-centred damage area (circle or cone) reaches its radius
/// (sim/encounter/chase.ts engageReachMm, tests/sim/encounter/chase-area-reach.test.ts is the TS twin); an area with
/// no damage op does not engage.
/// </summary>
public sealed class ChaseAreaReachTests
{
    private static readonly IReadOnlyDictionary<string, ScriptAbility> Abilities = new Dictionary<string, ScriptAbility>
    {
        ["slam"] = new("slam", 0, "circle", false) { AreaReachMm = 3000 },
        ["cleave"] = new("cleave", 4000, "cone", false) { AreaReachMm = 4000 },
        ["call"] = new("call", 0, "circle", false),
        ["swing"] = new("swing", 3000, "target", false),
    };

    [Fact]
    public void A_damage_area_reaches_its_radius_and_the_shortest_reach_wins()
    {
        Assert.Equal(3000, Chase.EngageRangeMm(new[] { ScriptStep.Loop(300, "slam") }, Abilities));
        Assert.Equal(4000, Chase.EngageRangeMm(new[] { ScriptStep.Loop(160, "cleave") }, Abilities));
        Assert.Equal(3000, Chase.EngageRangeMm(new[] { ScriptStep.Loop(160, "cleave"), ScriptStep.Loop(300, "slam") }, Abilities));
        Assert.Null(Chase.EngageRangeMm(new[] { ScriptStep.Loop(400, "call") }, Abilities));
    }

    [Fact]
    public void An_enemy_whose_only_swing_is_a_slam_circle_closes_on_its_target()
    {
        var enemy = EncounterTestKit.Actor(7, ActorKind.Enemy);
        var player = EncounterTestKit.Actor(1, ActorKind.Player, new Vec3(0, 0, 6000));
        var moves = Chase.ChaseCommands(EncounterTestKit.State(0, enemy, player), EncounterTestKit.Scripts(7, ScriptStep.Loop(300, "slam")), Abilities);
        Assert.Single(moves);
    }

    [Fact]
    public void The_shipped_boss_and_twin_slams_carry_their_radius_as_reach_and_a_stack_call_does_not()
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), 1);
        var views = RunWiring.ScriptAbilitiesOf(setup.Content.Abilities);
        Assert.Equal(3000, views["hollowWarden.slam"].AreaReachMm);
        Assert.Equal(3000, views["twins.slam"].AreaReachMm);
        Assert.Equal(4000, views["hollowWarden.cleave"].AreaReachMm);
        Assert.Null(views["pair.stackCall"].AreaReachMm);
        Assert.Null(views["seed.deathBloom"].AreaReachMm);
        Assert.Equal(3000, Chase.EngageRangeMm(new[] { ScriptStep.Loop(300, "hollowWarden.slam"), ScriptStep.Loop(160, "hollowWarden.cleave") }, views));
    }

    [Fact]
    public void The_encounter_json_abilities_carry_a_damage_areas_radius()
    {
        var json = JsValue.Parse(
            "{" +
            "\"slam\":{\"id\":\"slam\",\"range\":0,\"shape\":{\"kind\":\"circle\",\"radius\":3000},\"effects\":[{\"op\":\"damage\",\"amount\":1,\"school\":\"physical\"}]}," +
            "\"call\":{\"id\":\"call\",\"range\":0,\"shape\":{\"kind\":\"circle\",\"radius\":1500},\"effects\":[{\"op\":\"applyEffect\",\"effectId\":\"mark\"}]}}");
        var views = EncounterJson.ParseAbilities(json);
        Assert.Equal(3000, views["slam"].AreaReachMm);
        Assert.Null(views["call"].AreaReachMm);
    }
}
