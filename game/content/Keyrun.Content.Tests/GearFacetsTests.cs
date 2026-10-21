using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// BIBLE s7 "Facets retag or reshape an ability": the gear layer of content/apply.ts applyLoadout. Each test equips
/// shipped Facets and asserts the patched ability the run would play, against the shipped content.
/// </summary>
public sealed class GearFacetsTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static FacetResolution Equip(string classId, params (string Slot, string FacetId)[] gear) =>
        GearFacets.Resolve(Reg.Facets, Reg.Abilities, Reg.Classes[classId], gear);

    [Fact]
    public void An_appended_window_without_a_setup_class_takes_the_wearers_class()
    {
        var taunt = Equip("warden", ("charm", "warden.charm.opportunist")).Abilities["warden.taunt"];

        var window = Assert.IsType<WindowOp>(taunt.Effects[^1]);
        Assert.Equal("sundered", window.WindowId);
        Assert.Equal("warden", window.SetupClass);
        Assert.Null(Reg.Facets["warden.charm.opportunist"].Patch.EffectsAppend!.OfType<WindowOp>().Single().SetupClass);
    }

    [Fact]
    public void A_refused_patch_is_skipped_and_the_ability_keeps_its_value_before_it()
    {
        // twinStrike makes Reckoning a line with a width, which this test's refuse check rejects: refused, so
        // Reckoning is not patched at all. On Strike, keenEdge applies and the keystone's wider cone is refused, so
        // Strike keeps keenEdge's cone, not its base shape.
        static string? Refuse(AbilityDef a) => a.Shape.Width is not null ? "line width" : a.Shape.Radius > 3000 ? "too wide" : null;
        var r = GearFacets.Resolve(
            Reg.Facets,
            Reg.Abilities,
            Reg.Classes["warden"],
            new[] { ("weapon", "warden.weapon.twinStrike") },
            Refuse);
        var stacked = GearFacets.Resolve(
            Reg.Facets,
            Reg.Abilities,
            Reg.Classes["warden"],
            new[] { ("weapon", "warden.weapon.keenEdge"), ("keystone", "warden.keystone.sunderedStorm") },
            Refuse);

        Assert.Empty(r.Abilities);
        Assert.Empty(r.Applied);
        Assert.Equal(new SkippedFacet("weapon", "warden.weapon.twinStrike", "line width"), Assert.Single(r.Skipped));
        Assert.Equal(new AbilityShape { Kind = ShapeKind.Cone, Radius = 3000, Angle1024 = 90 }, stacked.Abilities["warden.strike"].Shape);
        Assert.Equal(Reg.Abilities["warden.strike"].Tags, stacked.Abilities["warden.strike"].Tags);
        Assert.Equal(new AppliedFacet(GearSlot.Weapon, "warden.weapon.keenEdge", "warden.strike"), Assert.Single(stacked.Applied));
        Assert.Equal(new SkippedFacet("keystone", "warden.keystone.sunderedStorm", "too wide"), Assert.Single(stacked.Skipped));
    }

    [Fact]
    public void KeenEdge_makes_Strike_a_narrow_frontal_cone()
    {
        var baseStrike = Reg.Abilities["warden.strike"];

        var r = Equip("warden", ("weapon", "warden.weapon.keenEdge"));

        Assert.Equal(ShapeKind.Target, baseStrike.Shape.Kind);
        var strike = Assert.Single(r.Abilities).Value;
        Assert.Equal(new AbilityShape { Kind = ShapeKind.Cone, Radius = 3000, Angle1024 = 90 }, strike.Shape);
        Assert.Equal(baseStrike with { Shape = strike.Shape }, strike);
        Assert.Equal(new AppliedFacet(GearSlot.Weapon, "warden.weapon.keenEdge", "warden.strike"), Assert.Single(r.Applied));
        Assert.Empty(r.Skipped);
    }

    [Fact]
    public void Facets_on_one_ability_stack_in_slot_order_whatever_the_gear_order()
    {
        var baseStrike = Reg.Abilities["warden.strike"];
        var forward = Equip("warden", ("weapon", "warden.weapon.keenEdge"), ("ring", "warden.ring.momentumEngine"), ("keystone", "warden.keystone.sunderedStorm"));
        var backward = Equip("warden", ("keystone", "warden.keystone.sunderedStorm"), ("ring", "warden.ring.momentumEngine"), ("weapon", "warden.weapon.keenEdge"));

        var strike = forward.Abilities["warden.strike"];
        Assert.Equal(forward.Abilities, backward.Abilities);
        Assert.Equal(forward.Applied, backward.Applied);
        Assert.Equal(new[] { GearSlot.Weapon, GearSlot.Ring, GearSlot.Keystone }, forward.Applied.Select(a => a.Slot));
        // keystone (last) wins the shape; the ring appends its op after the base effects; the keystone's tag is added.
        Assert.Equal(new AbilityShape { Kind = ShapeKind.Cone, Radius = 3500, Angle1024 = 200 }, strike.Shape);
        Assert.Equal(baseStrike.Effects.Concat(new EffectOp[] { new ResourceOp { Resource = "momentum", Delta = 10 } }), strike.Effects);
        Assert.Equal(new[] { "setup:sundered", "warcry:multi-target" }, strike.Tags);
    }

    [Fact]
    public void An_appended_op_reaches_every_verse_variant()
    {
        var baseHymn = Reg.Abilities["chanter.hymn"];
        var hot = new ApplyEffectOp { EffectId = "mendingHot", DurationTicks = 200, Stacks = 1 };

        var hymn = Equip("chanter", ("armor", "chanter.armor.hymnalMending")).Abilities["chanter.hymn"];

        Assert.Equal(baseHymn.Effects.Append(hot), hymn.Effects);
        Assert.NotNull(hymn.Variants);
        Assert.Equal(baseHymn.Variants!.Keys, hymn.Variants!.Keys);
        foreach (var (verse, ops) in hymn.Variants)
        {
            Assert.Equal(baseHymn.Variants[verse].Append(hot), ops);
        }
    }

    [Fact]
    public void TagAdd_keeps_a_tag_already_present_once()
    {
        var strike = Reg.Abilities["warden.strike"] with { Tags = new ValueList<string>(new[] { "setup:sundered", "warcry:multi-target" }) };

        var patched = GearFacets.ApplyPatch(strike, Reg.Facets["warden.keystone.sunderedStorm"].Patch, "warden");

        Assert.Equal(new[] { "setup:sundered", "warcry:multi-target" }, patched.Tags);
    }

    [Fact]
    public void The_facet_layer_applies_on_top_of_the_talent_layer()
    {
        var warden = Reg.Classes["warden"];
        var fork = warden.Talents.Select((f, i) => (f, i)).Single(x => x.f.AbilityId == "warden.strike").i;
        var rend = warden.Talents[fork].Options.Select((o, i) => (o, i)).Single(x => x.o.Id == "rend").i;
        var talented = TalentPicks.Start(warden).Pick(fork, rend).Resolve(Reg.Abilities);
        var abilities = new ValueMap<AbilityDef>(Reg.Abilities.Select(kv =>
            talented.TryGetValue(kv.Key, out var t) ? new KeyValuePair<string, AbilityDef>(kv.Key, t) : kv));

        var strike = GearFacets.Resolve(Reg.Facets, abilities, warden, new[] { ("weapon", "warden.weapon.keenEdge") }).Abilities["warden.strike"];

        Assert.Equal(talented["warden.strike"].Effects, strike.Effects);
        Assert.Equal(ShapeKind.Cone, strike.Shape.Kind);
    }

    [Fact]
    public void A_facet_the_content_cannot_honour_is_skipped_and_named()
    {
        var r = Equip(
            "warden",
            ("weapon", "weapon-edge"),                        // an old save's placeholder Facet: not in content
            ("armor", "warden.weapon.keenEdge"),              // a weapon Facet claimed by the armor slot
            ("ring", "chanter.ring.swiftVerse"),              // another class's ability
            ("ring", "warden.ring.momentumEngine"),           // a second ring
            ("belt", "warden.weapon.keenEdge"));              // not a gear slot

        Assert.Empty(r.Abilities);
        Assert.Empty(r.Applied);
        Assert.Equal(
            new[]
            {
                new SkippedFacet("ring", "warden.ring.momentumEngine", "more than one item in the slot"),
                new SkippedFacet("belt", "warden.weapon.keenEdge", "unknown gear slot"),
                new SkippedFacet("weapon", "weapon-edge", "unknown facet id"),
                new SkippedFacet("armor", "warden.weapon.keenEdge", "facet belongs to slot weapon"),
                new SkippedFacet("ring", "chanter.ring.swiftVerse", "ability chanter.cadence is not in the class kit"),
            },
            r.Skipped);
    }

    [Fact]
    public void No_gear_patches_nothing()
    {
        var r = Equip("warden");

        Assert.Empty(r.Abilities);
        Assert.Empty(r.Applied);
        Assert.Empty(r.Skipped);
    }
}
