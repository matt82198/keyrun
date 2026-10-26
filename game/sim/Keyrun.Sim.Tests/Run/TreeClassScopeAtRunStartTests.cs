// G2.2 finding f-e624e14b (R-TREES-8 (b)): RunStart.Of has no class id, so a seat's owned nodes are the union of every
// class's TreeState.Nodes. A seat of class A must start the run with class A's nodes only. This test is the red for that
// gap: the seat is a warrior (class A, owns zap.rank2) whose progression also carries a mage tree (class B, owns
// bolt.rank2). The mage node must not change the mage ability, because the warrior seat does not own that class.
// Compiles against the current public API (RunStart.Of as shipped on main); the fix may add a class id parameter.
using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-TREES-8 (b): a seat's tree nodes are scoped to its own class at run start. The base kit holds one warrior ability
/// (zap, base damage 1) and one mage ability (bolt, base damage 1). The warrior seat owns zap.rank2 (+1); the mage tree
/// owns bolt.rank2 (+3). Only zap may change for the warrior seat.
/// </summary>
public sealed class TreeClassScopeAtRunStartTests
{
    private const int HumanId = 1;
    private const string WarriorClass = "warrior";
    private const string MageClass = "mage";

    private static readonly AbilityDef Zap =
        RunTestKit.Ability("zap", new EffectOp[] { new DamageOp(1, "physical") }, cooldownTicks: 100);

    private static readonly AbilityDef Bolt =
        RunTestKit.Ability("bolt", new EffectOp[] { new DamageOp(1, "physical") }, cooldownTicks: 100);

    private static readonly IReadOnlyDictionary<string, AbilityDef> Kit =
        new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["zap"] = Zap, ["bolt"] = Bolt };

    private static readonly IReadOnlyDictionary<string, GearPatch> NoFacetPatches =
        new Dictionary<string, GearPatch>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, TreeNodeEdit> NodeEdits =
        new Dictionary<string, TreeNodeEdit>(StringComparer.Ordinal)
        {
            ["zap.rank2"] = new TreeNodeEdit(Delta: new EffectDelta("damage", "amount", 1)),
            ["bolt.rank2"] = new TreeNodeEdit(Delta: new EffectDelta("damage", "amount", 3)),
        };

    [Fact(DisplayName = "Tree class scope: a warrior seat that also owns a mage node starts with only its warrior node applied")]
    public void A_seat_of_class_A_applies_only_class_A_nodes_when_it_also_owns_class_B_nodes()
    {
        var progression = Progression.Start(1) with
        {
            Trees = new Dictionary<string, TreeState>(StringComparer.Ordinal)
            {
                [WarriorClass] = new TreeState(1, 1, new[] { "zap.rank2" }),
                [MageClass] = new TreeState(1, 1, new[] { "bolt.rank2" }),
            },
        };

        var start = RunStart.Of(null, progression, HumanId, Kit, NoFacetPatches, NodeEdits, WarriorClass);

        Assert.NotNull(start.Gear);
        Assert.Equal(2, DamageOf(start.Gear!.Abilities["zap"]));
        Assert.Equal(1, DamageOf(start.Gear.Abilities.GetValueOrDefault("bolt") ?? Bolt));
    }

    private static long DamageOf(AbilityDef ability) =>
        ability.Effects.OfType<DamageOp>().Single().Amount;
}
