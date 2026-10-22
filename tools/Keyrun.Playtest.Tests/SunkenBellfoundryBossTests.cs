using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// D65/D66, ruling R-BOSS-1 for the Sunken Bellfoundry's three bosses: at key 2 with the default party (the autorun
/// Warden and the two bots, <see cref="BossArena"/>), each fight lasts 90 to 180 s (1800 to 3600 ticks) and each of its
/// mechanics happens, answered by the party, at least twice. Bosses are immune to CC (R-BOSS-3: run.json
/// stunImmuneEnemies). The third boss is the hardest and asks two seats to answer together (its Lock and its Clappers).
/// <list type="bullet">
/// <item>Foundry-Master Ommet (segment 1): Pour Slag, a frontal cone the tank turns away (no one but his target is hit);
/// Temper, a self-heal kicked before it lands.</item>
/// <item>The Brine Mother (segment 2): Undertow drags the whole party in, then Boiling Surge boils the ground around her
/// (no one but her target may still stand in it); below 60% Drowning Hymn, a party-wide cast kicked before it lands.</item>
/// <item>Old Sorrow, the Drowned Bell (segment 3): Clangour, a heavier cone turned away; the Iron Clappers share a Break
/// bar staggered by kicks from two seats; Still the Bell, a Lock broken by one class's Setup and another's Payoff.</item>
/// </list>
/// </summary>
public sealed class SunkenBellfoundryBossTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the measured numbers are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public SunkenBellfoundryBossTests(ITestOutputHelper output) => _output = output;

    private static void AssertBand(BossArena.Fight f, string what)
    {
        Assert.True(f.Clear is not null, $"{what}: the boss never died: {f.Line(what)}");
        Assert.True(f.Ticks >= BossArena.MinFightTicks && f.Ticks <= BossArena.MaxFightTicks,
            $"{what}: fight lasted {f.Ticks} ticks, outside [{BossArena.MinFightTicks}, {BossArena.MaxFightTicks}]: {f.Line(what)}");
    }

    [Fact(DisplayName = "R-BOSS-1: Foundry-Master Ommet lasts 90-180 s at key 2; Pour Slag turned away and Temper kicked, each at least twice")]
    public void Ommet_fight_lasts_the_window_and_teaches_both_mechanics()
    {
        var f = BossArena.Drive("bellfoundry.ommet");
        var pour = f.AvoidedHits("ommet.pourSlag");
        var kicked = f.Kicked("ommet.temper");
        var healed = f.Landed("ommet.temper");
        _output.WriteLine($"{f.Line("ommet")} pourSlag={pour.Landed} answered={pour.Answered} temperKicked={kicked} temperLanded={healed}");
        AssertBand(f, "Ommet");
        Assert.True(pour.Answered >= 2, $"Pour Slag answered {pour.Answered} of {pour.Landed}");
        Assert.True(kicked >= 2, $"Temper kicked {kicked} times ({healed} landed)");
        // He has no Lock and no adds (HostRunSetup.BuildBossConfig, D66): the armed rules open none and spawn none.
        Assert.Equal((0, 0), f.Locks);
        Assert.Empty(f.Setup.Boss.Twins);
    }

    [Fact(DisplayName = "R-BOSS-1: the Brine Mother lasts 90-180 s at key 2; Undertow and Surge escaped and Drowning Hymn kicked, each at least twice")]
    public void Brine_mother_fight_lasts_the_window_and_teaches_both_mechanics()
    {
        var f = BossArena.Drive("bellfoundry.brineMother");
        var surge = f.AvoidedHits("brineMother.boilingSurge");
        var undertows = f.Landed("brineMother.undertow");
        var kicked = f.Kicked("brineMother.drowningHymn");
        var sung = f.Landed("brineMother.drowningHymn");
        var dragged = f.DraggedIn("brineMother.undertow", 4500);
        _output.WriteLine($"{f.Line("brineMother")} undertow={undertows} draggedIn={dragged} surge={surge.Landed} escaped={surge.Answered} hymnKicked={kicked} hymnLanded={sung}");
        AssertBand(f, "Brine Mother");
        Assert.True(undertows >= 2, $"Undertow landed {undertows} times");
        Assert.True(dragged >= 2, $"Undertow dragged a non-tank into the Surge's ground {dragged} times");
        Assert.True(surge.Answered >= 2, $"Boiling Surge escaped {surge.Answered} of {surge.Landed}");
        Assert.True(kicked >= 2, $"Drowning Hymn kicked {kicked} times ({sung} landed)");
        // Drowning Hymn is gated below 60% (SCRIPT-GATE): none is begun while she is above it.
        var firstHymn = f.Steps.FirstOrDefault(s => s.Events.Any(e => e.Type == "telegraph" && BossArena.Str(e, "cast") == "brineMother.drowningHymn"));
        Assert.NotNull(firstHymn);
        var before = f.Steps.TakeWhile(s => s != firstHymn).LastOrDefault();
        var boss = (before ?? firstHymn!).After.Actors[BossArena.BossId];
        Assert.True(boss.Health * 10000 < boss.MaxHealth * 6000, $"the first Hymn began at {boss.Health}/{boss.MaxHealth}");
        Assert.Equal((0, 0), f.Locks);
    }

    [Fact(DisplayName = "R-BOSS-1 / R-SORROW-1: Old Sorrow lasts 90-180 s at key 2; Clangour turned away, the Clappers staggered by two seats and Resonance kicked or ridden out")]
    public void Great_bell_fight_lasts_the_window_and_teaches_all_three_mechanics()
    {
        var f = BossArena.Drive("bellfoundry.greatBell");
        var clang = f.AvoidedHits("greatBell.clangour");
        var (staggers, twoSeat) = f.Staggers("clapperBreak");
        var (opened, broken) = f.Locks;
        _output.WriteLine($"{f.Line("greatBell")} clangour={clang.Landed} answered={clang.Answered} staggers={staggers} twoSeat={twoSeat} addDeaths={f.AddDeaths} locksOpened={opened} broken={broken}");
        AssertBand(f, "Old Sorrow");
        Assert.True(clang.Answered >= 2, $"Clangour answered {clang.Answered} of {clang.Landed}");
        Assert.True(twoSeat >= 2, $"Clappers staggered by two seats {twoSeat} times (of {staggers})");
        // R-SORROW-1 item 1: Still the Bell (the Lock) is gone; Resonance is a kick, cast at least three times and answered by a kick at least once.
        var resonanceKicked = f.Kicked("greatBell.resonance");
        var resonanceLanded = f.Landed("greatBell.resonance");
        _output.WriteLine($"resonance kicked={resonanceKicked} landed={resonanceLanded}");
        Assert.True(resonanceKicked >= 1, $"Resonance kicked {resonanceKicked} times ({resonanceLanded} landed)");
        Assert.True(resonanceKicked + resonanceLanded >= 3, $"Resonance cast {resonanceKicked + resonanceLanded} times");
        Assert.Equal((0, 0), f.Locks);
        Assert.Equal(new[] { "bellfoundry.clapperA", "bellfoundry.clapperB" }, f.Setup.Boss.Twins.Select(t => t.ClassId));
        Assert.Equal("clapperBreak", f.Setup.Boss.TwinsBar.Id);
        Assert.Empty(f.Setup.Boss.SuppressedBossCasts); // no Lock ability, so nothing is suppressed
    }

    [Fact(DisplayName = "D66 boss arena: the Hollow Warden's armed rules are unchanged by the generalised boss config")]
    public void Hollow_warden_config_is_unchanged()
    {
        var reg = Keyrun.Content.ContentLoader.Load();
        var cfg = Keyrun.Host.HostRunSetup.Build(reg, "hollow-vault", BossArena.Key, Keyrun.Host.Lobby.DefaultClassId).Boss;
        Assert.Equal(new[] { "hollowWarden.twinA", "hollowWarden.twinB" }, cfg.Twins.Select(t => t.ClassId));
        Assert.Equal(new[] { 4000L, -4000L }, cfg.Twins.Select(t => t.Offset.X));
        Assert.Equal("twinsBreak", cfg.TwinsBar.Id);
        Assert.Equal(5000, cfg.LockThresholdBp);
        Assert.True(cfg.CastLock);
        Assert.Equal(new[] { "hollowWarden.lock" }, cfg.SuppressedBossCasts);
    }

    [Fact(DisplayName = "R-BOSS-3: the three Bellfoundry bosses are immune to CC; their Clappers are not")]
    public void Bosses_are_cc_immune()
    {
        var reg = Keyrun.Content.ContentLoader.Load();
        foreach (var boss in new[] { "bellfoundry.ommet", "bellfoundry.brineMother", "bellfoundry.greatBell" })
        {
            Assert.Contains(boss, reg.Run.StunImmuneEnemies);
        }
        Assert.DoesNotContain("bellfoundry.clapperA", reg.Run.StunImmuneEnemies);
        Assert.DoesNotContain("bellfoundry.clapperB", reg.Run.StunImmuneEnemies);
        var f = BossArena.Drive("bellfoundry.ommet");
        Assert.Contains("bellfoundry.ommet", f.Run.Content.StunImmuneClassIds!);
    }
}
