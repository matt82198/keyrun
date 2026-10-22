using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Content.Tests;

/// <summary>
/// Matt 2026-10-22 ("ability tooltips and cooler names for the players"): every player kit ability has a themed display
/// name and a flavor line in game/content/data/ability-text.json, the loader fails closed on a missing, unknown, wordy or
/// colliding row, and the tooltip <see cref="AbilityTooltip.Build"/> generates from content states the role, what the
/// ability does, its cast time, cooldown and range.
/// </summary>
public sealed class AbilityTextTests
{
    private const string File = ContentLoader.AbilityTextFile;
    private readonly ITestOutputHelper _out;

    public AbilityTextTests(ITestOutputHelper output) => _out = output;

    private static ContentRegistry Registry { get; } = ContentLoader.Load();

    private static AbilityTextTable Table { get; } = AbilityTextLoader.Load();

    private static AbilityTooltipText Tip(string id) =>
        AbilityTooltip.Build(AbilityTooltipFacts.From(Registry.Abilities[id]), Table.For(id)!, Registry);

    private static string LoadFails(Action<JsonNode> edit) =>
        Assert.Throws<ContentLoadException>(() => AbilityTextLoader.Load(new MutableSource().Edit(File, edit).Build())).Message;

    private static JsonObject Row(JsonNode root, string id) =>
        root["abilities"]!.AsArray().Select(r => r!.AsObject()).First(r => (string)r["id"]! == id);

