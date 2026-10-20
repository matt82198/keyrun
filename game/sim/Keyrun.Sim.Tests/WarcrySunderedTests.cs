using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Warden's Warcry talent (content/data/classes/warden.json "Strike also applies Sundered to two adjacent enemies."):
/// a Strike carrying the "warcry:multi-target" tag opens Sundered on its primary target and on the
/// enemy in the cone nearest that target, two in total. Driven through the real cast pipeline (Rules.Make).
/// </summary>
public sealed class WarcrySunderedTests
{
    private static readonly EffectOp[] StrikeOps =
    {
        new DamageOp(80, "physical"),
        new ResourceOp(15),
        new WindowOp("sundered", 60, true, "warden"),
    };

    /// <summary>warden.strike with the Warcry patch applied (cone radius 3000, angle1024 170, both tags).</summary>
    private static readonly AbilityDef WarcryStrike = Def("warden.strike", new AbilityShape(ShapeKind.Cone, Radius: 3000, Angle1024: 170),
        StrikeOps, range: 3000, tags: new[] { "setup:sundered", "warcry:multi-target" });

    /// <summary>Base warden.strike (single target).</summary>
    private static readonly AbilityDef BaseStrike = Def("warden.strike", new AbilityShape(ShapeKind.Target),
        StrikeOps, range: 3000, tags: new[] { "setup:sundered" });

    /// <summary>The Warcry cone without the multi-target tag: proves the tag, not the shape, drives the second Sundered.</summary>
    private static readonly AbilityDef UntaggedConeStrike = Def("warden.strike", new AbilityShape(ShapeKind.Cone, Radius: 3000, Angle1024: 170),
        StrikeOps, range: 3000, tags: new[] { "setup:sundered" });

    private static bool Sundered(ActorState a) => a.Effects.Any(e => e.EffectId.StartsWith("window:sundered:", System.StringComparison.Ordinal));

    private static (ActorState Warden, ActorState[] Enemies, SimState State) ThreeInCone()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank, maxResource: 100);
        var e2 = Make(2, ActorKind.Enemy, "trash", x: 1000, z: 0, maxHealth: 1000);
        var e3 = Make(3, ActorKind.Enemy, "trash", x: 1500, z: 200, maxHealth: 1000);
        var e4 = Make(4, ActorKind.Enemy, "trash", x: 2500, z: -300, maxHealth: 1000);
        return (warden, new[] { e2, e3, e4 }, At(0, warden, e2, e3, e4));
    }

    private static int[] SunderedIds(ActorState[] enemies) => enemies.Where(Sundered).Select(e => e.Id).ToArray();

    [Fact]
    public void Warcry_Strike_with_three_enemies_in_the_cone_Sunders_exactly_two_the_primary_and_its_nearest_neighbour()
    {
        var (_, enemies, state) = ThreeInCone();
        var (events, emit) = Collector();
        Tick(state, Rules.Make(Config(AbilityMap(WarcryStrike))), Cmds(Cast(1, "warden.strike")), emit);
        Assert.All(enemies, e => Assert.True(e.Health < 1000)); // the cone hit all three
        Assert.Equal(new[] { 2, 3 }, SunderedIds(enemies));
        Assert.Equal(2, events.Count(e => e.Type == "window_opened" && Str(e, "windowId") == "sundered"));
    }

    [Fact]
    public void Warcry_Strike_primary_is_the_casters_target_when_it_is_in_the_cone()
    {
        var (warden, enemies, state) = ThreeInCone();
        warden.TargetId = 4;
        Tick(state, Rules.Make(Config(AbilityMap(WarcryStrike))), Cmds(Cast(1, "warden.strike", 4)), _ => { });
        Assert.Equal(new[] { 3, 4 }, SunderedIds(enemies));
    }

    [Fact]
    public void Warcry_Strike_with_one_enemy_in_the_cone_Sunders_one()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank, maxResource: 100);
        var e2 = Make(2, ActorKind.Enemy, "trash", x: 1000, z: 0, maxHealth: 1000);
        var outside = Make(3, ActorKind.Enemy, "trash", x: 0, z: 1000, maxHealth: 1000); // 90 degrees off the facing
        var (events, emit) = Collector();
        Tick(At(0, warden, e2, outside), Rules.Make(Config(AbilityMap(WarcryStrike))), Cmds(Cast(1, "warden.strike")), emit);
        Assert.Equal(new[] { 2 }, SunderedIds(new[] { e2, outside }));
        Assert.Equal(1, events.Count(e => e.Type == "window_opened" && Str(e, "windowId") == "sundered"));
    }

    [Fact]
    public void Base_Strike_without_Warcry_still_Sunders_only_its_target()
    {
        var (warden, enemies, state) = ThreeInCone();
        warden.TargetId = 2;
        Tick(state, Rules.Make(Config(AbilityMap(BaseStrike))), Cmds(Cast(1, "warden.strike", 2)), _ => { });
        Assert.Equal(new[] { 2 }, SunderedIds(enemies));
    }

    [Fact]
    public void Untagged_cone_Strike_Sunders_only_the_first_target()
    {
        var (_, enemies, state) = ThreeInCone();
        Tick(state, Rules.Make(Config(AbilityMap(UntaggedConeStrike))), Cmds(Cast(1, "warden.strike")), _ => { });
        Assert.Equal(new[] { 2 }, SunderedIds(enemies));
    }
}
