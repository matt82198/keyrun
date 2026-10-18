using System.Linq;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// <see cref="JsRecord{TValue}"/> must iterate exactly like a JS object's own string keys (used for
/// ActorState.cooldowns and SimState.floorWindows): array-index keys ascending numerically first, then the
/// other keys in insertion order, with delete-then-reinsert moving a key to the end.
/// </summary>
public sealed class JsRecordTests
{
    [Fact]
    public void Iterates_index_keys_ascending_then_strings_in_insertion_order()
    {
        var r = new JsRecord<int>();
        r["b"] = 1;
        r["10"] = 2;
        r["a"] = 3;
        r["2"] = 4;
        r["01"] = 5;          // not canonical: a string key
        r["4294967295"] = 6;  // 2^32 - 1 is not an array index in JS
        r["4294967294"] = 7;  // the largest array index
        Assert.Equal(new[] { "2", "10", "4294967294", "b", "a", "01", "4294967295" }, r.Keys.ToArray());
    }

    [Fact]
    public void Overwrite_keeps_position_and_delete_then_reinsert_moves_to_the_end()
    {
        var r = new JsRecord<int> { ["x"] = 1, ["y"] = 2, ["z"] = 3 };
        r["x"] = 10;
        Assert.Equal(new[] { "x", "y", "z" }, r.Keys.ToArray());
        Assert.True(r.Remove("x"));
        Assert.False(r.Remove("x"));
        r["x"] = 11;
        Assert.Equal(new[] { "y", "z", "x" }, r.Keys.ToArray());
        Assert.Equal(new[] { 2, 3, 11 }, r.Select(kv => kv.Value).ToArray());
        Assert.Equal(3, r.Count);
    }

    [Fact]
    public void Clone_is_independent_and_preserves_order()
    {
        var r = new JsRecord<int> { ["q"] = 1, ["5"] = 2, ["p"] = 3 };
        var c = r.Clone(v => v);
        c["q"] = 9;
        c.Remove("p");
        Assert.Equal(1, r["q"]);
        Assert.Equal(new[] { "5", "q", "p" }, r.Keys.ToArray());
        Assert.Equal(new[] { "5", "q" }, c.Keys.ToArray());
    }
}
