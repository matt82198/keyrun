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
/// ABILITY-TREES s1.2 and s5.2, pinned against the live content registry: the host adapter maps each rank node's single
/// <c>effectDelta</c> to the sim's <see cref="EffectDelta"/>, and refuses a seat that owns an enhanced node (no sim edit
/// path yet). The expected literal is read from game/content/data/trees/warden.json (warden.strike.rank2), never computed
/// from the code under test.
/// </summary>
public sealed class HostTreeEditsTests
{
    // game/content/data/trees/warden.json, warden.strike.rank2: edit.effectDelta = [ { op: "damage", field: "amount", delta: 8 } ].
    private const string Rank2Op = "damage";
    private const string Rank2Field = "amount";
    private const long Rank2Delta = 8;

    // game/content/data/trees/warden.json:4 (warden.strike.enhanced, "Braced Strike") and design/ABILITY-TREES.md s4.1 line 109:
    // if selfHas "braced", consume false, then resource +15.
    private const string EnhancedId = "warden.strike.enhanced";
    private const string StrikeAbilityId = "warden.strike";
    private const string EnhancedConditionId = "braced";
    private const long EnhancedResourceDelta = 15;

    // game/content/data/trees/warden.json: warden.bulwark.rank2 is a rank node whose delta is window.durationTicks, which the
    // sim does not run (TreeComposer refuses it). ABILITY-TREES R-TREES-10b: owning it must skip the node, not throw.
    private const string BulwarkRank2Id = "warden.bulwark.rank2";
    private const string BulwarkAbilityId = "warden.bulwark";

    // game/content/data/trees/chanter.json: chanter.cadence.enhanced, a conditional whose body op is "heal" (not a sim op).
    private const string UnrunnableEnhancedId = "chanter.cadence.enhanced";

    [Fact(DisplayName = "Host tree edits: warden.strike.rank2 maps to its single content effectDelta (damage amount +8)")]
    public void Rank2_edit_matches_the_content_delta()
    {
        var registry = ContentLoader.Load();

        var edits = HostRunSetup.TreeEdits(registry);

        Assert.True(edits.ContainsKey("warden.strike.rank2"));
        var edit = edits["warden.strike.rank2"];
        Assert.NotNull(edit.Delta);
        Assert.Equal(new EffectDelta(Rank2Op, Rank2Field, Rank2Delta), edit.Delta);
    }

    [Fact(DisplayName = "Host tree edits: owning warden.strike.enhanced puts its Braced Strike conditional into the strike patch at run start")]
    public void Owning_an_enhanced_node_puts_its_conditional_into_the_run_strike()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden");
        var progression = ProgressionOwning("warden.strike.rank2", EnhancedId);

        var start = setup.StartFor(null, progression, registry, "warden");

