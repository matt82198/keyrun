using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-PACKDENSITY-1 (orchestrator ruling, event store): trash packs are placed so that a chain pull is a choice, never
/// free and never forced. Property (a), over the seeded layouts (seeds 1..8) of both dungeons: every trash pack has at
/// least one neighbouring pack whose nearest member stands within 1.5x the pack's own aggro radius, and no neighbouring
/// pack's member stands within 0.8x that radius unless both packs share a pull id.
/// Definitions used here (the ruling does not fix them, so they are stated, not tuned): the layout's trash packs are the
/// neighbour set (walked and side branch alike; a pack in another room is simply far away); the distance is from the
/// subject pack's centre to the nearest member centre (centre + group spawn offset) of the other pack; a pull id is the
/// content id that prefixes the pack's placement id ("pair@r5s0" is pull "pair"). The radius is the pack's
/// <see cref="LayoutPack.AggroMm"/> as the content ships it; nothing here is a guessed constant.
/// The bounds are the ruling's and are never edited to make a layout pass (a fitted green is forbidden).
/// </summary>
public sealed class PackDensityPropertyTests
{
    private const string HollowVault = "hollow-vault";
    private const string SunkenBellfoundry = "sunken-bellfoundry";

    /// <summary>R-PACKDENSITY-1: the nearest neighbour must stand within this multiple of the subject's aggro radius.</summary>
    private const double FarBound = 1.5;

    /// <summary>R-PACKDENSITY-1: no neighbour's member may stand within this multiple of the subject's aggro radius.</summary>
    private const double NearBound = 0.8;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (measured distances are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public PackDensityPropertyTests(ITestOutputHelper output) => _output = output;

    /// <summary>Both dungeons crossed with seeds 1..8.</summary>
    public static IEnumerable<object[]> Layouts()
    {
        foreach (var dungeon in new[] { HollowVault, SunkenBellfoundry })
        {
            for (long seed = 1; seed <= 8; seed++)
            {
                yield return new object[] { dungeon, seed };
            }
        }
    }

    /// <summary>The seeded layout the run walks for a dungeon and seed (the same builders the bots use).</summary>
    private static LayoutDef SeededLayoutFor(ContentRegistry reg, string dungeon, long seed) =>
        dungeon == HollowVault
            ? SeededLayout.Build(reg, seed).Registry.Layout
            : SeededLayout.ArmSegmented(reg, dungeon, seed).Layout;

    /// <summary>The content pull id of a pack: the prefix of its placement id ("pair@r5s0" is pull "pair").</summary>
    private static string PullIdOf(LayoutPack pack) => pack.Id.Split('@')[0];

    /// <summary>Distance, mm, from <paramref name="from"/> to the nearest member centre of <paramref name="pack"/>.</summary>
    private static double NearestMemberDistance(LayoutPoint from, LayoutPack pack)
    {
        double best = double.MaxValue;
        foreach (var group in pack.Enemies)
        {
            double dx = (pack.Center.X + group.Spawn.X) - from.X;
            double dz = (pack.Center.Z + group.Spawn.Z) - from.Z;
            best = Math.Min(best, Math.Sqrt((dx * dx) + (dz * dz)));
        }
        return best;
    }

    [Theory(DisplayName = "Pack density (R-PACKDENSITY-1): every trash pack has a neighbour within 1.5x its aggro radius and none inside 0.8x unless they share a pull")]
    [MemberData(nameof(Layouts))]
    public void Every_trash_pack_has_a_near_neighbour_and_none_inside_the_near_bound(string dungeon, long seed)
    {
        ContentRegistry reg = ContentLoader.Load();
        LayoutDef layout = SeededLayoutFor(reg, dungeon, seed);
        var violations = new List<string>();

        foreach (LayoutPack subject in layout.Packs)
        {
            double radius = subject.AggroMm;
            var neighbours = layout.Packs
                .Where(other => other.Id != subject.Id)
                .Select(other => (Pack: other, Distance: NearestMemberDistance(subject.Center, other)))
                .OrderBy(n => n.Distance)
                .ToList();

            var nearest = neighbours.Count > 0 ? neighbours[0] : default;
            _output.WriteLine(
                $"{dungeon} seed {seed} {subject.Id} R={radius}mm nearest={(neighbours.Count > 0 ? $"{nearest.Pack.Id} at {nearest.Distance:F0}mm ({nearest.Distance / radius:F2}x R)" : "none")}");

            if (neighbours.Count == 0 || nearest.Distance > FarBound * radius)
            {
                violations.Add(
                    $"{subject.Id} (R {radius} mm) has no neighbour within {FarBound}x R; nearest is " +
                    $"{(neighbours.Count > 0 ? $"{nearest.Pack.Id} at {nearest.Distance:F0} mm ({nearest.Distance / radius:F2}x R)" : "none")}");
            }

            foreach (var n in neighbours.Where(n => n.Distance < NearBound * radius && PullIdOf(n.Pack) != PullIdOf(subject)))
            {
                violations.Add(
                    $"{subject.Id} (R {radius} mm): neighbour {n.Pack.Id} member at {n.Distance:F0} mm ({n.Distance / radius:F2}x R), inside {NearBound}x R, different pull");
            }
        }

        Assert.True(violations.Count == 0, $"{dungeon} seed {seed}: {violations.Count} R-PACKDENSITY-1 violation(s):\n" + string.Join("\n", violations));
    }
}
