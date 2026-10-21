using System.Linq;
using System.Text.RegularExpressions;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// PATH-PACKS Hall Sentries play as Mythic+ trash (design/BIBLE.md s9.1; Matt 2026-10-20 "lean into m+ design heavily"):
/// the required pack on the walking line between the Pair and the Seed is a speed bump a competent party walks through,
/// not a second Choir. Played through <see cref="PlaytestRunner"/> on the real Hollow Vault at key 2, seed 1, as
/// <see cref="ChoirLessonTests"/> plays it: a party that kicks Choral Mending (the kick plan and the scripted party) kills
/// every Hall Sentry. The never-kick plan's failure is pinned by <see cref="ChoirLessonTests"/>.
/// </summary>
public sealed class HallSentriesTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    // The same competent kick plan ChoirLessonTests plays (STATE.md D48: the tank mitigates as the scripted tank does).
    private const string KickPlan = "{\"mitigate\": [\"warden\"], \"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";

    private static PlaytestResult Play(string? policyJson) =>
        PlaytestRunner.Play(new PlaytestOptions(2, 1, policyJson is null ? null : PolicyReader.Parse(policyJson, Registry)), Registry);

    [Fact(DisplayName = "Hall Sentries: a party that kicks Choral Mending kills every Hall Sentry (a kick plan and the scripted party)")]
    public void Kicking_party_kills_every_hall_sentry()
    {
        foreach (var r in new[] { Play(KickPlan), Play(null) })
        {
            var members = r.Setup.Content.Packs!.Single(p => p.Id == "hall-sentries").Spawns.Select(s => s.ActorId).ToList();
            Assert.Equal(5, members.Count);
            // RunStats.Deaths records player deaths only, so an enemy's death is read from the run's own death events.
            var dead = r.Log.Split('\n')
                .Where(l => l.Contains("\"type\":\"death\""))
                .Select(l => int.Parse(Regex.Match(l, "\"actorId\":(\\d+)").Groups[1].Value))
                .ToHashSet();
            var alive = members.Where(id => !dead.Contains(id)).ToList();
            Assert.True(alive.Count == 0,
                $"{alive.Count} of {members.Count} Hall Sentries never died (alive: {string.Join(", ", alive)}); run ended {r.Run.RunEnd.Result}");
        }
    }

    [Fact(DisplayName = "Hall Sentries: each sentry's swing timer runs on its own, so the five first swings land on five distinct ticks")]
    public void Sentry_first_swings_land_on_distinct_ticks()
    {
        var r = Play(KickPlan);
        var members = r.Setup.Content.Packs!.Single(p => p.Id == "hall-sentries").Spawns.Select(s => s.ActorId).ToList();
        var lines = r.Log.Split('\n');
        var first = members.ToDictionary(id => id, id => lines
            .Where(l => l.Contains("\"type\":\"cast_complete\"") && l.Contains($"\"actorId\":{id},") && l.Contains("\"abilityId\":\"choir.meleeSwing\""))
            .Select(l => int.Parse(Regex.Match(l, "\"tick\":(\\d+)").Groups[1].Value))
            .DefaultIfEmpty(-1)
            .Min());
        var timeline = string.Join(", ", first.Select(kv => $"{kv.Key}@{kv.Value}"));
        Assert.True(first.Values.All(t => t >= 0), $"a Hall Sentry never swung: {timeline}");
        Assert.True(first.Values.Distinct().Count() == members.Count, $"Hall Sentry first swings share a tick: {timeline}");
    }
}