        Assert.NotNull(start.Gear);
        var strike = start.Gear!.Abilities[StrikeAbilityId];
        var conditional = Assert.Single(strike.Effects.OfType<ConditionalOp>());
        Assert.Equal(ConditionKind.SelfHas, conditional.If.Kind);
        Assert.Equal(EnhancedConditionId, conditional.If.Id);
        Assert.False(conditional.Consume);
        var resource = Assert.IsType<Keyrun.Sim.Combat.ResourceOp>(Assert.Single(conditional.Then));
        Assert.Equal(EnhancedResourceDelta, resource.Delta);
    }

    [Fact(DisplayName = "Host tree edits: a rank node whose edit has an unknown shape still throws")]
    public void An_unknown_edit_shape_still_throws()
    {
        var registry = ContentLoader.Load();
        var node = registry.Trees["warden.strike.rank2"] with { Edit = "{\"mystery\":[]}" };
        var broken = registry with
        {
            Trees = new ValueMap<TreeNodeDef>(
                registry.Trees.Select(kv => new KeyValuePair<string, TreeNodeDef>(kv.Key, kv.Key == node.Id ? node : kv.Value))),
        };

        Assert.Throws<InvalidOperationException>(() => HostRunSetup.TreeEdits(broken));
    }

    [Fact(DisplayName = "Host tree edits: a seat owning only warden.strike.rank2 passes the owned-edit check")]
    public void Owning_only_a_rank_node_is_accepted()
    {
        var registry = ContentLoader.Load();
        var progression = ProgressionOwning("warden.strike.rank2");

        var ex = Record.Exception(() => HostRunSetup.RequireOwnedEdits(registry, progression));

        Assert.Null(ex);
    }

    [Fact(DisplayName = "Tree apply: a Cinder seat gets no patch from a warden node it owns (R-TREES-10a)")]
    public void A_seat_gets_no_patch_from_another_classes_owned_node()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "cinder");
        var progression = ProgressionOwningIn("warden", "warden.strike.rank2");

        var start = setup.StartFor(null, progression, registry, "cinder");

        Assert.NotNull(start.Gear);
        Assert.Equal(BaseEffects(setup, start.Gear!, StrikeAbilityId), setup.Content.Abilities[StrikeAbilityId].Effects);
    }

    [Fact(DisplayName = "Tree apply: a Warden seat still gets the warden.strike rank2 patch it owns (guard)")]
    public void A_seat_of_the_owning_class_still_gets_its_patch()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden");
        var progression = ProgressionOwningIn("warden", "warden.strike.rank2");

        var start = setup.StartFor(null, progression, registry, "warden");

        Assert.NotNull(start.Gear);
        long baseDamage = setup.Content.Abilities[StrikeAbilityId].Effects.OfType<Keyrun.Sim.Combat.DamageOp>().First().Amount;
        Assert.Equal(baseDamage + Rank2Delta, BaseEffects(setup, start.Gear!, StrikeAbilityId).OfType<Keyrun.Sim.Combat.DamageOp>().First().Amount);
    }

    [Fact(DisplayName = "Tree apply: owning warden.bulwark.rank2 (window.durationTicks) starts the run and skips the node")]
    public void Owning_warden_bulwark_rank2_starts_the_run_and_skips_the_node()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden");
        var progression = ProgressionOwningIn("warden", BulwarkRank2Id);

        var start = setup.StartFor(null, progression, registry, "warden");

        Assert.NotNull(start.Gear);
        Assert.Equal(new[] { BulwarkRank2Id }, start.SkippedTreeNodes);
    }

    [Fact(DisplayName = "Tree apply: owning chanter.cadence.enhanced (shipped conditional op heal) starts the run and skips the node (R-TREES-10b)")]
    public void Owning_chanter_cadence_enhanced_starts_the_run_and_skips_the_node()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "chanter");
        var progression = ProgressionOwningIn("chanter", UnrunnableEnhancedId);

        // The shipped chanter.cadence.enhanced conditional body is op "heal", which ContentToSim.Trees.cs ConditionalOf refuses.
        var start = setup.StartFor(null, progression, registry, "chanter");

        Assert.NotNull(start.Gear);
        Assert.Equal(new[] { UnrunnableEnhancedId }, start.SkippedTreeNodes);
    }

    // The effects an ability starts with: its loadout entry when the gear carries one, else the content base kit.
    private static IReadOnlyList<Keyrun.Sim.Combat.EffectOp> BaseEffects(HostRunSetup setup, GearLoadout gear, string abilityId) =>
        gear.Abilities.TryGetValue(abilityId, out var ability) ? ability.Effects : setup.Content.Abilities[abilityId].Effects;

    private static Progression ProgressionOwning(params string[] nodeIds) => ProgressionOwningIn("warden", nodeIds);

    private static Progression ProgressionOwningIn(string classId, params string[] nodeIds) =>
        Progression.Start(1) with
        {
            Trees = new Dictionary<string, TreeState>(StringComparer.Ordinal)
            {
                [classId] = new TreeState(1, 1, nodeIds),
            },
        };
}
