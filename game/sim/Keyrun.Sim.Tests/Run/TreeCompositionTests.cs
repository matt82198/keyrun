// R-TREES-1: the sim never reads Keyrun.Content; Compose takes sim types built by the host (HostRunSetup), so this test builds them as literals.
// G2.2 design/ABILITY-TREES.md s5.5 TreeCompositionTests: base, fork, enhanced, ranks, Facets, in order; Rend with every node has the Braced Strike conditional and 110 damage; a Facet equal to a node is rejected.
using System;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The composition order of a talented ability (ABILITY-TREES s3, s5.2; PIVOT s1.2): base kit, then the fork, then the
/// enhanced node, then ranks, then Facets, each applied through the sim patch type (<c>GearPatch</c>). Red until S1's
/// PR 2 adds <c>TreeComposer</c>, <c>ComposedAbility</c> and <c>TreeCompositionException</c> to <c>Keyrun.Sim.Runs</c>.
/// </summary>
public sealed class TreeCompositionTests
{
    [Fact]
    public void Base_fork_enhanced_ranks_then_facets_apply_in_that_order()
    {
        EffectOp braced = Braced();
        ComposedAbility composed = TreeComposer.Compose(
            baseAbility: Strike(),
            fork: RendFork(braced),
            nodes: new[] { Enhanced(), Rank(), Rank() },
            facets: new[] { new GearPatch { EffectsAppend = new EffectOp[] { new ResourceOp(5) } } });
        Assert.Equal(
            new EffectOp[] { new DamageOp(80, "physical"), braced, new ResourceOp(15), new DamageOp(15, "physical"), new DamageOp(15, "physical"), new ResourceOp(5) },
            composed.Ability.Effects.ToArray());
    }

    [Fact]
    public void Rend_with_every_node_has_the_Braced_Strike_conditional_and_110_damage()
    {
        ComposedAbility composed = TreeComposer.Compose(
            baseAbility: Strike(),
            fork: RendFork(Braced()),
            nodes: new[] { Enhanced(), Rank(), Rank() },
            facets: Array.Empty<GearPatch>());
        Assert.Contains(composed.Ability.Effects, e => e is ConditionalOp c && c.If.Id == "braced");
        Assert.Equal(110L, composed.Ability.Effects.OfType<DamageOp>().Sum(d => d.Amount));
    }

    [Fact]
    public void A_Facet_equal_to_a_tree_node_is_rejected()
    {
        GearPatch sundered = Enhanced();
        var error = Assert.Throws<TreeCompositionException>(() => TreeComposer.Compose(
            baseAbility: Strike(),
            fork: RendFork(Braced()),
            nodes: new[] { sundered },
            facets: new[] { sundered }));
        Assert.Contains("duplicates", error.Message);
    }

    // s5.5 item 3 (R-TREES-1, f-0780069d): two content-equal patches built from distinct lists are a duplicate; RED while
    // TreeComposer compares GearPatch members by reference (the lists in a record compare by reference, not content).
    [Fact]
    public void A_Facet_content_equal_to_a_tree_node_built_from_distinct_lists_is_rejected()
    {
        var node = new GearPatch { EffectsAppend = new EffectOp[] { new ResourceOp(15) } };
        var facet = new GearPatch { EffectsAppend = new EffectOp[] { new ResourceOp(15) } };
        Assert.Throws<TreeCompositionException>(() => TreeComposer.Compose(
            baseAbility: Strike(),
            fork: RendFork(Braced()),
            nodes: new[] { node },
            facets: new[] { facet }));
    }

    // R-TREES-3 (b) residual (f-25f18720): a ConditionalOp is content-equal across two patches when its Then list is
    // element-equal, even though each Braced() call builds a distinct list instance. RED while TreeComposer.SamePatch compares
    // EffectsAppend by record Equals, because ConditionalOp's record Equals compares Then by reference, so the duplicate is missed.
    [Fact]
    public void A_Facet_content_equal_to_a_node_holding_a_ConditionalOp_built_from_distinct_Then_lists_is_rejected()
    {
        var node = new GearPatch { EffectsAppend = new EffectOp[] { Braced() } };
        var facet = new GearPatch { EffectsAppend = new EffectOp[] { Braced() } };
        Assert.Throws<TreeCompositionException>(() => TreeComposer.Compose(
            baseAbility: Strike(),
            fork: RendFork(Braced()),
            nodes: new[] { node },
            facets: new[] { facet }));
    }

    // Literal Strike: base 80 physical damage, the same shape the host would produce from the content kit.
    private static AbilityDef Strike() => new()
    {
        Id = "strike",
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

    // The Braced Strike conditional: a self-state check on "braced" wrapping one op.
    private static EffectOp Braced() => new ConditionalOp
    {
        If = new OpCondition(ConditionKind.SelfHas, "braced"),
        Consume = false,
        Then = new EffectOp[] { new ResourceOp(15) },
    };

    // Rend fork: a tag plus the braced conditional appended after the base ops.
    private static GearPatch RendFork(EffectOp braced) =>
        new() { TagAdd = new[] { "rend" }, EffectsAppend = new EffectOp[] { braced } };

    // Enhanced node and Rank II / Rank III nodes, each +15 in its own way.
    private static GearPatch Enhanced() => new() { EffectsAppend = new EffectOp[] { new ResourceOp(15) } };

    private static GearPatch Rank() => new() { EffectsAppend = new EffectOp[] { new DamageOp(15, "physical") } };
}
