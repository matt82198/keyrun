using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Tests.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The Warden talent sentences the player reads (content/data/classes/warden.json) say what the sim does with the
/// patch. Each test reads the shipped text and the shipped patch, drives the patched ability through the real cast
/// pipeline (<see cref="Rules.Make"/>), and checks the sentence against the observed result: Warcry's Sundered count,
/// Mobile Ward's floor patch and Braced recipients, and Surge's cost.
/// </summary>
public sealed class WardenTalentTextTests
{
    private static JsonElement WardenClass()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(EncounterTestKit.RepoRoot(), "content", "data", "classes", "warden.json")));
        return doc.RootElement.Clone();
    }

    private static JsonElement Option(string abilityId, string optionId) =>
        WardenClass().GetProperty("talents").EnumerateArray()
            .Single(f => f.GetProperty("abilityId").GetString() == abilityId)
            .GetProperty("options").EnumerateArray()
            .Single(o => o.GetProperty("id").GetString() == optionId);

    private static string FacetText(string facetId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(EncounterTestKit.RepoRoot(), "content", "data", "facets", "warden.json")));
        return doc.RootElement.EnumerateArray().Single(f => f.GetProperty("id").GetString() == facetId).GetProperty("text").GetString()!;
    }

    private static string[] Tags(JsonElement patch) => patch.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray();

    private static readonly string[] Numbers = { "zero", "one", "two", "three", "four", "five" };

    private static int Num(string word) => Array.IndexOf(Numbers, word.ToLowerInvariant()) is var i && i >= 0
        ? i
        : throw new ArgumentException("not a count word: " + word);

    /// <summary>
    /// How many enemies a sentence says end up Sundered. "also applies Sundered to N ... enemies" adds N to the
    /// target; "applies Sundered to N ... enemies" is N in all; "also reaches the nearest other enemy" is two.
    /// </summary>
    internal static int ClaimedSundered(string text)
    {
        var also = Regex.Match(text, @"also applies Sundered to (\w+) (adjacent |other |nearby )?enemies", RegexOptions.IgnoreCase);
        if (also.Success) return 1 + Num(also.Groups[1].Value);
        var total = Regex.Match(text, @"applies Sundered to (\w+) (adjacent |other |nearby )?enemies", RegexOptions.IgnoreCase);
        if (total.Success) return Num(total.Groups[1].Value);
        if (Regex.IsMatch(text, @"nearest other enemy", RegexOptions.IgnoreCase)) return 2;
        return 1;
    }

    private static bool Sundered(ActorState a) => a.Effects.Any(e => e.EffectId.StartsWith("window:sundered:", StringComparison.Ordinal));

    private static bool Braced(ActorState a) => a.Effects.Any(e => e.EffectId == "braced");

    /// <summary>warden.strike's base effects (content/data/abilities/warden.json), with the Warcry patch's shape and tags.</summary>
    private static AbilityDef WarcryStrike()
    {
        var patch = Option("warden.strike", "warcry").GetProperty("patch");
        var shape = patch.GetProperty("shape");
        Assert.Equal("cone", shape.GetProperty("kind").GetString());
        return Def("warden.strike",
            new AbilityShape(ShapeKind.Cone, Radius: shape.GetProperty("radius").GetInt64(), Angle1024: shape.GetProperty("angle1024").GetInt32()),
            new EffectOp[] { new DamageOp(80, "physical"), new ResourceOp(15), new WindowOp("sundered", 60, true, "warden") },
            range: 3000, tags: Tags(patch));
    }

    // Four enemies inside the Warcry cone, so any count up to four can show.
    private static int SunderedByWarcry()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank, maxResource: 100);
        var enemies = new[]
        {
            Make(2, ActorKind.Enemy, "trash", x: 1000, z: 0, maxHealth: 1000),
            Make(3, ActorKind.Enemy, "trash", x: 1500, z: 200, maxHealth: 1000),
            Make(4, ActorKind.Enemy, "trash", x: 2000, z: -200, maxHealth: 1000),
            Make(5, ActorKind.Enemy, "trash", x: 2500, z: 300, maxHealth: 1000),
        };
        var state = At(0, new[] { warden }.Concat(enemies).ToArray());
        Tick(state, Rules.Make(Config(AbilityMap(WarcryStrike()))), Cmds(Cast(1, "warden.strike")), _ => { });
        Assert.All(enemies, e => Assert.True(e.Health < 1000)); // the cone hit all four
        return enemies.Count(Sundered);
    }

    [Fact]
    public void Warcry_text_states_the_number_of_enemies_the_sim_Sunders()
    {
        var text = Option("warden.strike", "warcry").GetProperty("text").GetString()!;
        Assert.Equal(SunderedByWarcry(), ClaimedSundered(text));
    }

    [Fact]
    public void Sundered_Storm_text_states_the_number_of_enemies_the_shared_tag_Sunders()
    {
        Assert.Equal(SunderedByWarcry(), ClaimedSundered(FacetText("warden.keystone.sunderedStorm")));
    }

    [Theory]
    [InlineData("Strike also applies Sundered to two adjacent enemies.", 3)]
    [InlineData("Strike applies Sundered to two adjacent enemies: its target and the nearest other enemy in the cone.", 2)]
    [InlineData("Sundered also reaches the nearest other enemy in the arc.", 2)]
    [InlineData("Sundered lasts longer.", 1)]
    public void The_claim_reader_counts_what_a_sentence_says(string text, int expected)
    {
        Assert.Equal(expected, ClaimedSundered(text));
    }

    /// <summary>
    /// Bulwark (content/data/abilities/warden.json: circle radius 2500, floor Window "bulwark", Braced) cast with
    /// Mobile Ward's shape, next to an ally inside the base circle. Returns whether a floor patch opened and who
    /// is Braced.
    /// </summary>
    private static (bool FloorPatch, bool CasterBraced, bool AllyBraced) CastBulwark(bool mobile)
    {
        var shape = new AbilityShape(ShapeKind.Circle, Radius: 2500);
        if (mobile)
        {
            var kind = Option("warden.bulwark", "mobile").GetProperty("patch").GetProperty("shape").GetProperty("kind").GetString();
            Assert.Equal("self", kind);
            shape = new AbilityShape(ShapeKind.Self);
        }
        var bulwark = Def("warden.bulwark", shape,
            new EffectOp[] { new WindowOp("bulwark", 160, false, "warden", RadiusMm: 2500), new ApplyEffectOp("braced", 160, 1) },
            costResource: "none", range: mobile ? 0 : 3000, tags: new[] { "setup:bulwark" });
        var braced = new EffectDef("braced", 1, null, new EffectModifiers(DamageTakenBp: -1500));
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank, maxResource: 100);
        var ally = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: 1000, z: 0);
        var state = At(0, warden, ally);
        Tick(state, Rules.Make(Config(AbilityMap(bulwark), Defs(braced))), Cmds(Cast(1, "warden.bulwark")), _ => { });
        return (state.FloorWindows.Keys.Contains("bulwark"), Braced(warden), Braced(ally));
    }

    [Fact]
    public void Base_Bulwark_plants_a_floor_patch_and_braces_the_ally_in_it()
    {
        Assert.Equal((true, true, true), CastBulwark(mobile: false));
    }

    [Fact]
    public void Mobile_Ward_text_says_what_the_patched_Bulwark_does()
    {
        var text = Option("warden.bulwark", "mobile").GetProperty("text").GetString()!;
        var (floorPatch, casterBraced, allyBraced) = CastBulwark(mobile: true);
        if (floorPatch)
        {
            Assert.DoesNotMatch(new Regex(@"instead of a floor patch|no floor patch|no longer (plants|a floor)", RegexOptions.IgnoreCase), text);
        }
        if (casterBraced && !allyBraced)
        {
            Assert.Matches(new Regex(@"only you", RegexOptions.IgnoreCase), text);
        }
    }

    [Fact]
    public void Surge_text_does_not_call_its_cooldown_a_cost()
    {
        var surge = Option("warden.guardBreak", "surge");
        var text = surge.GetProperty("text").GetString()!;
        Assert.True(surge.GetProperty("patch").TryGetProperty("cooldownTicks", out _), "Surge's patch sets a cooldown");
        if (!surge.GetProperty("patch").TryGetProperty("cost", out _))
        {
            Assert.DoesNotMatch(new Regex(@"\bcosts?\b", RegexOptions.IgnoreCase), text);
        }
    }
}