    [Fact(DisplayName = "every player kit ability has a display name, a flavor and a generated tooltip with cast, cooldown and range")]
    public void Every_kit_ability_has_text_and_a_tooltip()
    {
        var count = 0;
        foreach (var cls in Registry.Classes.Values)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in AbilityTextLoader.KitIds(cls.Abilities))
            {
                var row = Table.For(id);
                Assert.True(row is not null, $"{id} has no ability-text row");
                var words = AbilityTextTable.Words(row!.DisplayName);
                Assert.InRange(words.Length, 1, AbilityTextTable.MaxWords);
                Assert.False(string.IsNullOrWhiteSpace(row.Flavor), $"{id} has no flavor");
                Assert.True(names.Add(row.DisplayName), $"{cls.Id}: display name \"{row.DisplayName}\" collides");
                Assert.NotEqual(Registry.Abilities[id].Name, row.DisplayName);

                var tip = Tip(id);
                _out.WriteLine($"{id} -> {string.Join(" / ", tip.Lines)}");
                Assert.Equal(row.DisplayName, tip.Title);
                Assert.Equal(row.Flavor, tip.Flavor);
                Assert.Matches(@"^Cast (instant|\d+(\.\d+)?s) \| Cooldown (none|\d+(\.\d+)?s) \| Range (self|\d+(\.\d+)? m)( \| \d+ charges)?$", tip.Stats);
                Assert.False(string.IsNullOrWhiteSpace(tip.Role), $"{id} has no role line");
                Assert.EndsWith(".", tip.Body);
                Assert.DoesNotContain("No direct effect", tip.Body, StringComparison.Ordinal);
                count++;
            }
        }
        Assert.Equal(27, count);
        Assert.Equal(27, Table.Rows.Count);
    }

    [Fact(DisplayName = "the Warden's Strike tooltip reads its role, ops and numbers from content")]
    public void Strike_tooltip_is_generated_from_its_ops()
    {
        var tip = Tip("warden.strike");
        Assert.Equal("Sundering Verdict", tip.Title);
        Assert.Equal("Builder | Setup: opens Sundered on target", tip.Role);
        Assert.Equal("Deals 80 physical damage, builds 15 Momentum and opens Sundered on the target for 3s.", tip.Body);
        Assert.Equal("Cast instant | Cooldown none | Range 3 m", tip.Stats);
    }

    [Fact(DisplayName = "a payoff's role line names each partner Window it closes and the combo it makes")]
    public void Payoff_role_names_the_windows_it_closes()
    {
        Assert.Equal("Builder | Payoff: closes Kindled for Forge, Attuned for Anthem", Tip("warden.guardBreak").Role);
        Assert.Equal("Deals 100 physical damage in an area, builds 22 Momentum and generates 1.5x threat.", Tip("warden.guardBreak").Body);
        Assert.Equal("Spender: 30 Heat | Payoff: closes Sundered for Shatter, Attuned for Flashfire", Tip("cinder.detonate").Role);
        Assert.Equal("Interrupt", Tip("chanter.rebuke").Role);
        Assert.Equal("Cast instant | Cooldown 24s | Range 25 m", Tip("chanter.rebuke").Stats);
    }

    [Fact(DisplayName = "a Chanter stance ability describes both verses")]
    public void Stance_ability_describes_both_verses()
    {
        var tip = Tip("chanter.refrain");
        Assert.Equal("Stance: swaps Mend and Ward", tip.Role);
        Assert.Equal("Heals 150. Ward: Cuts damage taken by 30% for 4s.", tip.Body);
    }

    [Fact(DisplayName = "the tooltip states the facts it is given (a talented cast time), not the bare content row")]
    public void Tooltip_reads_the_given_facts()
    {
        var bolt = Registry.Abilities["cinder.emberBolt"];
        var snap = Registry.Classes["cinder"].Talents.First(t => t.AbilityId == bolt.Id).Options.First(o => o.Id == "instant");
        var talented = TalentPicks.ApplyPatch(bolt, snap.Patch);
        var row = Table.For(bolt.Id)!;
        Assert.StartsWith("Cast 1s |", AbilityTooltip.Build(AbilityTooltipFacts.From(bolt), row, Registry).Stats);
        var tip = AbilityTooltip.Build(AbilityTooltipFacts.From(talented), row, Registry);
        Assert.StartsWith("Cast instant |", tip.Stats);
        Assert.Equal("Deals 70 fire damage and builds 15 Heat.", tip.Body);
    }

    [Fact(DisplayName = "ability-text.json fails closed: a missing kit row, an unknown id, a collision, too many words, an empty flavor")]
    public void Loader_fails_closed()
    {
        Assert.Contains("kit ability \"warden.taunt\" (warden) has no row", LoadFails(root =>
        {
            var rows = root["abilities"]!.AsArray();
            rows.Remove(rows.First(r => (string)r!["id"]! == "warden.taunt"));
        }), StringComparison.Ordinal);
        Assert.Contains("is not a player class kit ability", LoadFails(root =>
            root["abilities"]!.AsArray().Add(new JsonObject { ["id"] = "choir.meleeSwing", ["displayName"] = "Swing", ["flavor"] = "x" })), StringComparison.Ordinal);
        Assert.Contains("\"warden.guardBreak\" displayName \"Gavel Sweep\" collides with \"warden.strike\" in class warden", LoadFails(root =>
            Row(root, "warden.strike")["displayName"] = "gavel sweep"), StringComparison.Ordinal);
        Assert.Contains("has 4 words", LoadFails(root =>
            Row(root, "cinder.flare")["displayName"] = "One Two Three Four"), StringComparison.Ordinal);
        Assert.Contains("\"chanter.hymn\" has an empty flavor", LoadFails(root =>
            Row(root, "chanter.hymn")["flavor"] = " "), StringComparison.Ordinal);
        Assert.Contains("duplicate row", LoadFails(root =>
            root["abilities"]!.AsArray().Add(Row(root, "cinder.blink").DeepClone())), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a display name's initials are the narrow-slot tag")]
    public void Initials_are_one_letter_per_word()
    {
        Assert.Equal("SV", AbilityTextTable.Initials("Sundering Verdict"));
        Assert.Equal("Q", AbilityTextTable.Initials("quench"));
        Assert.Equal("Rimefrost Snap", Table.NameOr("cinder.coldSnap", "Cold Snap"));
        Assert.Equal("Cold Snap", Table.NameOr("nope.nope", "Cold Snap"));
    }
}
