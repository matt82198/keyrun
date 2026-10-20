using System.Linq;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// PATH-PACKS Hall Sentries play as Mythic+ trash (design/BIBLE.md s9.1; Matt 2026-10-20 "lean into m+ design heavily"):
/// the required pack on the walking line between the Pair and the Seed is a speed bump a competent party walks through,
/// not a second Choir. Played through <see cref="PlaytestRunner"/> on the real Hollow Vault at key 2, seed 1, as
/// <see cref="ChoirLessonTests"/> plays it: a party that kicks Choral Mending (the kick plan and the scripted party) kills
/// every Hall Sentry. The empty plan's failure is pinned by <see cref="ChoirLessonTests"/>, unchanged.
/// </summary>
public sealed class HallSentriesTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private const string KickPlan = "{\"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";

    private static PlaytestResult Play(string? policyJson) =>
        PlaytestRunner.Play(new PlaytestOptions(2, 1, policyJson is null ? null : PolicyReader.Parse(policyJson, Registry)), Registry);

    [Fact(DisplayName = "Hall Sentries: a party that kicks Choral Mending kills every Hall Sentry (a kick plan and the scripted party)")]
    public void Kicking_party_kills_every_hall_sentry()
    {
        foreach (var r in new[] { Play(KickPlan), Play(null) })
        {
            var members = r.Setup.Content.Packs!.Single(p => p.Id == "hall-sentries").Spawns.Select(s => s.ActorId).ToList();
            Assert.Equal(5, members.Count);
            var dead = r.Stats.Deaths.Select(d => d.ActorId).ToHashSet();
            var alive = members.Where(id => !dead.Contains(id)).ToList();
            Assert.True(alive.Count == 0,
                $"{alive.Count} of {members.Count} Hall Sentries never died (alive: {string.Join(", ", alive)}); run ended {r.Run.RunEnd.Result}");
        }
    }
}
