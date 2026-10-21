using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Hud;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The run-end panel's per-card lines (game/src/Hud/RunEndCardLines.cs, Godot-free; U10). An Ascension card is shown
/// as an Ascension mark, never as a tier bonus line; an ordinary card keeps the lines its builder gives. The content
/// gear table (GearItems.CardLines, Keyrun.Content) is not referenced by this project, so the ordinary-card test
/// passes a stand-in builder and asserts the seam returns exactly what that builder gives.
/// </summary>
public sealed class RunEndCardLinesTests
{
    private static Item Card(string slot, int tier) =>
        new($"reward-{slot}-edge-t{tier}-0", slot, tier, new Facet($"{slot}-edge", "Strike also applies Sundered", "ability-1"), Rewards.StartingCharges);

    /// <summary>An offer whose three cards were drawn over a fully capped character, so the third card is the Ascension card.</summary>
    private static IReadOnlyList<Item> CappedOffer()
    {
        var equipped = Rewards.GearSlots.ToDictionary(s => s, s => Card(s, Progression.MaxTier));
        var drawn = new[] { Card("charm", 2), Card("armor", 3), Card("weapon", 4) };
        return Rewards.WithAscensionCard(drawn, Array.Empty<string>(), equipped);
    }

    [Fact]
    public void An_ascension_card_shows_exactly_one_line_and_it_is_the_ascension_mark()
    {
        var ascension = CappedOffer()[2];
        Assert.True(Ascension.IsCard(ascension));

        var lines = RunEndCardLines.For(ascension, _ => new[] { "Tier 10: +9% damage dealt", "Strike also applies Sundered" });

        Assert.Equal(new[] { "Ascension mark" }, lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("Tier", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ordinary_card_keeps_the_lines_its_builder_gives_unchanged()
    {
        var ordinary = CappedOffer()[0];
        Assert.False(Ascension.IsCard(ordinary));
        var builderLines = new[] { "Tier 2: +2% damage dealt", "Strike also applies Sundered" };
        Item? seen = null;

        var lines = RunEndCardLines.For(ordinary, card =>
        {
            seen = card;
            return builderLines;
        });

        Assert.Same(ordinary, seen);
        Assert.Equal(builderLines, lines);
    }
}
