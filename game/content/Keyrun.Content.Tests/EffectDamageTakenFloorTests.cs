using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Pins the damage-taken floor on status effects: no effect's <c>modifiers.damageTakenBp</c> may sit below -10000 bp
/// (-100%), so two stacked reductions can never multiply into a heal. Mirrors the gear floor in
/// <see cref="GearItemsTests"/> (GearValidator rejects damageTakenBp below -5000). The shipped effects must all pass.
/// </summary>
public sealed class EffectDamageTakenFloorTests
{
    private const string PlayerEffects = "effects/player.json";

    /// <summary>The floor: an effect may remove all incoming damage but never heal through a negative reduction.</summary>
    private const int Floor = -10000;

    private static readonly ContentRegistry Reg = ContentLoader.Load();

    [Fact]
    public void An_effect_with_damage_taken_below_minus_10000_bp_is_rejected_and_the_error_names_the_effect()
    {
        var ex = Assert.Throws<ContentLoadException>(() => ContentLoader.Load(new MutableSource().Edit(PlayerEffects, n =>
        {
            JsonNode effect = n.AsArray().First(x => (string?)x!["id"] == "stunned")!;
            effect["modifiers"] = new JsonObject { ["damageTakenBp"] = Floor - 1 };
        }).Build()));
        Assert.Contains("stunned", ex.Message);
        Assert.Contains("damageTakenBp", ex.Message);
    }

    [Fact]
    public void An_effect_with_damage_taken_exactly_at_the_floor_is_accepted()
    {
        var reg = ContentLoader.Load(new MutableSource().Edit(PlayerEffects, n =>
        {
            JsonNode effect = n.AsArray().First(x => (string?)x!["id"] == "stunned")!;
            effect["modifiers"] = new JsonObject { ["damageTakenBp"] = Floor };
        }).Build());
        Assert.Equal(Floor, reg.Effects["stunned"].Modifiers.DamageTakenBp);
    }

    [Fact]
    public void Every_shipped_effect_validates_and_keeps_damage_taken_at_or_above_the_floor()
    {
        Assert.NotEmpty(Reg.Effects);
        foreach (EffectDef effect in Reg.Effects.Values)
        {
            int? bp = effect.Modifiers.DamageTakenBp;
            if (bp is int value)
                Assert.True(value >= Floor, "effect \"" + effect.Id + "\" damageTakenBp " + value + " is below " + Floor);
        }
    }
}
