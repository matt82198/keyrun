using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The melee ability set the strike cue reads (KF1; orchestrator ruling R9: every melee hit shows an impact). The live
/// list is the C#-only data file game/content/data/melee-abilities.json, because the root content/ tree is a frozen
/// oracle root and its abilities JSON is not edited. Drives the real loader and <see cref="StrikeCueRule.MeleeIds(IEnumerable{string})"/>.
/// </summary>
public sealed class MeleeAbilityTests
{
    [Fact]
    public void Melee_set_holds_the_player_and_enemy_melee_swings()
    {
        ContentRegistry reg = ContentLoader.Load();
        IReadOnlySet<string> melee = StrikeCueRule.MeleeIds(reg.MeleeAbilities);

        Assert.Contains("warden.strike", melee);
        Assert.Contains("choir.meleeSwing", melee);
        Assert.Contains("pair.meleeSwing", melee);
        Assert.Contains("hollowWarden.cleave", melee);
    }

    [Theory]
    [InlineData("choir.healCast")]
    [InlineData("seed.lash")]
    public void Ranged_casts_are_not_melee(string abilityId)
    {
        ContentRegistry reg = ContentLoader.Load();

        Assert.DoesNotContain(abilityId, StrikeCueRule.MeleeIds(reg.MeleeAbilities));
    }

    [Fact]
    public void Strike_cue_fires_for_a_data_listed_melee_swing()
    {
        ContentRegistry reg = ContentLoader.Load();
        var cues = StrikeCueRule.Cues(
            new[]
            {
                new StrikeEvent(StrikeCueRule.ResolveEvent, 20, 1, "warden.strike"),
                new StrikeEvent(StrikeCueRule.DamageEvent, 20, 1),
            },
            StrikeCueRule.MeleeIds(reg.MeleeAbilities),
            _ => 0);

        Assert.Single(cues);
    }
}
