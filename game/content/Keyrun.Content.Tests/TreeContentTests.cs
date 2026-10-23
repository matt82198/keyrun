// G2.2 design/ABILITY-TREES.md s4 (the four-node chain per rotational ability, with its key gates) and s5.1 (tree data
// under game/content/data/trees/, loaded by the C# content loader). RED until S2 adds the tree model, the loader and the
// TreesRule lookup. Missing symbols are named in the lane hand-back: ContentRegistry.Trees, TreeNodeDef, TreesRule.NodesFor.
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The tree node content and its loading (ABILITY-TREES s4, s5.1). Every rotational ability of every class kit on main
/// has four nodes in chain order (Rank, Enhanced, Choice, Rank), each id prefixed by its ability id; the key gates
/// are 0, 0, 4, 7; each node past the first requires the node before it; and <see cref="TreesRule"/> reports a class's
/// trees from the loaded set.
/// </summary>
public sealed class TreeContentTests
{
    /// <summary>The s4 chain order: Rank, Enhanced, Choice, Rank (kinds per s1.2: rank, enhanced, choice, rank; R-TREES-4).</summary>
    private static readonly string[] ChainKinds = { "rank", "enhanced", "choice", "rank" };

    /// <summary>The s4 key gates in chain order: Rank and Enhanced ungated, Choice at key 4, final Rank at key 7.</summary>
    private static readonly int[] ChainGates = { 0, 0, 4, 7 };

    private static ContentRegistry Load() => ContentLoader.Load();

    /// <summary>Every (class id, rotational ability id) pair across the class kits on main.</summary>
    private static IEnumerable<(string ClassId, string AbilityId)> RotationalAbilities(ContentRegistry reg) =>
        reg.Classes.Values.SelectMany(c => new[] { c.Abilities.Rot1, c.Abilities.Rot2, c.Abilities.Rot3, c.Abilities.Rot4, c.Abilities.Rot5 }
            .Select(a => (c.Id, a)));

    /// <summary>The loaded chain of one ability, in tree order.</summary>
    private static List<TreeNodeDef> ChainOf(ContentRegistry reg, string abilityId) =>
        reg.Trees.Values.Where(n => n.AbilityId == abilityId).ToList();

    [Fact]
    public void Every_rotational_ability_of_every_class_has_four_nodes_in_chain_order_prefixed_by_its_ability_id()
    {
        ContentRegistry reg = Load();
        var abilities = RotationalAbilities(reg).ToList();
        Assert.NotEmpty(abilities);
        foreach ((string classId, string abilityId) in abilities)
        {
            List<TreeNodeDef> chain = ChainOf(reg, abilityId);
            Assert.Equal(4, chain.Count);
            Assert.Equal(ChainKinds, chain.Select(n => n.Kind).ToArray());
            Assert.All(chain, n => Assert.StartsWith(abilityId + ".", n.Id, StringComparison.Ordinal));
            Assert.All(chain, n => Assert.Equal(classId, n.ClassId));
        }
    }

    [Fact]
    public void Key_gates_are_0_0_4_7_and_each_node_past_the_first_requires_the_node_before_it()
    {
        ContentRegistry reg = Load();
        foreach ((string _, string abilityId) in RotationalAbilities(reg))
        {
            List<TreeNodeDef> chain = ChainOf(reg, abilityId);
            Assert.Equal(ChainGates, chain.Select(n => n.MinKey).ToArray());
            Assert.Empty(chain[0].Requires);
            for (int i = 1; i < chain.Count; i++)
            {
                Assert.Equal(new[] { chain[i - 1].Id }, chain[i].Requires.ToArray());
            }
        }
    }

    [Fact]
    public void TreesRule_reports_a_classes_loaded_trees_instead_of_no_tree_ships()
    {
        ContentRegistry reg = Load();
        IReadOnlyList<TreeNodeDef> warden = TreesRule.NodesFor(reg, "warden");
        // Five rotational abilities at four nodes each.
        Assert.Equal(20, warden.Count);
        Assert.Equal(
            reg.Trees.Values.Where(n => n.ClassId == "warden").Select(n => n.Id).ToArray(),
            warden.Select(n => n.Id).ToArray());
        Assert.Empty(TreesRule.NodesFor(reg, "no-such-class"));
    }
}
