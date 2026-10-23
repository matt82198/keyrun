// G2.2 design/ABILITY-TREES.md s1.2 (rank nodes: integer effectDelta; enhanced nodes: one conditional op), s3, s5.2 (the host
// builds sim patches; the sim never reads Keyrun.Content, R-TREES-1). Node ids are the shipped ones in
// game/content/data/trees/warden.json. RED until TreeComposer.ToGearPatch, TreeNodeEdit and EffectDelta exist in Keyrun.Sim.Runs.
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The node-to-GearPatch composition API: <c>TreeComposer.ToGearPatch(TreeState, AbilityDef, IReadOnlyDictionary&lt;string, TreeNodeEdit&gt;)</c>
/// turns a class's unlocked node set into the single <see cref="GearPatch"/> the sim applies to an ability. Rank nodes
/// (<see cref="EffectDelta"/>) change a field the base ability already has; enhanced nodes (<see cref="TreeNodeEdit.Patch"/>)
/// append their conditional op. Composition follows the chain order (rank, enhanced, choice, rank), never the order the
/// nodes were taken, so the result is order-independent.
/// </summary>
public sealed class TreeGearPatchTests
{
    private const string RankII = "warden.strike.rank2";
    private const string Enhanced = "warden.strike.enhanced";

    [Fact]
    public void Empty_unlocked_set_composes_to_the_identity_patch()
    {
        GearPatch patch = TreeComposer.ToGearPatch(TreeState.Empty, Strike(), Edits());

        Assert.Equal(new GearPatch(), patch);
        Assert.Equal(Strike().Effects.ToArray(), patch.ApplyTo(Strike()).Effects.ToArray());
    }

    [Fact]
    public void Rank_node_effect_delta_changes_the_primary_amount_by_its_delta()
    {
        // warden.json: "Rank II: Strike deals 8 more damage." edit.effectDelta = damage amount +8 (flat, not a percent).
        var edits = Edits((RankII, new TreeNodeEdit(Delta: new EffectDelta("damage", "amount", 8))));
        TreeState state = TreeState.AllUnlocked(new[] { RankII }, earned: 1, bestKey: 0);

        AbilityDef applied = TreeComposer.ToGearPatch(state, Strike(), edits).ApplyTo(Strike());

        Assert.Equal(new EffectOp[] { new DamageOp(88, "physical") }, applied.Effects.ToArray());
    }

    [Fact]
    public void Two_unlocked_nodes_compose_order_independently()
    {
        var edits = Edits(
            (RankII, new TreeNodeEdit(Delta: new EffectDelta("damage", "amount", 8))),
            (Enhanced, new TreeNodeEdit(Patch: BracedStrikePatch())));

        GearPatch forward = TreeComposer.ToGearPatch(
            TreeState.AllUnlocked(new[] { RankII, Enhanced }, earned: 2, bestKey: 0), Strike(), edits);
        GearPatch reversed = TreeComposer.ToGearPatch(
            TreeState.AllUnlocked(new[] { Enhanced, RankII }, earned: 2, bestKey: 0), Strike(), edits);

        Assert.Equal(forward.ApplyTo(Strike()).Effects.ToArray(), reversed.ApplyTo(Strike()).Effects.ToArray());
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void Enhanced_node_conditional_lands_in_the_patch_as_the_design_states()
    {
        // warden.json: "While Bulwark's brace is up, Strike builds 15 more Momentum." consume false, selfHas "braced".
        var edits = Edits((Enhanced, new TreeNodeEdit(Patch: BracedStrikePatch())));
        TreeState state = TreeState.AllUnlocked(new[] { Enhanced }, earned: 1, bestKey: 0);

        GearPatch patch = TreeComposer.ToGearPatch(state, Strike(), edits);

        Assert.NotNull(patch.EffectsAppend);
        ConditionalOp conditional = Assert.IsType<ConditionalOp>(Assert.Single(patch.EffectsAppend!));
        Assert.Equal(ConditionKind.SelfHas, conditional.If.Kind);
        Assert.Equal("braced", conditional.If.Id);
        Assert.False(conditional.Consume);
        Assert.Equal(new EffectOp[] { new ResourceOp(15) }, conditional.Then.ToArray());
    }

    // Literal Strike: base 80 physical damage, the same shape the host would produce from the content kit.
    private static AbilityDef Strike() => new()
    {
        Id = "warden.strike",
        CastTicks = 0,
        GcdTicks = 0,
        CooldownTicks = 0,
        Charges = 1,
        Cost = new AbilityCost("none", 0),
        Range = 3000,
        Shape = new AbilityShape(ShapeKind.Target),
        Interruptible = false,
        Effects = new EffectOp[] { new DamageOp(80, "physical") },
    };

    // The Braced Strike enhanced node as the host would build it: one conditional op appended after the base ops.
    private static GearPatch BracedStrikePatch() => new()
    {
        EffectsAppend = new EffectOp[]
        {
            new ConditionalOp
            {
                If = new OpCondition(ConditionKind.SelfHas, "braced"),
                Consume = false,
                Then = new EffectOp[] { new ResourceOp(15) },
            },
        },
    };

    private static IReadOnlyDictionary<string, TreeNodeEdit> Edits(params (string Id, TreeNodeEdit Edit)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Edit, System.StringComparer.Ordinal);
}
