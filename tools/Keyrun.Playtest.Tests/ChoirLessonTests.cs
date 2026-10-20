using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The Choir teaches the kick (design/BIBLE.md 9.6 item 1 and 9.3: Choral Mending "Heals its pack. Left uncovered, the
/// pack outheals the timer"). Under PRESPAWN-AGGRO an empty plan stopped losing the Choir: one landed Mending healed one
/// Choir member for 300 of its 6000 health every ~20 s, so leaving it unkicked cost almost nothing and the party timed
/// the first key. These cases play the real Hollow Vault through <see cref="PlaytestRunner"/> at key 2, seed 1, and
/// pin both sides of the lesson: an unkicked Mending heals the whole pack and drags the Choir past the clock, and a
/// party that kicks it clears the Choir and times the key.
/// </summary>
public sealed class ChoirLessonTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private const string KickPlan = "{\"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";

    private static PlaytestResult Play(string? policyJson) =>
        PlaytestRunner.Play(new PlaytestOptions(2, 1, policyJson is null ? null : PolicyReader.Parse(policyJson, Registry)), Registry);

    private static int ChoirClearTick(PlaytestResult r) =>
        r.Stats.Pulls.Single(p => p.Id == "choir").ClearTick ?? int.MaxValue;

    [Fact(DisplayName = "Choir lesson: an unkicked Choral Mending heals every living Choir member at once")]
    public void Unkicked_mending_heals_the_pack()
    {
        var r = Play("{}");
        var cantor = r.Run.State.Actors.Values.Single(a => a.ClassId == "choir.lieutenant").Id;
        var firstHealTick = r.Log.Split('\n')
            .Where(l => l.Contains("\"type\":\"heal\"") && l.Contains($"\"sourceId\":{cantor},"))
            .Select(l => int.Parse(System.Text.RegularExpressions.Regex.Match(l, "\"tick\":(\\d+)").Groups[1].Value))
            .First();
        var targets = r.Log.Split('\n')
            .Where(l => l.Contains("\"type\":\"heal\"") && l.Contains($"\"sourceId\":{cantor},") && l.Contains($"\"tick\":{firstHealTick},"))
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, "\"targetId\":(\\d+)").Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.Equal(3, targets.Count);
    }

    [Fact(DisplayName = "Choir lesson: the empty plan's Choir drags past twice the kicking party's, and its key is not timed")]
    public void Unkicked_choir_drags_and_fails_the_key()
    {
        var empty = Play("{}");
        var kicking = Play(KickPlan);

        Assert.True(ChoirClearTick(empty) > 2 * ChoirClearTick(kicking),
            $"empty plan cleared the Choir at {ChoirClearTick(empty)}, the kicking plan at {ChoirClearTick(kicking)}");
        Assert.NotEqual(RunResult.Timed, empty.Run.RunEnd.Result);
    }

    [Fact(DisplayName = "Choir lesson: a party that kicks Choral Mending clears the Choir and times key 2 (a kick plan and the scripted party)")]
    public void Kicking_party_times_the_key()
    {
        foreach (var r in new[] { Play(KickPlan), Play(null) })
        {
            Assert.NotEqual(int.MaxValue, ChoirClearTick(r));
            Assert.True(r.Stats.Interrupted.TryGetValue("choir.healCast", out var kicked) && kicked > 0, "the party never stopped a Mending");
            Assert.Equal(RunResult.Timed, r.Run.RunEnd.Result);
        }
    }
}
