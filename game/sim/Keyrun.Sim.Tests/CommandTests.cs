using System.Text.Json;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Port of tests/sim/core/commands.test.ts (validateCommand), driven through both C# entry points:
/// <see cref="CommandJson.Parse"/> for untrusted JSON (the TS function's actual input domain) and
/// <see cref="Commands.Validate"/> for already-typed commands.
/// </summary>
public sealed class CommandTests
{
    private static Command? P(string json) => CommandJson.Parse(JsonDocument.Parse(json).RootElement);

    /// <summary>Asserts the JSON is accepted and re-renders canonically to <paramref name="expected"/>.</summary>
    private static void Accepts(string json, string expected)
    {
        var c = P(json);
        Assert.NotNull(c);
        Assert.Equal(expected, CommandJson.ToCanonicalJson(c!));
    }

    // ---- validateCommand: move ----

    [Fact]
    public void Accepts_a_well_formed_move_command() =>
        Accepts("{\"tick\":5,\"actorId\":1,\"kind\":\"move\",\"dir\":{\"x\":1000,\"z\":-1000},\"yaw\":0}",
            "{\"actorId\":1,\"dir\":{\"x\":1000,\"z\":-1000},\"kind\":\"move\",\"tick\":5,\"yaw\":0}");

    [Fact]
    public void Rejects_dir_components_out_of_range()
    {
        Assert.Null(P("{\"tick\":5,\"actorId\":1,\"kind\":\"move\",\"dir\":{\"x\":1001,\"z\":0},\"yaw\":0}"));
        Assert.Null(P("{\"tick\":5,\"actorId\":1,\"kind\":\"move\",\"dir\":{\"x\":-1001,\"z\":0},\"yaw\":0}"));
        Assert.Null(Commands.Validate(new MoveCommand(5, 1, new Dir2(1001, 0), 0)));
        Assert.Null(Commands.Validate(new DashCommand(5, 1, new Dir2(0, -1001), 0)));
    }

    [Fact]
    public void Rejects_yaw_out_of_range()
    {
        Assert.Null(P("{\"tick\":5,\"actorId\":1,\"kind\":\"move\",\"dir\":{\"x\":0,\"z\":0},\"yaw\":1024}"));
        Assert.Null(P("{\"tick\":5,\"actorId\":1,\"kind\":\"move\",\"dir\":{\"x\":0,\"z\":0},\"yaw\":-1}"));
        Assert.Null(Commands.Validate(new MoveCommand(5, 1, new Dir2(0, 0), 1024)));
        Assert.Null(Commands.Validate(new MoveCommand(5, 1, new Dir2(0, 0), -1)));
    }

    [Fact]
    public void Rejects_missing_or_non_numeric_dir()
    {
        Assert.Null(P("{\"tick\":5,\"actorId\":1,\"kind\":\"move\",\"yaw\":0}"));
        Assert.Null(P("{\"tick\":5,\"actorId\":1,\"kind\":\"move\",\"dir\":{\"x\":\"1\",\"z\":0},\"yaw\":0}"));
    }

    // ---- validateCommand: cast ----

    [Fact]
    public void Accepts_a_cast_with_a_target() =>
        Accepts("{\"tick\":1,\"actorId\":2,\"kind\":\"cast\",\"abilityId\":\"fireball\",\"targetId\":9}",
            "{\"abilityId\":\"fireball\",\"actorId\":2,\"kind\":\"cast\",\"targetId\":9,\"tick\":1}");

    [Fact]
    public void Accepts_a_cast_with_a_ground_point_and_no_target() =>
        Accepts("{\"tick\":1,\"actorId\":2,\"kind\":\"cast\",\"abilityId\":\"blizzard\",\"point\":{\"x\":1,\"y\":2,\"z\":3}}",
            "{\"abilityId\":\"blizzard\",\"actorId\":2,\"kind\":\"cast\",\"point\":{\"x\":1,\"y\":2,\"z\":3},\"tick\":1}");

    [Fact]
    public void Accepts_a_bare_cast_with_neither_target_nor_point() =>
        Accepts("{\"tick\":1,\"actorId\":2,\"kind\":\"cast\",\"abilityId\":\"selfheal\"}",
            "{\"abilityId\":\"selfheal\",\"actorId\":2,\"kind\":\"cast\",\"tick\":1}");

