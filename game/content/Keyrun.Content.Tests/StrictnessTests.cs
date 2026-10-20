using System;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The loader fails closed: malformed or inconsistent content throws
/// <see cref="ContentLoadException"/> naming the file, never returns partial or defaulted data.
/// Each test mutates exactly one embedded file; the control proves the unmutated copy loads.
/// </summary>
public sealed class StrictnessTests
{
    private static ContentLoadException LoadFails(MutableSource source)
    {
        return Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));
    }

    private static JsonObject FirstAbility(JsonNode root) => root.AsArray()[0]!.AsObject();

    [Fact]
    public void Control_unmutated_copy_loads_and_equals_embedded()
    {
        ContentRegistry copy = ContentLoader.Load(new MutableSource().Build());
        Assert.Equal(ContentLoader.Load(), copy);
    }

    [Fact]
    public void Ability_missing_id_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("abilities/warden.json", n => FirstAbility(n).Remove("id")));
        Assert.Contains("abilities/warden.json", ex.Message);
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void Missing_required_nested_field_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("abilities/warden.json", n => FirstAbility(n)["cost"]!.AsObject().Remove("amount")));
        Assert.Contains("abilities/warden.json", ex.Message);
    }

    [Fact]
    public void Unknown_property_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("effects/player.json", n => n.AsArray()[0]!.AsObject()["durationTicks"] = 10));
        Assert.Contains("effects/player.json", ex.Message);
    }

    [Fact]
    public void Unknown_property_inside_an_effect_op_is_rejected()
    {
        LoadFails(new MutableSource().Edit("abilities/warden.json", n => FirstAbility(n)["effects"]!.AsArray()[0]!.AsObject()["bonus"] = 1));
    }

    [Fact]
    public void Unknown_effect_op_is_rejected()
    {
        LoadFails(new MutableSource().Edit("abilities/warden.json", n => FirstAbility(n)["effects"]!.AsArray()[0]!.AsObject()["op"] = "explode"));
    }

    [Fact]
    public void Null_for_a_non_nullable_field_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("abilities/cinder.json", n => FirstAbility(n)["name"] = null));
        Assert.Contains("abilities/cinder.json", ex.Message);
    }

    [Fact]
    public void Fractional_number_is_rejected()
    {
        LoadFails(new MutableSource().Edit("abilities/warden.json", n => FirstAbility(n)["range"] = 3000.5));
    }

    [Fact]
    public void Unknown_enum_value_is_rejected()
    {
        LoadFails(new MutableSource().Edit("abilities/warden.json", n => FirstAbility(n)["shape"]!.AsObject()["kind"] = "sphere"));
    }

    [Fact]
    public void Enum_value_with_wrong_case_is_rejected()
    {
        LoadFails(new MutableSource().Edit("abilities/warden.json", n => FirstAbility(n)["shape"]!.AsObject()["kind"] = "Target"));
    }

    [Fact]
    public void Id_inside_a_facet_patch_is_rejected()
    {
        LoadFails(new MutableSource().Edit("facets/warden.json", n => n.AsArray()[0]!.AsObject()["patch"]!.AsObject()["id"] = "warden.renamed"));
    }

    [Fact]
    public void Duplicate_ability_id_across_files_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("abilities/cinder.json", n => FirstAbility(n)["id"] = "warden.strike"));
        Assert.Contains("warden.strike", ex.Message);
    }

    [Fact]
    public void Class_slot_referencing_unknown_ability_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("classes/warden.json", n => n["abilities"]!.AsObject()["rot1"] = "warden.nope"));
        Assert.Contains("warden.nope", ex.Message);
    }

    [Fact]
    public void Ability_referencing_unknown_effect_is_rejected()
    {
        var ex = LoadFails(new MutableSource().EditText("effects/player.json", t => t.Replace("\"id\": \"stunned\"", "\"id\": \"stunnedX\"")));
        Assert.Contains("stunned", ex.Message);
    }

    [Fact]
    public void Enemy_script_referencing_unknown_ability_is_rejected()
    {
        var ex = LoadFails(new MutableSource().EditText("enemies/hollow-warden.json", t => t.Replace("\"cast\": \"hollowWarden.slam\"", "\"cast\": \"hollowWarden.nope\"")));
        Assert.Contains("hollowWarden.nope", ex.Message);
    }

    [Fact]
    public void Dungeon_referencing_unknown_enemy_is_rejected()
    {
        LoadFails(new MutableSource().Edit("dungeons/hollow-vault.json", n => n["boss"]!.AsObject()["enemyId"] = "nobody"));
    }

    [Fact]
    public void Combo_with_unknown_payoff_class_is_rejected()
    {
        LoadFails(new MutableSource().Edit("combos.json", n => n.AsArray()[0]!.AsObject()["payoffClass"] = "bard"));
    }

    [Fact]
    public void Unlisted_json_file_is_rejected_not_silently_ignored()
    {
        var ex = LoadFails(new MutableSource().Add("abilities/bard.json", "[]"));
        Assert.Contains("abilities/bard.json", ex.Message);
    }

    [Fact]
    public void Missing_json_file_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Remove("clauses.json"));
        Assert.Contains("clauses.json", ex.Message);
    }

    [Fact]
    public void Spans_cast_telegraph_on_an_instant_cast_is_rejected()
    {
        // SLAM-TELEGRAPH: hollowWarden.slam's telegraph spans its cast; with castTicks 0 there is no cast to span.
        var ex = LoadFails(new MutableSource().Edit("abilities/enemies.json", n =>
        {
            foreach (var a in n.AsArray())
            {
                if ((string?)a!["id"] == "hollowWarden.slam") a["castTicks"] = 0;
            }
        }));
        Assert.Contains("spansCast", ex.Message);
        Assert.Contains("hollowWarden.slam", ex.Message);
    }

    [Fact]
    public void Json_comments_are_rejected()
    {
        LoadFails(new MutableSource().EditText("clauses.json", t => "// note" + Environment.NewLine + t));
    }
}
