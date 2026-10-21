using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// A player's heal never lands on an enemy. A policy run at seed 1 key 2 showed the Chanter healing the Choir Acolyte
/// (actor 100) for 90-450 hp and the Pair actors 104/105: a heal on a hostile is wrong behaviour the player sees.
/// Every heal event over seeds 1-5, with an empty plan and with a kick plan, must come from a non-player or land on a
/// player.
/// </summary>
public sealed class PlayerHealNeverHostileTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    public static IEnumerable<object[]> Cases()
    {
        foreach (var seed in new long[] { 1, 2, 3, 4, 5 })
        {
            yield return new object[] { seed, "{}" };
            yield return new object[] { seed, "{\"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}" };
        }
    }

    [Theory(DisplayName = "Policy play: no heal event has a player source and an enemy target (seeds 1-5)")]
    [MemberData(nameof(Cases))]
    public void Player_heals_never_land_on_enemies(long seed, string plan)
    {
        var r = PlaytestRunner.Play(new PlaytestOptions(2, seed, PolicyReader.Parse(plan, Registry)) { MaxTicks = 6000 }, Registry);
        var kinds = r.Run.State.Actors.ToDictionary(kv => kv.Key, kv => kv.Value.Kind);
        var bad = new List<string>();
        var heals = 0;
        foreach (var line in r.Log.Split('\n'))
        {
            if (!line.StartsWith("E ", System.StringComparison.Ordinal) || !line.Contains("\"heal\"", System.StringComparison.Ordinal))
            {
                continue;
            }
            using var doc = JsonDocument.Parse(line[2..]);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "heal")
            {
                continue;
            }
            heals++;
            var source = root.GetProperty("sourceId").GetInt32();
            var target = root.GetProperty("targetId").GetInt32();
            if (kinds.TryGetValue(source, out var sk) && sk == ActorKind.Player &&
                kinds.TryGetValue(target, out var tk) && tk == ActorKind.Enemy)
            {
                bad.Add(line);
            }
        }
        Assert.True(heals > 0, "the run emitted no heal event at all (the check would be vacuous)");
        Assert.True(bad.Count == 0, $"{bad.Count} player heal(s) landed on an enemy, first: {bad.FirstOrDefault()}");
    }
}
