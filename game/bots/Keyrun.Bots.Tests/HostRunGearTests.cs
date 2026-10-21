using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using AbilityDef = Keyrun.Sim.Combat.AbilityDef;
using AbilityShape = Keyrun.Sim.Combat.AbilityShape;
using ComboOp = Keyrun.Sim.Combat.ComboOp;
using EffectOp = Keyrun.Sim.Combat.EffectOp;
using ShapeKind = Keyrun.Sim.Combat.ShapeKind;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Finding f-ccb1cefb (design/BIBLE.md s7 "Facets change function"): gear equipped on the between-runs screen must
/// change the abilities of the run the HOST starts, not only the run <see cref="RunStart.Of"/> composes in isolation
/// (RunStartGearTests). Every case builds the REAL host setup (game/src/Host/HostRunSetup.cs, linked into this project,
/// over <see cref="ContentLoader.Load()"/>) and walks the same chain game/src/Host/SimDriver.cs StartRun walks:
/// <see cref="HostRunSetup.Build"/>, <see cref="HostRunSetup.StartFor"/> (the content Facet patches through
/// <see cref="HostRunSetup.GearPatches"/>), the rotation index, <see cref="HostRunSetup.WithBotLoadouts"/> and
/// <see cref="RunRoot.CreateRun"/>. The Facets are the shipped Warden content (content/data/facets/warden.json):
/// the keystone <c>warden.keystone.eternalVigil</c> (Vigil cooldown 120 to 40, tagged payoff, a combo op appended) and
/// the weapon <c>warden.weapon.keenEdge</c> (Strike from a single target to a 3000 mm, 90/1024 cone).
/// </summary>
public sealed class HostRunGearTests
{
    private const string Vigil = "warden.vigil";
    private const string Strike = "warden.strike";
    private const string EternalVigil = "warden.keystone.eternalVigil";
    private const string KeenEdge = "warden.weapon.keenEdge";

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    [Fact(DisplayName = "Host run: an equipped keystone and weapon Facet change the human's abilities in the created run")]
    public void Equipped_keystone_and_weapon_reach_the_host_run()
    {
        var progression = Progression.Start(1)
            .Take(ItemOf("keystone", EternalVigil))
            .Take(ItemOf("weapon", KeenEdge));

        var (run, start) = StartHostRun(progression);
        var vigil = run.Content.Abilities[Vigil];
        var strike = run.Content.Abilities[Strike];

        // The keystone: Vigil's cooldown is the Facet's, it is a Payoff and it closes Windows (combo op appended).
        Assert.Equal(40, vigil.CooldownTicks);
        Assert.Contains("payoff", vigil.Tags ?? Array.Empty<string>());
        Assert.IsType<ComboOp>(vigil.Effects[^1]);
        Assert.Equal("combos", ((ComboOp)vigil.Effects[^1]).ComboTable);

        // The weapon: Strike is the Facet's narrow frontal cone.
        Assert.Equal(new AbilityShape(ShapeKind.Cone, Radius: 3000, Angle1024: 90), strike.Shape);

        // Both Facets were applied, keystone included, for the human seat.
        Assert.Equal(
            new[] { EternalVigil, KeenEdge },
            start.Gear!.Facets.Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Contains(start.Gear.Gear, g => g.Slot == "keystone");
    }

    [Fact(DisplayName = "Host run: without the gear the human's abilities are the base content ones")]
    public void Without_gear_the_host_run_has_the_base_abilities()
    {
        var setup = Setup();
        var baseVigil = setup.Content.Abilities[Vigil];
        var baseStrike = setup.Content.Abilities[Strike];

        // The base content differs from both Facets, so the geared case above cannot pass on the base kit.
        Assert.Equal(120, baseVigil.CooldownTicks);
        Assert.DoesNotContain("payoff", baseVigil.Tags ?? Array.Empty<string>());
        Assert.DoesNotContain(baseVigil.Effects, e => e is ComboOp);
        Assert.Equal(ShapeKind.Target, baseStrike.Shape.Kind);

        foreach (var progression in new Progression?[] { Progression.Start(1), null })
        {
            var (run, _) = StartHostRun(progression);
            AssertSameAbility(baseVigil, run.Content.Abilities[Vigil]);
            AssertSameAbility(baseStrike, run.Content.Abilities[Strike]);
        }
    }

    [Fact(DisplayName = "Host run: the keystone alone changes only the ability its Facet names")]
    public void The_keystone_alone_patches_only_its_ability()
    {
        var setup = Setup();
        var progression = Progression.Start(1).Take(ItemOf("keystone", EternalVigil));

        var (run, _) = StartHostRun(progression);

        Assert.Equal(40, run.Content.Abilities[Vigil].CooldownTicks);
        AssertSameAbility(setup.Content.Abilities[Strike], run.Content.Abilities[Strike]);
    }

    // AbilityDef is a record whose Effects/Tags/Variants are lists (reference equality across two Build calls), so the
    // comparison is field by field with the lists compared by value.
    private static void AssertSameAbility(AbilityDef expected, AbilityDef actual)
    {
        Assert.Equal(
            expected with { Effects = Array.Empty<EffectOp>(), Tags = null, Variants = null },
            actual with { Effects = Array.Empty<EffectOp>(), Tags = null, Variants = null });
        Assert.Equal(expected.Effects, actual.Effects);
        Assert.Equal(expected.Tags ?? Array.Empty<string>(), actual.Tags ?? Array.Empty<string>());
        Assert.Equal(expected.Variants?.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Variants?.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    private static HostRunSetup Setup() => HostRunSetup.Build(Registry.Value, "hollow-vault", 2, "warden");

    // SimDriver.StartRun's chain (game/src/Host/SimDriver.cs, the run for a CLI start with the screen's progression).
    private static (Run Run, RunStart Start) StartHostRun(Progression? progression)
    {
        var registry = Registry.Value;
        var setup = Setup();
        var start = setup.StartFor(null, progression, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        return (RunRoot.CreateRun(1, party, setup.Dungeon, setup.Content, options), start);
    }

    // A screen reward for the content Facet (Lobby.SlotPool builds the sim Facet the same way).
    private static Item ItemOf(string slot, string facetId)
    {
        var f = Registry.Value.Facets[facetId];
        Assert.Equal(slot, f.Slot.ToString().ToLowerInvariant());
        var facet = new Facet(f.Id, f.Text, f.AbilityId);
        return new Item(FormattableString.Invariant($"reward-{slot}-{facet.Id}-t1-0"), slot, 1, facet, Rewards.StartingCharges);
    }
}
