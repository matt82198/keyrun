using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G2.2 finding f-e624e14b (R-TREES-8 (b)), host path: a warden seat starts its run with only its own class's owned tree
/// nodes. The progression owns warden.strike.rank2 (+8 damage on warden.strike) and a cinder node whose id is prefixed by
/// warden.strike, so it names the same kit ability. A union of every class's nodes would stack both edits onto the warden
/// strike; the seat must see only the warden node. Base damage is read from the content, never computed from the code under test.
/// </summary>
public sealed class HostTreeClassScopeTests
{
    // game/content/data/trees/warden.json, warden.strike.rank2: edit.effectDelta damage amount +8.
    private const long WardenRank2Delta = 8;

    // A cross-class node that names the warden kit ability warden.strike (id prefix), with a delta distinct from every shipped node.
    private const string CinderCollidingNodeId = "warden.strike.cinderCollide";
    private const long CinderCollidingDelta = 20;
    private const string StrikeAbilityId = "warden.strike";

    [Fact(DisplayName = "Host tree class scope: a warden seat owning a cinder node on warden.strike starts with only the warden node applied")]
    public void A_warden_seat_applies_only_its_own_class_node_when_another_class_owns_the_same_ability()
    {
        var registry = WithCinderCollidingNode(ContentLoader.Load());
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden");
        var progression = Progression.Start(1) with
        {
            Trees = new Dictionary<string, TreeState>(StringComparer.Ordinal)
            {
                ["warden"] = new TreeState(1, 1, new[] { "warden.strike.rank2" }),
                ["cinder"] = new TreeState(1, 1, new[] { CinderCollidingNodeId }),
            },
        };

        var start = setup.StartFor(null, progression, registry, "warden");

        Assert.NotNull(start.Gear);
        long baseDamage = setup.Content.Abilities[StrikeAbilityId].Effects.OfType<Keyrun.Sim.Combat.DamageOp>().First().Amount;
        long strikeDamage = Damage(start.Gear!.Abilities[StrikeAbilityId]);
        Assert.Equal(baseDamage + WardenRank2Delta, strikeDamage);
    }

    private static long Damage(Keyrun.Sim.Combat.AbilityDef ability) =>
        ability.Effects.OfType<Keyrun.Sim.Combat.DamageOp>().First().Amount;

    private static ContentRegistry WithCinderCollidingNode(ContentRegistry registry)
    {
        var warden = registry.Trees["warden.strike.rank2"];
        var colliding = warden with
        {
            Id = CinderCollidingNodeId,
            ClassId = "cinder",
            AbilityId = StrikeAbilityId,
            Name = "Colliding",
            Text = "Test-only cross-class node on warden.strike.",
            Edit = "{\"effectDelta\":[{\"op\":\"damage\",\"field\":\"amount\",\"delta\":" + CinderCollidingDelta + "}]}",
        };
        return registry with
        {
            Trees = new ValueMap<TreeNodeDef>(
                registry.Trees.Append(new KeyValuePair<string, TreeNodeDef>(colliding.Id, colliding))),
        };
    }
}
