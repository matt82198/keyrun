using System;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-BOTS-9 (BIBLE section 7, "Facets change function"; section 5 Chanter, "Every other ability behaves differently per
/// verse"): a run setup carries each verse ability's <c>variants</c>, and the loader must keep them. A loader that drops
/// them runs every Chanter verse ability as its static Effects whatever the verse, so the setup plays a different kit
/// than the content (the fork-parity converter, ForkParityTests.ToSim, keeps them). These facts drive the loaded
/// ability through <see cref="Abilities.ResolveVerse"/>, the sim's own verse rule, so a dropped variant shows as the
/// Ward verse playing the Mend ops.
/// </summary>
public sealed class RunSetupVariantsTests
{
    private static AbilityDef Loaded(string abilityId) =>
        RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), 7).Content.Abilities[abilityId];

    [Theory(DisplayName = "R-BOTS-9: the run setup loader keeps a verse ability's mend and ward variants")]
    [InlineData("chanter.cadence")]
    [InlineData("chanter.chorus")]
    [InlineData("chanter.dissonance")]
    [InlineData("chanter.hymn")]
    [InlineData("chanter.refrain")]
    public void Loader_keeps_the_variants(string abilityId)
    {
        var def = Loaded(abilityId);
        Assert.NotNull(def.Variants);
        Assert.Equal(new[] { Abilities.MendVerse, Abilities.WardVerse }, def.Variants!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.NotEqual(def.Variants[Abilities.MendVerse], def.Variants[Abilities.WardVerse]);
    }

    [Fact(DisplayName = "R-BOTS-9: a loaded Chorus in the Ward verse plays the Ward pulse, not the Mend heal")]
    public void Ward_verse_plays_the_ward_variant()
    {
        var def = Loaded("chanter.chorus");
        var mend = Abilities.ResolveVerse(def, ChanterInVerse(1));
        var ward = Abilities.ResolveVerse(def, ChanterInVerse(0));
        Assert.Contains(mend.Effects, op => op is HealOp);
        Assert.DoesNotContain(ward.Effects, op => op is HealOp);
        Assert.Contains(ward.Effects, op => op is ApplyEffectOp { EffectId: "wardPulse" });
    }

    private static ActorState ChanterInVerse(long verse)
    {
        var a = CombatCastTestKit.Make(1, ActorKind.Player, "chanter", ActorRole.Healer, maxHealth: 1000, maxResource: 1);
        a.Resource = verse;
        return a;
    }
}
