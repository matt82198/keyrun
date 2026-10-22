using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Content;
using Keyrun.Playtest;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-PATROL-1 (ruling on PR #528; defer to WoW Mythic+: a patrol is a timing read between pulls, never a hidden add on the
/// first pull). At seed 1, key 2 the corridor-2 patrol's members (the spawns of the "corridor-patrol" patrol pack) must not engage, damage anyone, or die before the Choir (pull 1) has cleared.
/// </summary>
public sealed class PatrolTimingTests
{
    /// <summary>R-PINS-1: the patrol's members come from the run's own content, not an id range (519 and up went stale, and
    /// silently vacuous, whenever a pack was added or removed ahead of the patrol).</summary>
    private const string PatrolPackId = "corridor-patrol";

    [Fact(DisplayName = "Patrol timing (R-PATROL-1, seed 1 key 2): no patrol member engages, hits or dies before pull 1 (the Choir) clears")]
    public void No_patrol_member_acts_before_the_choir_clears()
    {
        var registry = ContentLoader.Load();
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null), registry);
        var members = result.Setup.Content.Patrols!.Single(p => p.Id == PatrolPackId).Spawns.Select(s => (long)s.ActorId).ToHashSet();
        Assert.True(members.Count >= 2, $"the {PatrolPackId} patrol must have members to watch; content gave {members.Count}");
        var events = result.Log.Split('\n')
            .Where(line => line.StartsWith("E ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line.AsSpan(2).ToString()).RootElement)
            .ToList();

        var choirClear = events.Where(e => Str(e, "type") == "pull_clear" && Str(e, "pullId") == "choir")
            .Select(e => (long)e.GetProperty("tick").GetInt32())
            .ToList();
        Assert.Single(choirClear);
        long clearTick = choirClear[0];

        var early = events
            .Where(e => (long)e.GetProperty("tick").GetInt32() < clearTick)
            .Where(e => Str(e, "type") is "pack_engage" or "damage" or "death" or "cast_complete" or "heal" or "telegraph" or "pull_start")
            .Where(e => Ids(e).Any(members.Contains))
            .Select(e => e.GetRawText())
            .ToList();
        Assert.True(early.Count == 0, $"patrol members acted before the Choir cleared at tick {clearTick}: {string.Join(" | ", early.Take(3))}");
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Every actor id an event names: actorId, sourceId, targetId and the actorIds list.</summary>
    private static IEnumerable<long> Ids(JsonElement e)
    {
        foreach (var name in new[] { "actorId", "sourceId", "targetId" })
        {
            if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
            {
                yield return v.GetInt64();
            }
        }
        if (e.TryGetProperty("actorIds", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in list.EnumerateArray())
            {
                yield return v.GetInt64();
            }
        }
    }
}
