using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// run.json (game/content/data/run.json) carries the walk speed, actor radius and arena bounds the
/// desktop host used to hardcode. Values are the frozen TS client's client/src/sim-host/run-setup.ts
/// BASE_SPEED_MM_PER_TICK (:53), ACTOR_RADIUS_MM (:54) and DUNGEON_BOUNDS (:57). A missing or
/// impossible value fails the load; there is no default.
/// </summary>
public sealed class RunConfigTests
{
    private const string RunFile = "run.json";

    private static ContentLoadException LoadFails(MutableSource source)
    {
        return Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));
    }

    private static JsonObject Obj(JsonNode root, string key) => root[key]!.AsObject();

    [Fact]
    public void Loads_the_run_setup_ts_movement_values()
    {
        RunConfig run = ContentLoader.Load().Run;

        Assert.Equal(180, run.Movement.SpeedMmPerTick);
        Assert.Equal(400, run.Movement.RadiusMm);
        Assert.Equal(-4000, run.ArenaBounds.MinX);
        Assert.Equal(18000, run.ArenaBounds.MaxX);
        Assert.Equal(-6000, run.ArenaBounds.MinZ);
        Assert.Equal(9000, run.ArenaBounds.MaxZ);
    }

    [Fact]
    public void Run_json_is_listed_and_embedded()
    {
        Assert.Contains(RunFile, ContentLoader.Files);
        Assert.Contains(RunFile, EmbeddedContentSource.Instance.Paths);
    }

    [Fact]
    public void Missing_run_json_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Remove(RunFile));
        Assert.Contains("missing file \"run.json\"", ex.Message);
    }

    [Theory]
    [InlineData("movement", "speedMmPerTick")]
    [InlineData("movement", "radiusMm")]
    [InlineData("arenaBounds", "minX")]
    [InlineData("arenaBounds", "maxX")]
    [InlineData("arenaBounds", "minZ")]
    [InlineData("arenaBounds", "maxZ")]
    public void Missing_movement_or_bounds_field_is_rejected(string block, string field)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Obj(n, block).Remove(field)));
        Assert.Contains(RunFile, ex.Message);
        Assert.Contains(field, ex.Message);
    }

    [Theory]
    [InlineData("movement")]
    [InlineData("arenaBounds")]
    public void Missing_block_is_rejected(string block)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => n.AsObject().Remove(block)));
        Assert.Contains(RunFile, ex.Message);
    }

    [Fact]
    public void Unknown_run_property_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Obj(n, "movement")["turnRate"] = 1));
        Assert.Contains(RunFile, ex.Message);
    }

    [Fact]
    public void Fractional_speed_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Obj(n, "movement")["speedMmPerTick"] = 180.5));
        Assert.Contains(RunFile, ex.Message);
    }

    [Theory]
    [InlineData("speedMmPerTick", 0)]
    [InlineData("speedMmPerTick", -180)]
    [InlineData("radiusMm", 0)]
    public void Non_positive_speed_or_radius_is_rejected(string field, int value)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Obj(n, "movement")[field] = value));
        Assert.Contains("run movement." + field, ex.Message);
    }

    [Theory]
    [InlineData("minX", 18000)]
    [InlineData("minZ", 9001)]
    public void Empty_or_inverted_bounds_are_rejected(string field, int value)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Obj(n, "arenaBounds")[field] = value));
        Assert.Contains("run arenaBounds", ex.Message);
    }
}
