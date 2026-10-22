using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Playtest;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-PACKDENSITY-1 property (b), as a red-first property test (the bounds are never edited; a red run is the expected outcome
/// until the pack placement chains). Over both dungeons (hollow-vault, sunken-bellfoundry) and seeds 1..8 at key 2: for each
/// trash pack A that the autorun engages and clears, the engage event of A's nearest neighbour B is emitted before the log
/// position of A's last member death. A pack's engage event is pack_engage (keyed by its full placement id) or, for a
/// dungeon pull placed as a layout pack (the hollow-vault teaching pulls), pull_start (keyed by the pull id).
/// Definitions used here (the ruling fixes the property, not these): the neighbour is the R-PACKDENSITY-2 one (copied from
/// PackDensityPropertyTests on PR #617: the other trash pack whose nearest member centre is nearest to A's centre; a member
/// centre is the placement centre plus the group spawn offset). The neighbour is taken at any distance: a neighbour beyond
/// 1.5x A's radius is a chaining failure too, because property (a) has already failed for it. "A's last death" is the log
/// position of the last "death" event among the actor ids A's engage event names; a pack whose members do not all die in
/// the run is unmeasured, not passed. The harness is the autorun only: PlaytestRunner.Play drives the scripted party along
/// the walking line, and a pack engages only if the route reaches it. The playtest observer is read-only, so the party is not
/// steered into A's aggro radius; off-route trash packs are unmeasured. The measured set is every trash pack the autorun
/// engages and clears, and the measurement lines report how many were measured per seed.
/// The seeded layout is the one the run uses: hollow-vault through SeededLayout.Build (the seeded registry), sunken-bellfoundry
/// through the host's own ArmSegmented call (PlaytestOptions.DungeonId with the seed).
/// </summary>
public sealed class PackDensityBodyPullTests
{
    private const string HollowVault = "hollow-vault";
    private const string SunkenBellfoundry = "sunken-bellfoundry";

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the per-seed measurements are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public PackDensityBodyPullTests(ITestOutputHelper output) => _output = output;

    /// <summary>Both dungeons crossed with seeds 1..8.</summary>
    public static IEnumerable<object[]> Cases()
    {
        foreach (var dungeon in new[] { HollowVault, SunkenBellfoundry })
        {
            for (long seed = 1; seed <= 8; seed++)
            {
                yield return new object[] { dungeon, seed };
            }
        }
    }

    /// <summary>
    /// The seeded registry, the playtest options and the layout a run of this dungeon and seed walks. Both are pure in the seed,
    /// so the layout the measurement reads is the one the run plays.
    /// </summary>
    private static (ContentRegistry Registry, PlaytestOptions Options, LayoutDef Layout) Arm(string dungeon, long seed)
    {
        var reg = ContentLoader.Load();
        if (dungeon == HollowVault)
        {
            var seeded = SeededLayout.Build(reg, seed).Registry;
            return (seeded, new PlaytestOptions(2, seed, null), seeded.Layout!);
        }

        var armed = SeededLayout.ArmSegmented(reg, dungeon, seed);
        return (reg, new PlaytestOptions(2, seed, null) { DungeonId = dungeon }, armed.Layout!);
    }

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

    /// <summary>The "E" lines of a playtest log, parsed in log order.</summary>
    private static List<JsonElement> EventsOf(string log) =>
        log.Split('\n')
            .Where(line => line.StartsWith("E ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line.AsSpan(2).ToString()).RootElement.Clone())
            .ToList();

    private static string? TypeOf(JsonElement e) =>
        e.TryGetProperty("type", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The placement id an engage event names: pullId for a pull_start, packId for a pack_engage.</summary>
    private static string? EngagedIdOf(JsonElement e)
    {
        var key = TypeOf(e) == "pull_start" ? "pullId" : "packId";
        return e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>The content pull id of a placement: the prefix before '@' ("pair@r3s0" is pull "pair").</summary>
    private static string PullIdOf(LayoutPack pack) => pack.Id.Split('@')[0];

    /// <summary>
    /// The engage event of a layout placement. A pack the sim engages as a pack (pack_engage) is keyed by its full placement id;
    /// a placement that is a dungeon pull (a teaching pull, pull_start) is keyed by its content pull id, the prefix.
    /// </summary>
    private static bool TryEngage(Dictionary<string, (int Index, int[] Actors)> engages, LayoutPack pack, out (int Index, int[] Actors) engage) =>
        engages.TryGetValue(pack.Id, out engage) || engages.TryGetValue(PullIdOf(pack), out engage);

    [Theory(DisplayName = "Pack density body pull (R-PACKDENSITY-1 b): a pack the autorun engages and clears chains each near neighbour, pulled before its last death")]
    [MemberData(nameof(Cases))]
    public void A_body_pulled_pack_chains_its_near_neighbour_before_its_last_death(string dungeon, long seed)
    {
        var (registry, options, layout) = Arm(dungeon, seed);
        var result = PlaytestRunner.Play(options, registry);
        var events = EventsOf(result.Log);

        // The first engage event per placement id (log position and the actors it names), and the first death per actor.
        var firstEngage = new Dictionary<string, (int Index, int[] Actors)>(StringComparer.Ordinal);
        var firstDeath = new Dictionary<int, int>();
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var type = TypeOf(e);
            if (type is "pull_start" or "pack_engage")
            {
                var id = EngagedIdOf(e);
                if (id is not null && e.TryGetProperty("actorIds", out var ids))
                {
                    firstEngage.TryAdd(id, (i, ids.EnumerateArray().Select(v => v.GetInt32()).ToArray()));
                }
            }
            else if (type == "death" && e.TryGetProperty("actorId", out var actor))
            {
                firstDeath.TryAdd(actor.GetInt32(), i);
            }
        }

        var violations = new List<string>();
        var measured = 0;
        var unmeasured = 0;
        var chained = 0;
        var notChained = 0;
        string? firstFailure = null;

        foreach (var subject in layout.Packs)
        {
            if (!TryEngage(firstEngage, subject, out var engage))
            {
                continue;
            }

            var deaths = engage.Actors.Select(id => firstDeath.TryGetValue(id, out var d) ? d : -1).ToArray();
            if (deaths.Length == 0 || deaths.Any(d => d < 0))
            {
                unmeasured++;
                continue;
            }

            measured++;
            var lastDeath = deaths.Max();
            double radius = subject.AggroMm;

            // R-PACKDENSITY-2 neighbour: the other trash pack whose nearest member is nearest to the subject's centre.
            var nearest = layout.Packs
                .Where(other => other.Id != subject.Id)
                .OrderBy(other => NearestMemberDistance(subject.Center, other))
                .FirstOrDefault();
            if (nearest is null)
            {
                continue;
            }

            var distance = NearestMemberDistance(subject.Center, nearest);
            var multiple = distance / radius;
            var pulled = TryEngage(firstEngage, nearest, out var neighbourEngage);
            if (pulled && neighbourEngage.Index < lastDeath)
            {
                chained++;
                continue;
            }

            notChained++;
            var reason = pulled
                ? $"engaged at log {neighbourEngage.Index}, after the subject's last death at log {lastDeath}"
                : "never engaged in the run";
            firstFailure ??= $"{subject.Id} (R {radius} mm) -> {nearest.Id} at {distance:F0} mm ({multiple:F2}x R)";
            violations.Add($"{subject.Id} (R {radius} mm): nearest neighbour {nearest.Id} at {distance:F0} mm ({multiple:F2}x R) {reason}");
        }

        _output.WriteLine(
            $"{dungeon} seed {seed}: engaged-and-cleared packs {measured}, unmeasured (not engaged or not all dead) {unmeasured}, " +
            $"subject-neighbour pairs chained {chained}, not chained {notChained}, first failure: {firstFailure ?? "none"}, result {result.Run.RunEnd.Result}");

        Assert.True(measured > 0, $"{dungeon} seed {seed}: the autorun engaged and cleared no trash pack, so nothing was measured");
        Assert.True(
            violations.Count == 0,
            $"{dungeon} seed {seed}: {violations.Count} R-PACKDENSITY-1 (b) violation(s), no body pull chained the nearest neighbour before the subject's last death:\n" +
            string.Join("\n", violations));
    }
}
