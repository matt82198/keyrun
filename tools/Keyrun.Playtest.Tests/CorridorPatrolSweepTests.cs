using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Content;
using Keyrun.Playtest;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-PATROL-4 (f-a887de3c; BIBLE s0 test 1 "hard because of what enemies do", s3 "every dangerous enemy action is a cast
/// with a visible bar and a floor telegraph"; dungeon loop defers to WoW Mythic+). The corridor patrol's signature is the
/// Patrol Sweep: a 2 second frontal cone cast, telegraphed on the floor for the whole cast, interruptible, first cast within
/// 100 ticks of the patrol engaging. Standing in the cone is the mistake; a kick or a step out is the answer. The
/// patrol's members are read from the run's own content (R-PINS-1), never an id range.
/// </summary>
public sealed class CorridorPatrolSweepTests
{
    private const string PatrolPackId = "corridor-patrol";
    private const string Sweep = "choir.patrolSweep";

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private static (PlaytestResult Result, HashSet<long> Members, List<JsonElement> Events) Play(long seed)
    {
        var result = PlaytestRunner.Play(new PlaytestOptions(2, seed, null), Registry);
        var members = result.Setup.Content.Patrols!.Single(p => p.Id == PatrolPackId).Spawns.Select(s => (long)s.ActorId).ToHashSet();
        Assert.True(members.Count >= 2, $"the {PatrolPackId} patrol must have members; content gave {members.Count}");
        var events = result.Log.Split('\n')
            .Where(line => line.StartsWith("E ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line.AsSpan(2).ToString()).RootElement)
            .ToList();
        return (result, members, events);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : -1;

    private static int Tick(JsonElement e) => e.GetProperty("tick").GetInt32();

    [Fact(DisplayName = "Corridor patrol sweep (R-PATROL-4, seed 1 key 2): the first Patrol Sweep is drawn as a cone spanning its cast within 100 ticks of the patrol engaging")]
    public void First_sweep_is_a_cone_telegraph_within_100_ticks_of_engage()
    {
        var (_, members, events) = Play(1);
        var engage = events.Where(e => Str(e, "type") == "pack_engage" && Str(e, "packId") == PatrolPackId).Select(Tick).ToList();
        Assert.NotEmpty(engage);
        var sweeps = events.Where(e => Str(e, "type") == "telegraph" && Str(e, "cast") == Sweep).ToList();
        Assert.True(sweeps.Count > 0, "the corridor patrol never drew a Patrol Sweep telegraph");
        var first = sweeps.OrderBy(Tick).First();
        Assert.Equal("cone", Str(first, "shape"));
        // The log's telegraph event carries no spansCast flag; a spansCast telegraph is drawn at the cast's start and resolves
        // when the cast completes, so its lead equals the 40-tick (2 second) cast time.
        Assert.Equal(40, Num(first, "leadTicks"));
        Assert.Equal(Tick(first) + 40, Num(first, "resolvesTick"));
        Assert.Contains(Num(first, "actorId"), members);
        Assert.InRange(Tick(first) - engage.Min(), 0, 100);
    }

    [Theory(DisplayName = "Corridor patrol sweep (R-PATROL-4, key 2): the bots answer the sweep (a kick or a dodge at least once) and the party never wipes on the patrol")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Bots_answer_the_sweep_and_the_party_survives_the_patrol(long seed)
    {
        var (_, members, events) = Play(seed);
        var starts = events.Where(e => Str(e, "type") == "telegraph" && Str(e, "cast") == Sweep).ToList();
        Assert.True(starts.Count > 0, "the corridor patrol never swept");

        var kicked = events.Count(e => Str(e, "type") == "interrupt" && Str(e, "abilityId") == Sweep);
        // A sweep that completed and hit nobody was stepped out of; a completion that damaged a seat was stood in.
        var completes = events.Where(e => Str(e, "type") == "cast_complete" && Str(e, "abilityId") == Sweep).ToList();
        var dodged = completes.Count(c => !events.Any(d => Str(d, "type") == "damage" && Tick(d) == Tick(c) && Num(d, "sourceId") == Num(c, "actorId") && Num(d, "targetId") <= 3));
        Assert.True(kicked + dodged >= 1, $"seed {seed}: {starts.Count} sweeps, {completes.Count} completed, none kicked or dodged");

        var patrolDeaths = events.Where(e => Str(e, "type") == "death" && members.Contains(Num(e, "actorId"))).Select(Tick).ToList();
        Assert.Equal(members.Count, patrolDeaths.Count);
        var lastPatrolDeath = patrolDeaths.Max();
        var seatDeaths = events.Where(e => Str(e, "type") == "death" && Num(e, "actorId") is >= 1 and <= 3 && Tick(e) <= lastPatrolDeath)
            .Select(e => Num(e, "actorId")).Distinct().Count();
        Assert.True(seatDeaths < 3, $"seed {seed}: the party wiped on the patrol (tick {lastPatrolDeath})");
    }
}
