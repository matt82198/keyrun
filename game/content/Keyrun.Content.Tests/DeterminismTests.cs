using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Two loads produce structurally equal, independent object graphs. The negative controls prove
/// the equality used here is deep (a one-field difference anywhere is visible), so the positive
/// assertion is not a reference-equality or always-true comparison.
/// </summary>
public sealed class DeterminismTests
{
    [Fact]
    public void Two_loads_produce_equal_but_distinct_graphs()
    {
        ContentRegistry a = ContentLoader.Load();
        ContentRegistry b = ContentLoader.Load();

        Assert.NotSame(a, b);
        Assert.NotSame(a.Abilities, b.Abilities);
        Assert.NotSame(a.Abilities.Values.First(), b.Abilities.Values.First());
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a.Abilities.Keys.ToArray(), b.Abilities.Keys.ToArray());
    }

    [Fact]
    public void Equality_sees_a_one_number_difference_deep_in_the_graph()
    {
        ContentRegistry baseline = ContentLoader.Load();
        // hollowWarden.boss -> script -> cleave telegraph -> leadTicks: 40 -> 41.
        ContentRegistry changed = ContentLoader.Load(new MutableSource().EditText(
            "enemies/hollow-warden.json", t => t.Replace("\"leadTicks\": 40", "\"leadTicks\": 41")).Build());

        Assert.NotEqual(baseline, changed);
        Assert.NotEqual(baseline.Enemies, changed.Enemies);
        Assert.Equal(baseline.Abilities, changed.Abilities);
    }

    [Fact]
    public void Equality_sees_a_one_field_difference_on_a_record()
    {
        AbilityDef first = ContentLoader.Load().Abilities.Values.First();

        Assert.NotEqual(first, first with { CastTicks = first.CastTicks + 1 });
        Assert.NotEqual(first, first with { Tags = new ValueList<string>(first.Tags.Append("extra")) });
        Assert.Equal(first, first with { Tags = new ValueList<string>(first.Tags.ToArray()) });
    }

    [Fact]
    public void Value_collections_compare_by_content_and_order()
    {
        Assert.Equal(new ValueList<int>(new[] { 1, 2 }), new ValueList<int>(new[] { 1, 2 }));
        Assert.NotEqual(new ValueList<int>(new[] { 1, 2 }), new ValueList<int>(new[] { 2, 1 }));
        Assert.NotEqual(new ValueList<int>(new[] { 1, 2 }), new ValueList<int>(new[] { 1, 2, 3 }));
    }
}
