using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// A talent's text must be true in both Chanter verses (BIBLE s5 "Every other ability behaves differently per verse")
/// The sim plays a verse ability's <c>Variants</c>
/// (Abilities.ResolveVerse replaces Effects with the caster's verse), so a talent that patched only
/// <see cref="AbilityDef.Effects"/> used to be dead on every verse ability. These tests drive
/// <see cref="TalentPicks.Resolve"/> against the shipped content.
/// </summary>
public sealed class TalentVariantPatchTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static AbilityDef Talented(string classId, string abilityId, string optionId)
    {
        var cls = Reg.Classes[classId];
        var fork = cls.Talents.Select((f, i) => (f, i)).Single(x => x.f.AbilityId == abilityId).i;
        var option = cls.Talents[fork].Options.Select((o, i) => (o, i)).Single(x => x.o.Id == optionId).i;
        return TalentPicks.Start(cls).Pick(fork, option).Resolve(Reg.Abilities)[abilityId];
    }

    private static IReadOnlyList<EffectOp> Verse(AbilityDef def, string verse) => def.Variants![verse];

    [Theory]
    [InlineData("mend", "slowed")]
    [InlineData("ward", "silenced")]
    public void Amplify_makes_Dissonance_hit_for_170_in_both_verses_and_keeps_the_verse_effect(string verse, string effectId)
    {
        var dissonance = Talented("chanter", "chanter.dissonance", "amplify");

        var ops = Verse(dissonance, verse);
        Assert.Equal(170, Assert.IsType<DamageOp>(Assert.Single(ops, o => o is DamageOp)).Amount);
        Assert.Single(ops, o => o is ApplyEffectOp a && a.EffectId == effectId);
        Assert.Single(ops, o => o is PayoffOp);
    }

    [Theory]
    [InlineData("mend", "mendingHot")]
    [InlineData("ward", "wardShield")]
    public void Resonance_makes_Attuned_last_100_ticks_in_both_verses(string verse, string effectId)
    {
        var cadence = Talented("chanter", "chanter.cadence", "resonance");

        var ops = Verse(cadence, verse);
        var window = Assert.IsType<WindowOp>(Assert.Single(ops, o => o is WindowOp));
        Assert.Equal("attuned", window.WindowId);
        Assert.Equal(100, window.DurationTicks);
        Assert.Single(ops, o => o is ApplyEffectOp a && a.EffectId == effectId);
    }

    [Theory]
    [InlineData("mend", "hymnHeal")]
    [InlineData("ward", "hymnSpeed")]
    public void Grand_Hymn_makes_the_hymn_window_last_220_ticks_in_both_verses(string verse, string effectId)
    {
        var hymn = Talented("chanter", "chanter.hymn", "grandHymn");

        var ops = Verse(hymn, verse);
        Assert.Equal(220, Assert.IsType<WindowOp>(Assert.Single(ops, o => o is WindowOp)).DurationTicks);
        Assert.Single(ops, o => o is ApplyEffectOp a && a.EffectId == effectId);
        Assert.Equal(3500, hymn.Shape.Radius);
    }

    [Theory]
    [InlineData("mend", 1)]
    [InlineData("ward", -1)]
    public void Crescendo_adds_a_payoff_to_Refrain_in_both_verses_and_keeps_the_verse_swap(string verse, int delta)
    {
        var refrain = Talented("chanter", "chanter.refrain", "crescendo");

        var ops = Verse(refrain, verse);
        Assert.Single(ops, o => o is PayoffOp);
        Assert.Equal(delta, Assert.IsType<ResourceOp>(Assert.Single(ops, o => o is ResourceOp)).Delta);
        Assert.Equal(Verse(Reg.Abilities["chanter.refrain"], verse).Count + 1, ops.Count);
    }

    [Fact]
    public void A_patch_that_leaves_effects_alone_leaves_every_variant_alone()
    {
        foreach (var cls in Reg.Classes.Values)
        {
            foreach (var fork in cls.Talents)
            {
                var baseDef = Reg.Abilities[fork.AbilityId];
                foreach (var option in fork.Options.Where(o => o.Patch.Effects is null && o.Patch.Variants is null))
                {
                    Assert.Equal(baseDef.Variants, TalentPicks.ApplyPatch(baseDef, option.Patch).Variants);
                }
            }
        }
    }

    [Fact]
    public void With_nothing_picked_every_ability_resolves_exactly_as_its_base()
    {
        foreach (var cls in Reg.Classes.Values)
        {
            Assert.Empty(TalentPicks.Start(cls).Resolve(Reg.Abilities));
            foreach (var fork in cls.Talents)
            {
                var baseDef = Reg.Abilities[fork.AbilityId];
                Assert.Equal(baseDef, TalentPicks.ApplyPatch(baseDef, new AbilityPatch()));
            }
        }
    }

    [Fact]
    public void Every_changed_op_of_an_effects_patch_lands_in_every_verse()
    {
        foreach (var cls in Reg.Classes.Values)
        {
            foreach (var fork in cls.Talents)
            {
                var baseDef = Reg.Abilities[fork.AbilityId];
                if (baseDef.Variants is not { Count: > 0 }) continue;
                foreach (var option in fork.Options.Where(o => o.Patch.Effects is not null && o.Patch.Variants is null))
                {
                    var patched = TalentPicks.ApplyPatch(baseDef, option.Patch);
                    foreach (var op in option.Patch.Effects!.Where(o => !baseDef.Effects.Contains(o)))
                    {
                        foreach (var verse in patched.Variants!)
                            Assert.True(verse.Value.Contains(op), option.Id + " " + verse.Key + " lacks " + op);
                    }
                }
            }
        }
    }

    [Fact]
    public void A_patch_that_drops_a_base_op_drops_it_from_every_verse_and_explicit_variants_still_replace_whole()
    {
        var dissonance = Reg.Abilities["chanter.dissonance"];
        var dropPayoff = new AbilityPatch { Effects = new ValueList<EffectOp>(new EffectOp[] { dissonance.Effects[0] }) };

        var patched = TalentPicks.ApplyPatch(dissonance, dropPayoff);
        foreach (var verse in patched.Variants!)
        {
            Assert.DoesNotContain(verse.Value, o => o is PayoffOp);
            Assert.Equal(Verse(dissonance, verse.Key).Count - 1, verse.Value.Count);
        }

        var explicitVariants = new ValueMap<ValueList<EffectOp>>(new[]
        {
            new KeyValuePair<string, ValueList<EffectOp>>("mend", new ValueList<EffectOp>(new EffectOp[] { new PayoffOp() })),
        });
        var replaced = TalentPicks.ApplyPatch(dissonance, dropPayoff with { Variants = explicitVariants });
        Assert.Equal(explicitVariants, replaced.Variants);
    }
}
