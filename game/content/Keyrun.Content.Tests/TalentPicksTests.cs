using System;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// BIBLE s6 "Talents: forks on the five": each rotational ability has one fork of two or three mutually exclusive
/// options, 12-15 choices per class, and "Respec is free on the screen, always". <see cref="TalentPicks"/> is the pure
/// pick state the Screen's talent panel renders; these tests drive it against the shipped content.
/// </summary>
public sealed class TalentPicksTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private static ClassDef Warden => Reg.Classes["warden"];

    [Fact]
    public void A_fresh_build_picks_nothing_and_leaves_every_ability_at_its_base()
    {
        var picks = TalentPicks.Start(Warden);

        Assert.Equal("warden", picks.ClassId);
        Assert.Equal(5, picks.ForkCount);
        Assert.Equal(0, picks.Count);
        Assert.Empty(picks.Choices);
        Assert.Empty(picks.Resolve(Reg.Abilities));
        for (var f = 0; f < picks.ForkCount; f++) Assert.Null(picks.PickedOption(f));
    }

    [Fact]
    public void Picking_a_side_patches_that_ability_the_way_the_content_patch_says()
    {
        var strikeFork = Warden.Talents.Select((fork, i) => (fork, i)).Single(x => x.fork.AbilityId == "warden.strike").i;
        var rend = Warden.Talents[strikeFork].Options.Select((o, i) => (o, i)).Single(x => x.o.Id == "rend").i;

        var picks = TalentPicks.Start(Warden).Pick(strikeFork, rend);
        var resolved = picks.Resolve(Reg.Abilities);

        Assert.Equal("rend", picks.PickedOption(strikeFork));
        Assert.Equal("rend", picks.Choices["warden.strike"]);
        var strike = Assert.Single(resolved).Value;
        var window = Assert.IsType<WindowOp>(strike.Effects.Single(e => e is WindowOp));
        Assert.Equal(100, window.DurationTicks); // Rend: "Sundered lasts 5s instead of 3s" (20 Hz)
        var baseStrike = Reg.Abilities["warden.strike"];
        Assert.Equal(baseStrike.GcdTicks, strike.GcdTicks); // a field the patch omits keeps its base value
        Assert.Equal(baseStrike.Tags, strike.Tags);
    }

    [Fact]
    public void The_options_of_one_fork_are_mutually_exclusive()
    {
        var fork = Warden.Talents.Select((f, i) => (f, i)).Single(x => x.f.AbilityId == "warden.strike").i;
        var opts = Warden.Talents[fork].Options.Select(o => o.Id).ToList();

        var picks = TalentPicks.Start(Warden).Pick(fork, opts.IndexOf("rend")).Pick(fork, opts.IndexOf("riposte"));
        var strike = picks.Resolve(Reg.Abilities)["warden.strike"];

        Assert.Equal(1, picks.Count);
        Assert.Equal("riposte", picks.PickedOption(fork));
        // Riposte's patch is tags only. The free GCD is conditional (only after Brace, sim Combat/Riposte.cs), so the
        // patch must NOT zero the GCD outright (f-d37651d1: gcdTicks 0 made every Strike GCD-free).
        Assert.Contains("riposte:free-after-brace", strike.Tags);
        Assert.Equal(Reg.Abilities["warden.strike"].GcdTicks, strike.GcdTicks);
        Assert.Equal(Reg.Abilities["warden.strike"].Effects, strike.Effects); // Rend's patch is gone
    }

    [Fact]
    public void Respec_is_free_and_returns_the_untalented_build()
    {
        var picks = TalentPicks.Start(Warden);
        for (var f = 0; f < picks.ForkCount; f++) picks = picks.Pick(f, picks.Forks[f].Options.Count - 1);
        Assert.Equal(5, picks.Count);

        var respecced = picks.Respec();

        Assert.Equal(0, respecced.Count);
        Assert.Empty(respecced.Resolve(Reg.Abilities));
        Assert.Equal(TalentPicks.Start(Warden), respecced);
        Assert.Equal(5, respecced.Pick(0, 0).Pick(1, 0).Pick(2, 0).Pick(3, 0).Pick(4, 0).Count); // respec never locks
    }

    [Fact]
    public void A_pick_outside_the_forks_fails_closed()
    {
        var picks = TalentPicks.Start(Warden);
        Assert.Throws<ArgumentOutOfRangeException>(() => picks.Pick(picks.ForkCount, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => picks.Pick(0, picks.Forks[0].Options.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => picks.Pick(-1, 0));
    }

    [Theory]
    [InlineData("warden")]
    [InlineData("cinder")]
    [InlineData("chanter")]
    public void Every_shipped_option_resolves_and_changes_its_ability(string classId)
    {
        var cls = Reg.Classes[classId];
        var start = TalentPicks.Start(cls);
        var total = 0;
        for (var f = 0; f < start.ForkCount; f++)
        {
            Assert.InRange(start.Forks[f].Options.Count, 2, 3);
            for (var o = 0; o < start.Forks[f].Options.Count; o++)
            {
                total++;
                var id = start.Forks[f].AbilityId;
                var patched = start.Pick(f, o).Resolve(Reg.Abilities)[id];
                Assert.Equal(id, patched.Id);
                Assert.NotEqual(Reg.Abilities[id], patched);
            }
        }
        Assert.InRange(total, 12, 15);
    }

    [Fact]
    public void ApplyPatch_replaces_only_the_fields_the_patch_sets()
    {
        var baseStrike = Reg.Abilities["warden.strike"];
        var patched = TalentPicks.ApplyPatch(baseStrike, new AbilityPatch { CooldownTicks = 77, Name = "Strike II" });

        Assert.Equal(baseStrike with { CooldownTicks = 77, Name = "Strike II" }, patched);
        Assert.Equal(baseStrike, TalentPicks.ApplyPatch(baseStrike, new AbilityPatch()));
    }
}
