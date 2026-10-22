using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// run.json enemyAggro (AGGRO-CHASE, finding f-f0c886eb): every enemy's own aggro radius, assist radius and leash, with
/// per-enemy overrides where 0 switches a rule off. The shipped values, and the load failing on a negative value, an
/// unknown enemy key, or a leash that is on but not longer than the same enemy's aggro radius.
/// </summary>
public sealed class EnemyAggroConfigTests
{
    private const string RunFile = "run.json";

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    private static JsonObject Block(JsonNode root) => root["enemyAggro"]!.AsObject();

    [Fact]
    public void Shipped_values_give_trash_a_radius_assist_and_leash_and_switch_the_boss_off()
    {
        var aggro = ContentLoader.Load().Run.EnemyAggro;
        Assert.NotNull(aggro);
        Assert.Equal((8000, 3000, 30000), aggro!.For("choir.melee"));
        Assert.Equal((8000, 3000, 30000), aggro.For("pair.melee"));
        Assert.Equal((0, 0, 0), aggro.For("hollowWarden.boss"));
    }

    [Fact]
    public void An_absent_block_loads_as_no_per_enemy_rule()
    {
        var registry = ContentLoader.Load(new MutableSource().Edit(RunFile, n => n.AsObject().Remove("enemyAggro")).Build());
        Assert.Null(registry.Run.EnemyAggro);
    }

    [Fact]
    public void A_negative_value_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)["assistMm"] = -1));
        Assert.Contains("run enemyAggro (default) aggroMm, assistMm and leashMm must be non-negative", ex.Message);
    }

    [Fact]
    public void An_unknown_enemy_key_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)["kinds"]!.AsObject()["choir.ghost"] = new JsonObject { ["aggroMm"] = 1000 }));
        Assert.Contains("run enemyAggro.kinds \"choir.ghost\" is not an enemy", ex.Message);
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(7999)]
    public void A_leash_not_longer_than_the_aggro_radius_is_rejected(int leash)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)["kinds"]!.AsObject()["pair.melee"] = new JsonObject { ["leashMm"] = leash }));
        Assert.Contains("run enemyAggro pair.melee leashMm " + leash + " must be longer than its aggroMm 8000", ex.Message);
    }

    [Fact]
    public void A_leash_switched_off_needs_no_length()
    {
        var registry = ContentLoader.Load(new MutableSource().Edit(RunFile, n => Block(n)["kinds"]!.AsObject()["pair.melee"] = new JsonObject { ["leashMm"] = 0 }).Build());
        Assert.Equal((8000, 3000, 0), registry.Run.EnemyAggro!.For("pair.melee"));
    }
}
