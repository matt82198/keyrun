using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Every gear Facet and talent option shows the player a sentence; that sentence must say what the patch does to the
/// base ability. Each rule below binds a phrase to a field of the patched ability ("cooldown falls" means the patched
/// cooldown is below the base one, "costs much less Verse" means the cost amount went down, "grows" means the same
/// shape got bigger), and the walk fails on any text whose phrase is not backed by the data. The patched ability is
/// built from the shipped content: talents through <see cref="TalentPicks.ApplyPatch"/>, Facets by the gear-layer
/// rule (set fields replace, <c>effectsAppend</c> ops are added to the base effects).
/// </summary>
public sealed class TextMatchesDataTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    /// <summary>A phrase a text can carry and the data check it implies (base ability, patched ability).</summary>
    private sealed record Rule(string Name, Regex Phrase, Func<AbilityDef, AbilityDef, bool> Holds);

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static int CostOf(AbilityDef a) => a.Cost?.Amount ?? 0;

    private static IEnumerable<EffectOp> Ops(AbilityDef a) => a.Effects ?? Enumerable.Empty<EffectOp>();

    private static int MaxDamage(AbilityDef a) => Ops(a).OfType<DamageOp>().Select(d => d.Amount).DefaultIfEmpty(0).Max();

    // Heat (or any resource) the ability removes: the sum of its negative resource deltas, as a positive number.
    private static int Dumped(AbilityDef a) => -Ops(a).OfType<ResourceOp>().Where(r => r.Delta < 0).Sum(r => r.Delta);

    // What a shield-strength claim can rest on: which effects are applied and at how many stacks (duration is time).
    private static string Applied(AbilityDef a) =>
        string.Join(",", Ops(a).OfType<ApplyEffectOp>().Select(o => o.EffectId + "x" + o.Stacks).OrderBy(s => s, StringComparer.Ordinal));

    private static bool SameEffects(AbilityDef b, AbilityDef p) =>
        Ops(b).SequenceEqual(Ops(p)) && Equals(b.Variants, p.Variants);

    // The same shape kind, no dimension smaller, at least one larger. A single target growing is not "growing".
    private static bool Grew(AbilityDef b, AbilityDef p)
    {
        if (b.Shape is not { } bs || p.Shape is not { } ps || bs.Kind != ps.Kind) return false;
        var dims = new[] { (bs.Radius, ps.Radius), (bs.Angle1024, ps.Angle1024), (bs.Length, ps.Length), (bs.Width, ps.Width) };
        return dims.All(d => (d.Item2 ?? 0) >= (d.Item1 ?? 0)) && dims.Any(d => (d.Item2 ?? 0) > (d.Item1 ?? 0));
    }

    private static bool Spreads(AbilityDef p) => p.Tags?.Contains("warcry:multi-target") == true;

    // Resource the ability grants its caster: the sum of its positive resource deltas.
    private static int Gained(AbilityDef a) => Ops(a).OfType<ResourceOp>().Where(r => r.Delta > 0).Sum(r => r.Delta);

    /// <summary>Every rule the walk applies. A text matching a phrase must satisfy that phrase's check.</summary>
    private static readonly Rule[] Rules =
    {
        new("cooldown named, cooldown changed", Rx(@"\bcooldown\b"), (b, p) => p.CooldownTicks != b.CooldownTicks),
        new("cooldown falls", Rx(@"cooldown falls|shorter cooldown"), (b, p) => p.CooldownTicks < b.CooldownTicks),
        new("cooldown rises", Rx(@"longer cooldown|\bshort cooldown|adds a cooldown"), (b, p) => p.CooldownTicks > b.CooldownTicks),
        new("cheaper", Rx(@"\bcheaper\b|costs (much )?less\b"), (b, p) => CostOf(p) < CostOf(b)),
        new("costs more", Rx(@"costs more\b"), (b, p) => CostOf(p) > CostOf(b)),
        new("range named, range changed", Rx(@"\brange\b|farther"), (b, p) => p.Range != b.Range),
        new("farther", Rx(@"farther"), (b, p) => p.Range > b.Range),
        new("trades range", Rx(@"trades range|shorter range"), (b, p) => p.Range < b.Range),
        new("area grows", Rx(@"\b(grows|wider|larger|bigger)\b"), Grew),
        new("dumps less", Rx(@"dumps (much )?less|dumps part"), (b, p) => Dumped(p) < Dumped(b)),
        new("dumps more", Rx(@"dumps (even )?more|dumps an extra"), (b, p) => Dumped(p) > Dumped(b)),
        new("hits harder", Rx(@"hits (noticeably |much |far )?harder"), (b, p) => MaxDamage(p) > MaxDamage(b)),
        new("hits for less", Rx(@"hits for less"), (b, p) => MaxDamage(p) < MaxDamage(b)),
        new("shield strength", Rx(@"\b(smaller|bigger|larger|stronger|weaker) shield"), (b, p) => Applied(p) != Applied(b)),
        new("stronger", Rx(@"\bstronger\b(?! shield)"), (b, p) => !SameEffects(b, p)),
        new("extra charge", Rx(@"(extra|second) charge"), (b, p) => p.Charges > b.Charges),
        new("instant", Rx(@"instant cast|casts instantly"), (b, p) => p.CastTicks == 0 && b.CastTicks > 0),
        new("faster cast", Rx(@"faster cast"), (b, p) => p.CastTicks < b.CastTicks),
        new("enemy count", Rx(@"extra enemy|other enemy|two (adjacent )?enemies"), (_, p) => Spreads(p)),
        new("grants resource", Rx(@"burst of \w+|banks (bonus|extra) \w+|refunds (bonus|extra) \w+"), (b, p) => Gained(p) > Gained(b)),
        // No game system reads a Facet-added tag as a synergy hook: verse-surge, heat-surge and momentum-surge have
        // no reader in game/sim, game/src, game/content or game/bots, and no Facet or talent patch keys off a tag.
        // When a system starts reading one, this rule becomes "the added tag has that reader".
        new("tag synergies", Rx(@"\bsynerg"), (_, _) => false),
        // No content field or tag does these: a Window spread is the Warcry tag's primary plus nearest, and a cast
        // has no release step.
        new("no such mechanic", Rx(@"whole arc|releasing it early|released early"), (_, _) => false),
    };

    /// <summary>One shown sentence with the base and patched ability it describes.</summary>
    public sealed record Shown(string Where, string Text, AbilityDef Base, AbilityDef Patched);

    // The gear layer for one Facet: set fields replace, tagAdd/tagRemove retag, effectsAppend ops append.
    private static AbilityDef ApplyFacet(AbilityDef b, FacetPatch f)
    {
        var tags = (b.Tags ?? new ValueList<string>()).Where(t => f.TagRemove?.Contains(t) != true).ToList();
        foreach (var t in f.TagAdd ?? new ValueList<string>()) if (!tags.Contains(t)) tags.Add(t);
        var effects = (f.Effects ?? b.Effects ?? new ValueList<EffectOp>()).Concat(f.EffectsAppend ?? new ValueList<EffectOp>());
        return b with
        {
            CastTicks = f.CastTicks ?? b.CastTicks,
            GcdTicks = f.GcdTicks ?? b.GcdTicks,
            CooldownTicks = f.CooldownTicks ?? b.CooldownTicks,
            Charges = f.Charges ?? b.Charges,
            Cost = f.Cost ?? b.Cost,
            Range = f.Range ?? b.Range,
            Shape = f.Shape ?? b.Shape,
            Interruptible = f.Interruptible ?? b.Interruptible,
            Tags = new ValueList<string>(tags),
            Effects = new ValueList<EffectOp>(effects),
            Variants = f.Variants ?? b.Variants,
        };
    }

    /// <summary>Every Facet and every talent option in the shipped content, with its base and patched ability.</summary>
    public static IReadOnlyList<Shown> AllShown()
    {
        var shown = new List<Shown>();
        foreach (var facet in Reg.Facets.Values)
        {
            var b = Reg.Abilities[facet.AbilityId];
            shown.Add(new Shown("facet " + facet.Id, facet.Text, b, ApplyFacet(b, facet.Patch)));
        }
        foreach (var cls in Reg.Classes.Values)
        {
            foreach (var fork in cls.Talents)
            {
                var b = Reg.Abilities[fork.AbilityId];
                foreach (var option in fork.Options)
                    shown.Add(new Shown("talent " + cls.Id + "/" + fork.AbilityId + "/" + option.Id, option.Text, b, TalentPicks.ApplyPatch(b, option.Patch)));
            }
        }
        return shown;
    }

    private static IEnumerable<string> Violations(Shown s) =>
        Rules.Where(r => r.Phrase.IsMatch(s.Text) && !r.Holds(s.Base, s.Patched))
            .Select(r => s.Where + ": \"" + s.Text + "\" fails [" + r.Name + "]");

    [Fact]
    public void Every_facet_and_talent_text_is_backed_by_its_patch()
    {
        var shown = AllShown();
        var violations = shown.SelectMany(Violations).ToList();

        Assert.True(violations.Count == 0, "text claims the data does not back:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void The_walk_covers_every_facet_and_option_and_the_rules_fire()
    {
        var shown = AllShown();
        var options = Reg.Classes.Values.Sum(c => c.Talents.Sum(f => f.Options.Count));
        var matched = shown.Sum(s => Rules.Count(r => r.Phrase.IsMatch(s.Text)));

        Assert.Equal(Reg.Facets.Count + options, shown.Count);
        Assert.True(Reg.Facets.Count > 0 && options > 0, "no facets or talent options loaded");
        Assert.True(matched >= 30, "only " + matched + " rule matches across " + shown.Count + " texts: the rules check almost nothing");
    }

    private static AbilityDef Base(string id) => Reg.Abilities[id];

    // Each rule rejects a false sentence built on real content (the shapes of the defects this suite was written for).
    public static IEnumerable<object[]> FalseClaims()
    {
        var flare = Base("cinder.flare");
        var snap = Base("cinder.coldSnap");
        var vigil = Base("warden.vigil");
        var guardBreak = Base("warden.guardBreak");
        var dissonance = Base("chanter.dissonance");
        var strike = Base("warden.strike");
        var chorus = Base("chanter.chorus");
        yield return new object[] { "Flare's cooldown falls sharply.", flare, flare with { CooldownTicks = 20 } };
        yield return new object[] { "Cold Snap dumps much less Heat.", snap, ApplyFacet(snap, new FacetPatch { EffectsAppend = new ValueList<EffectOp>(new EffectOp[] { new ResourceOp { Delta = -30, Resource = "heat" } }) }) };
        yield return new object[] { "Cheaper, smaller shield.", vigil, vigil with { Cost = new AbilityCost { Resource = "momentum", Amount = 25 } } };
        yield return new object[] { "Guard Break trades range for a cone.", guardBreak, guardBreak with { CooldownTicks = 20 } };
        yield return new object[] { "Dissonance costs much less Verse.", dissonance, dissonance with { Cost = new AbilityCost { Resource = "verse", Amount = 0 } } };
        yield return new object[] { "Flare's area grows substantially.", flare, flare with { Shape = new AbilityShape { Kind = ShapeKind.Circle, Radius = 2200 } } };
        yield return new object[] { "Strike becomes a cone, catching an extra enemy.", strike, strike with { Shape = new AbilityShape { Kind = ShapeKind.Cone, Radius = 3000, Angle1024 = 90 } } };
        yield return new object[] { "Applies Sundered across the whole arc.", strike, strike };
        yield return new object[] { "Longer cooldown; stronger pulse.", chorus, chorus with { CooldownTicks = 160 } };
        yield return new object[] { "Chorus grants an extra charge, tagged for Verse synergies.", chorus, ApplyFacet(chorus, new FacetPatch { Charges = chorus.Charges + 1, TagAdd = new ValueList<string>(new[] { "verse-surge" }) }) };
        yield return new object[] { "Flare also banks bonus Heat.", flare, ApplyFacet(flare, new FacetPatch { TagAdd = new ValueList<string>(new[] { "heat-surge" }) }) };
    }

    [Theory]
    [MemberData(nameof(FalseClaims))]
    public void A_false_claim_is_rejected(string text, AbilityDef b, AbilityDef p)
    {
        Assert.NotEmpty(Violations(new Shown("probe", text, b, p)));
    }
}
