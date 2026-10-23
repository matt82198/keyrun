// G2.2 design/ABILITY-TREES.md s5.5 TreesRuleTests: the four kinds in order; requires chains; minKey never goes down; each s1.2 decision rule as a rejected fixture; an enhanced node reading its own output is rejected; a class whose enhanced nodes read fewer than two other rotational abilities is rejected.
using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The improvement-tree validator (<see cref="TreesRule"/>, ABILITY-TREES s5.1, s1.2 decision rules). A tree file is
/// data; the rule answers with the load errors it produces. An empty list means the tree loads. Red until S2 adds
/// <c>Keyrun.Content/Model/Trees.cs</c> and <c>Loading/Rules/TreesRule.cs</c>.
/// </summary>
public sealed class TreesRuleTests
{
    private static IReadOnlyList<string> Errors(string json) => TreesRule.Validate(json);

    private const string Warden = """
    {"classId":"warden","nodes":[
      {"id":"strike.rank2","kind":"rank","minKey":0,"edit":{"effectDelta":{"damage":10}}},
      {"id":"strike.enhanced","kind":"enhanced","requires":["strike.rank2"],"minKey":0,
       "edit":{"conditional":{"if":{"kind":"selfHas","id":"braced"},"then":[{"op":"resource","amount":15}]}}},
      {"id":"strike.choice","kind":"choice","requires":["strike.enhanced"],"minKey":4,"optionsFrom":"talents"},
      {"id":"strike.rank3","kind":"rank","requires":["strike.choice"],"minKey":7,"edit":{"effectDelta":{"damage":10}}}
    ]}
    """;

    [Fact]
    public void A_well_formed_chain_of_the_four_kinds_in_order_loads_clean() =>
        Assert.Empty(Errors(Warden));

    [Fact]
    public void A_requires_chain_must_name_a_node_that_exists() =>
        Assert.Contains(Errors(Warden.Replace("\"requires\":[\"strike.choice\"]", "\"requires\":[\"ghost\"]")),
            e => e.Contains("ghost"));

    [Fact]
    public void A_minKey_may_not_go_down_along_a_chain() =>
        Assert.Contains(Errors(Warden.Replace("\"minKey\":7", "\"minKey\":2")),
            e => e.Contains("minKey"));

    [Fact]
    public void A_rank_or_enhanced_node_never_lowers_cooldown_cast_gcd_or_cost() =>
        Assert.NotEmpty(Errors(Warden.Replace("{\"damage\":10}", "{\"cooldownTicks\":-20}")));

    [Fact]
    public void A_node_never_gives_one_ability_both_a_window_and_a_payoff() =>
        Assert.NotEmpty(Errors(Warden.Replace("\"then\":[{\"op\":\"resource\",\"amount\":15}]",
            "\"then\":[{\"op\":\"window\"},{\"op\":\"payoff\"}]")));

    [Fact]
    public void An_enhanced_node_never_applies_an_effect_another_button_exists_to_apply() =>
        Assert.NotEmpty(Errors(Warden.Replace("\"op\":\"resource\",\"amount\":15",
            "\"op\":\"apply\",\"effect\":\"bracedSelf\"")));

    [Fact]
    public void A_free_or_instant_press_is_not_a_rank_or_enhanced_edit() =>
        Assert.NotEmpty(Errors(Warden.Replace("{\"damage\":10}", "{\"castTicks\":0}")));

    [Fact]
    public void An_enhanced_node_reading_its_own_output_is_rejected() =>
        Assert.Contains(Errors(Warden.Replace("\"id\":\"braced\"", "\"id\":\"strike.output\"")),
            e => e.Contains("own output"));

    [Fact]
    public void A_class_whose_enhanced_nodes_read_fewer_than_two_other_abilities_is_rejected() =>
        // The fixture's one enhanced node reads only Strike's own state, so the class reads zero other abilities.
        Assert.Contains(TreesRule.ClassReadErrors(Warden), e => e.Contains("two other"));
}
