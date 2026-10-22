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
/// party that kicks it clears the Choir and times the key. Both plans are the same competent party (STATE.md D48: the
/// tank mitigates as the scripted tank does); they differ only in the kick rule, so the lesson is the kick.
/// </summary>
public sealed class ChoirLessonTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    // STATE.md D48: the lesson compares a competent party that kicks against the SAME competent party that never kicks.
    // Competent includes the tank mitigating the way the scripted party's tank does (Brace and Bulwark under pressure);
    // both plans carry it, so the only difference between the two cases is the kick rule.
    private const string Competent = "\"mitigate\": [\"warden\"]";

    private const string KickPlan = "{" + Competent + ", \"kicks\": [{\"cast\": \"choir.healCast\", \"kickers\": [\"cinder\", \"chanter\", \"warden\"]}]}";

    private const string NeverKickPlan = "{" + Competent + "}";

    private static PlaytestResult Play(string? policyJson) =>
        PlaytestRunner.Play(new PlaytestOptions(2, 1, policyJson is null ? null : PolicyReader.Parse(policyJson, Registry)), Registry);

    private static int ChoirClearTick(PlaytestResult r) =>
        r.Stats.Pulls.Single(p => p.Id == "choir").ClearTick ?? int.MaxValue;

    [Fact(DisplayName = "Choir lesson: an unkicked Choral Mending heals every living Choir member at once")]
    public void Unkicked_mending_heals_the_pack()
    {
        var r = Play(NeverKickPlan);
        // The Choir pull's own Cantor: PATH-PACKS packs also field choir.lieutenant members, idle in the state from tick 0.
        var cantor = r.Setup.Dungeon.Pulls.Single(p => p.Id == "choir").Spawns.Single(s => s.ClassId == "choir.lieutenant").ActorId;
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

    [Fact(DisplayName = "Choir lesson: the never-kick plan's Choir drags past twice the kicking party's, and its key is not timed")]
    public void Unkicked_choir_drags_and_fails_the_key()
    {
        var neverKick = Play(NeverKickPlan);
        var kicking = Play(KickPlan);

        Assert.True(ChoirClearTick(neverKick) > 2 * ChoirClearTick(kicking),
            $"never-kick plan cleared the Choir at {ChoirClearTick(neverKick)}, the kicking plan at {ChoirClearTick(kicking)}");
        Assert.NotEqual(RunResult.Timed, neverKick.Run.RunEnd.Result);
        // FINISH-NEEDS-FORCES: on main's layout no bot party times the key (the count stays short), so the line above no
        // longer separates the plans; the lesson is that the never-kick party does not even kill the boss inside par.
        var bossClear = neverKick.Stats.Pulls.SingleOrDefault(p => p.Id == "boss")?.ClearTick;
        Assert.True(bossClear is null || Timer.ClockTicks(neverKick.Run.Encounter, bossClear.Value) > neverKick.Run.Encounter.ParTicks,
            $"the never-kick party killed the boss at clock {Timer.ClockTicks(neverKick.Run.Encounter, bossClear ?? 0)}, inside par {neverKick.Run.Encounter.ParTicks}");
    }

    [Fact(DisplayName = "Choir lesson: a party that kicks Choral Mending clears the Choir and kills the boss inside par at key 2 (a kick plan and the scripted party)")]
    public void Kicking_party_times_the_key()
    {
        foreach (var r in new[] { Play(KickPlan), Play(null) })
        {
            Assert.NotEqual(int.MaxValue, ChoirClearTick(r));
            Assert.True(r.Stats.Interrupted.TryGetValue("choir.healCast", out var kicked) && kicked > 0, "the party never stopped a Mending");
            // FINISH-NEEDS-FORCES (f-8c50a787, WoW Mythic+): the key completes only at 100% forces with the boss dead. The
            // kicking party kills the boss inside par, but on main's layout it skips every optional pack, so the count
            // stays short and the key is not timed. The layout lane (feat/dungeon-branches) flips this back to Timed.
            var bossClear = r.Stats.Pulls.Single(p => p.Id == "boss").ClearTick;
            Assert.True(bossClear is not null, "the party never killed the boss");
            Assert.True(Timer.ClockTicks(r.Run.Encounter, bossClear!.Value) <= r.Run.Encounter.ParTicks,
                $"the boss died at clock {Timer.ClockTicks(r.Run.Encounter, bossClear.Value)}, past par {r.Run.Encounter.ParTicks}");
            var goal = Assert.IsType<ForcesGoal>(r.Run.Dungeon.ForcesGoal);
            Assert.False(goal.Met(r.Run.State), $"forces reached {goal.Read(r.Run.State).PercentBp} bp yet the key was not timed");
            Assert.NotEqual(RunResult.Timed, r.Run.RunEnd.Result);
        }
    }
}
