using System;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-MULTIBOSS-1 through the host: a multi-segment dungeon (the Sunken Bellfoundry) is armed from its seed with one boss
/// pull per segment in its own boss room (<see cref="SeededLayout.ArmSegmented"/>), one boss config per segment at its own
/// actor id (<see cref="HostRunSetup.BossActorIdOf"/>), and the factory arms them all. The Hollow Vault's single boss is
/// untouched: pull "boss" at actor 900, no queued boss.
/// </summary>
public sealed class MultiBossSetupTests
{
    private const string Dungeon = "sunken-bellfoundry";

    private static readonly long[] Seeds = Enumerable.Range(1, 60).Select(i => (long)i).ToArray();

    [Fact(DisplayName = "Armed segmented layout (seeds 1-60): validates, one boss room per segment in fight order, the walked packs reach the forces bar")]
    public void The_armed_layout_has_a_boss_room_per_segment_and_a_route_that_fills_the_bar()
    {
        var reg = ContentLoader.Load();
        var expected = LayoutDef.BossPullIds(reg.Dungeons[Dungeon]);
        Assert.Equal(new[] { "boss-s1", "boss-s2", "boss" }, expected);
        foreach (var seed in Seeds)
        {
            var armed = SeededLayout.ArmSegmented(reg, Dungeon, seed);
            var layout = armed.Layout;
            Assert.Equal(Dungeon, layout.Dungeon);
            Assert.Equal(expected, layout.Boxes.Where(b => LayoutDef.IsBossPull(b.Pull)).Select(b => b.Pull!));
            var walked = layout.Packs.Where(p => !p.Optional).Sum(p => RouteForces.LayoutPackPoints(armed.Forces, p));
            Assert.True(walked >= armed.Forces.Required, $"seed {seed}: the required packs carry {walked} of {armed.Forces.Required}");
            Assert.Contains(layout.Packs, p => p.Optional);
        }
    }

    [Fact(DisplayName = "Build for the Sunken Bellfoundry: three boss pulls at actor ids 900/910/920, three configs, the final boss pull is \"boss\"")]
    public void Build_arms_one_boss_per_segment()
    {
        var reg = ContentLoader.Load();
        var setup = HostRunSetup.Build(reg, Dungeon, 2, "warden", dungeonSeed: 1);

        Assert.Equal(new[] { "boss-s1", "boss-s2", "boss" }, setup.Dungeon.Pulls.Select(p => p.Id));
        var bossIds = setup.Dungeon.Pulls.Select(p => Assert.Single(p.Spawns).ActorId).ToList();
        Assert.Equal(new[] { 900, 910, 920 }, bossIds);
        Assert.Equal(new[] { 900, 910, 920 }, new[] { setup.Boss }.Concat(setup.NextBosses).Select(c => c.BossActorId));
        Assert.Equal(SeededRoster.SegmentBosses(reg, Dungeon), setup.Dungeon.Pulls.Select(p => Assert.Single(p.Spawns).ClassId));
        // The Great Bell's Clappers take the ids after their boss (921, 922), clear of the earlier bosses.
        Assert.Equal(new[] { 921, 922 }, setup.NextBosses[^1].Twins.Select(t => t.ActorId));
        // Every boss spawn stands where its pull's room puts it (the boss room origin), and the pull has a trigger room.
        Assert.All(setup.Dungeon.Pulls, p => Assert.Contains(setup.Layout!.Boxes, b => b.Pull == p.Id && b.Origin is not null));

        var run = HostRunFactory.Create(setup, new RunRequest(1, reg, "warden")).Run;
        Assert.Equal(900, run.Encounter.Boss!.Config.BossActorId);
        Assert.Equal(new[] { 910, 920 }, run.Encounter.NextBosses.Select(c => c.BossActorId));
        Assert.Equal(3, run.Encounter.BossesArmed);
        Assert.All(bossIds, id => Assert.True(run.State.Actors[id].Idle, $"boss {id} should stand idle until its ring engages"));
    }

    [Fact(DisplayName = "Build without a seed for a seeded dungeon fails closed")]
    public void A_seeded_dungeon_needs_a_seed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HostRunSetup.Build(ContentLoader.Load(), Dungeon, 2, "warden"));
        Assert.Contains("dungeonSeed", ex.Message);
    }

    [Fact(DisplayName = "Build for the Hollow Vault: one boss pull \"boss\" at actor 900, no queued boss, whatever seed is passed")]
    public void The_hollow_vault_keeps_its_single_boss()
    {
        var reg = ContentLoader.Load();
        var plain = HostRunSetup.Build(reg, "hollow-vault", 2, "warden");
        var seeded = HostRunSetup.Build(reg, "hollow-vault", 2, "warden", dungeonSeed: 7);

        Assert.Equal(HostRunSetup.BossActorId, plain.Boss.BossActorId);
        Assert.Empty(plain.NextBosses);
        Assert.Equal("boss", plain.Dungeon.Pulls[^1].Id);
        Assert.Equal(plain.Dungeon.Pulls.Select(p => p.Id), seeded.Dungeon.Pulls.Select(p => p.Id));
        Assert.Equal(plain.Dungeon.Pulls.SelectMany(p => p.Spawns), seeded.Dungeon.Pulls.SelectMany(p => p.Spawns));
        Assert.Equal(plain.Layout, seeded.Layout);
        var run = HostRunFactory.Create(plain, new RunRequest(1, reg, "warden")).Run;
        Assert.Equal(0, run.Encounter.BossesArmed);
        Assert.Empty(run.Encounter.NextBosses);
    }
}