    [Fact]
    public void Rejects_a_missing_or_empty_ability_id()
    {
        Assert.Null(P("{\"tick\":1,\"actorId\":2,\"kind\":\"cast\"}"));
        Assert.Null(P("{\"tick\":1,\"actorId\":2,\"kind\":\"cast\",\"abilityId\":\"\"}"));
        Assert.Null(P("{\"tick\":1,\"actorId\":2,\"kind\":\"cast\",\"abilityId\":7}"));
        Assert.Null(Commands.Validate(new CastCommand(1, 2, string.Empty)));
    }

    [Fact]
    public void Rejects_a_non_integer_point_component()
    {
        Assert.Null(P("{\"tick\":1,\"actorId\":2,\"kind\":\"cast\",\"abilityId\":\"x\",\"point\":{\"x\":1.5,\"y\":0,\"z\":0}}"));
    }

    [Fact]
    public void Rejects_a_null_target_id_on_cast_because_null_is_not_undefined()
    {
        Assert.Null(P("{\"tick\":1,\"actorId\":2,\"kind\":\"cast\",\"abilityId\":\"x\",\"targetId\":null}"));
    }

    // ---- validateCommand: stopcast and target ----

    [Fact]
    public void Accepts_stopcast() =>
        Accepts("{\"tick\":3,\"actorId\":1,\"kind\":\"stopcast\"}", "{\"actorId\":1,\"kind\":\"stopcast\",\"tick\":3}");

    [Fact]
    public void Accepts_target_with_an_id_or_null()
    {
        Accepts("{\"tick\":3,\"actorId\":1,\"kind\":\"target\",\"targetId\":42}", "{\"actorId\":1,\"kind\":\"target\",\"targetId\":42,\"tick\":3}");
        Accepts("{\"tick\":3,\"actorId\":1,\"kind\":\"target\",\"targetId\":null}", "{\"actorId\":1,\"kind\":\"target\",\"targetId\":null,\"tick\":3}");
    }

    [Fact]
    public void Rejects_target_missing_the_target_id_field()
    {
        Assert.Null(P("{\"tick\":3,\"actorId\":1,\"kind\":\"target\"}"));
    }

    // ---- validateCommand: malformed input ----

    [Fact]
    public void Rejects_non_objects()
    {
        Assert.Null(P("null"));
        Assert.Null(P("42"));
        Assert.Null(P("\"cast\""));
        Assert.Null(P("[]"));
        Assert.Null(Commands.Validate(null));
    }

    [Fact]
    public void Rejects_an_unknown_kind()
    {
        Assert.Null(P("{\"tick\":1,\"actorId\":1,\"kind\":\"teleport\"}"));
    }

    [Fact]
    public void Rejects_a_negative_or_non_integer_tick()
    {
        Assert.Null(P("{\"tick\":-1,\"actorId\":1,\"kind\":\"stopcast\"}"));
        Assert.Null(P("{\"tick\":1.5,\"actorId\":1,\"kind\":\"stopcast\"}"));
        Assert.Null(Commands.Validate(new StopCastCommand(-1, 1)));
    }

    [Theory]
    [InlineData("move")]
    [InlineData("dash")]
    public void Rejects_a_client_supplied_walk_speed(string kind)
    {
        Assert.NotNull(P("{\"tick\":1,\"actorId\":1,\"kind\":\"" + kind + "\",\"dir\":{\"x\":0,\"z\":1000},\"yaw\":0}"));
        Assert.Null(P("{\"tick\":1,\"actorId\":1,\"kind\":\"" + kind + "\",\"dir\":{\"x\":0,\"z\":1000},\"yaw\":0,\"speedMmPerTick\":9999}"));
    }

    [Fact]
    public void Rejects_a_non_integer_actor_id()
    {
        Assert.Null(P("{\"tick\":1,\"actorId\":\"1\",\"kind\":\"stopcast\"}"));
    }

    [Fact]
    public void Accepts_integer_spellings_js_treats_as_integers()
    {
        Accepts("{\"tick\":1.0,\"actorId\":2e0,\"kind\":\"stopcast\"}", "{\"actorId\":2,\"kind\":\"stopcast\",\"tick\":1}");
    }

    [Fact]
    public void Typed_validate_returns_the_same_instance_for_a_valid_command()
    {
        var c = new CastCommand(0, 1, "warden.brace", TargetId: 100);
        Assert.Same(c, Commands.Validate(c));
    }
}
