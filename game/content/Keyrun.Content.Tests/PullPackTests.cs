using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// PULL-PACKS (finding f-82d9390e; Matt 2026-10-20, verbatim: "the room design is just super lame... it does not even
/// look fun"; the core dungeon loop defers to WoW Mythic+, design/BIBLE.md section 9). A teaching pull is fought as a
/// Mythic+ trash pack, not as a handful of bodies: each pull's room holds a required escort pack standing on the pull's
/// own aggro ring (same centre, same radius), so walking into the ring engages the pull and its escort on one tick, as
/// one pack. The escort adds bodies and never a second lesson: the Choir keeps exactly one kickable caster (BIBLE 9.3:
/// "One caster casting every 9-12 s can be kicked every time, which is the Choir's lesson"). Sizes follow WoW Mythic+
/// trash: the Choir is a six-body pack around one priority caster (its pull plus the Cantor's Guard), and the Pair keeps
/// its five marked warriors (a standard five-mob pack). The Seed stays its one elite (BIBLE 9.6, "Teaches the boss's core
/// rule with no stakes"): a guard for it does not fit the forces rule (the walking line must stay under the bar).
/// </summary>
public sealed class PullPackTests
{
    /// <summary>A group's aggro radius when content sets none, mm (BIBLE 9.1; Keyrun.Sim Pulls.DefaultAggroMm).</summary>
    private const int DefaultAggroMm = 4000;

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    /// <summary>One teaching pull fought as a pack: its own enemies plus every escort standing on its ring.</summary>
    private sealed record PullPack(string PullId, List<string> Members, List<LayoutPack> Escorts);

    /// <summary>
    /// The pull's aggro ring as the sim places it: the integer mean (truncated toward zero) of its spawn points, each at the
    /// room origin plus the content offset, one point per counted body; the room's aggroMm, else the default.
    /// </summary>
    private static (int X, int Z, int Radius) Ring(LayoutBox room, PullDef pull)
    {
        var points = pull.Enemies.SelectMany(e => Enumerable.Repeat((X: room.Origin!.X + e.Spawn.X, Z: room.Origin.Z + e.Spawn.Z), e.Count)).ToList();
        long x = points.Sum(p => (long)p.X), z = points.Sum(p => (long)p.Z);
        return ((int)(x / points.Count), (int)(z / points.Count), room.AggroMm ?? DefaultAggroMm);
    }

    private static PullPack PackOf(string pullId)
    {
        LayoutDef layout = Registry.Layout;
        PullDef pull = Registry.Dungeons[layout.Dungeon].Pulls.Single(p => p.Id == pullId);
        LayoutBox room = layout.Boxes.Single(b => b.Pull == pullId);
        var ring = Ring(room, pull);
        var escorts = layout.Packs
            .Where(p => p.Room == room.Id && p.Center.X == ring.X && p.Center.Z == ring.Z && p.AggroMm == ring.Radius)
            .ToList();
        var members = pull.Enemies.Concat(escorts.SelectMany(p => p.Enemies))
            .SelectMany(e => Enumerable.Repeat(e.EnemyId, e.Count))
            .ToList();
        return new PullPack(pullId, members, escorts);
    }

    /// <summary>True when <paramref name="enemyId"/>'s script casts an interruptible ability (a kick target).</summary>
    private static bool Kickable(string enemyId) =>
        Registry.Enemies[enemyId].Script.Any(s => Registry.Abilities[s.Cast].Interruptible);

    private static string Describe(PullPack p) =>
        $"{p.PullId}: {p.Members.Count} bodies ({string.Join(", ", p.Members.GroupBy(m => m).Select(g => g.Count() + "x " + g.Key))}); escorts [{string.Join(", ", p.Escorts.Select(e => e.Id))}]";

    [Fact(DisplayName = "Pull packs: the Choir is six bodies around one kickable Cantor, so the kick ledger has a pack to save")]
    public void The_choir_is_a_six_body_pack_with_one_kickable_caster()
    {
        var choir = PackOf("choir");
        Assert.True(choir.Members.Count >= 6, Describe(choir));
        Assert.Equal(1, choir.Members.Count(Kickable));
        Assert.Equal("choir.lieutenant", Assert.Single(choir.Members, Kickable));
        Assert.All(choir.Escorts, e => Assert.False(e.Optional, $"escort {e.Id} is optional: an escort is part of the pull"));
    }

    [Fact(DisplayName = "Pull packs: the Seed keeps exactly one elite and no kickable caster, so its lesson stays the Window")]
    public void The_seed_keeps_one_elite_and_no_caster()
    {
        var seed = PackOf("seed");
        Assert.Equal(1, seed.Members.Count(m => Registry.Enemies[m].Role == EnemyRole.Elite));
        Assert.DoesNotContain(seed.Members, Kickable);
    }

    [Fact(DisplayName = "Pull packs: the Pair keeps its five marked warriors as a five-body pack")]
    public void The_pair_is_a_five_body_pack()
    {
        var pair = PackOf("pair");
        Assert.True(pair.Members.Count >= 5, Describe(pair));
        Assert.DoesNotContain(pair.Members, Kickable);
    }

    [Fact(DisplayName = "Pull packs: every escort banks forces like any pack")]
    public void Every_escort_is_worth_forces()
    {
        foreach (string id in new[] { "choir", "pair", "seed" })
        {
            foreach (LayoutPack escort in PackOf(id).Escorts)
            {
                Assert.True(RouteForces.LayoutPackPoints(Registry.Forces, escort) > 0, $"escort {escort.Id} banks no forces");
            }
        }
        Assert.NotEmpty(PackOf("choir").Escorts);
    }
}
