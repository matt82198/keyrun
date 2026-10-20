using System;
using System.Linq;
using System.Text.Json;
using Keyrun.Content;
using Keyrun.Sim.Encounter;
using Xunit;
using SimTimer = Keyrun.Sim.Encounter.Timer;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The playtest reports read the run clock (STATE.md D46, Mythic+): a death adds 15 s and a full wipe 25 s more to the
/// clock, and nothing drains a Slack bank any more. summary.json and the recap line report the deaths, the time they
/// added and the clock against par, as the HUD does ("Deaths 3 (+45s)", "0:57 / par 12:00"); no report carries the
/// Slack bank, which no longer moves.
/// </summary>
public sealed class RunClockReportTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private static readonly PlaytestOptions Options = new(2, 1, null) { MaxTicks = 2000 };

    private static readonly Lazy<PlaytestResult> Played = new(() => PlaytestRunner.Play(Options, Registry));

    /// <summary>The played run with its encounter reset to no deaths, then charged <paramref name="deaths"/> times by the sim.</summary>
    private static PlaytestResult WithDeaths(int deaths)
    {
        var played = Played.Value;
        var encounter = played.Run.Encounter with { Deaths = 0, PenaltyTicks = 0 };
        for (var i = 0; i < deaths; i++)
        {
            encounter = SimTimer.ChargeDeath(encounter);
        }
        return played with { Run = played.Run with { Encounter = encounter } };
    }

    private static JsonElement SummaryOf(PlaytestResult r) =>
        JsonDocument.Parse(Summary.ToJson(r, Options, new SummaryHeader("p", null, "log.txt", 2, 2, 1))).RootElement;

    [Fact(DisplayName = "Run clock report: a run with 3 deaths shows 3 deaths and 900 added ticks in summary.json")]
    public void Summary_reports_deaths_and_added_time()
    {
        var r = WithDeaths(3);
        Assert.Equal(900, r.Run.Encounter.PenaltyTicks);
        var s = SummaryOf(r);
        Assert.Equal(3, s.GetProperty("deathCount").GetInt32());
        Assert.Equal(900, s.GetProperty("addedTicks").GetInt32());
        Assert.Equal(45, s.GetProperty("addedSeconds").GetInt32());
    }

    [Fact(DisplayName = "Run clock report: summary.json puts the run clock (elapsed plus added time) against par")]
    public void Summary_reports_clock_against_par()
    {
        var r = WithDeaths(3);
        var s = SummaryOf(r);
        var elapsed = s.GetProperty("clearTicks").GetInt32();
        var clock = elapsed + 900;
        Assert.Equal(clock, s.GetProperty("clockTicks").GetInt32());
        Assert.Equal(clock / Summary.TicksPerSecond, s.GetProperty("clockSeconds").GetInt32());
        Assert.Equal(SimTimer.ClockTicks(r.Run.Encounter, elapsed), s.GetProperty("clockTicks").GetInt64());
        var par = s.GetProperty("parTicks").GetInt32();
        Assert.Equal((int)((long)clock * 100 / par), s.GetProperty("clockParPercent").GetInt32());
        Assert.Equal(clock > par, s.GetProperty("overPar").GetBoolean());
    }

    [Fact(DisplayName = "Run clock report: summary.json carries no Slack figure, with or without deaths")]
    public void Summary_has_no_slack_figure()
    {
        foreach (var deaths in new[] { 0, 3 })
        {
            var s = SummaryOf(WithDeaths(deaths));
            var names = s.EnumerateObject().Select(p => p.Name).ToList();
            Assert.DoesNotContain(names, n => n.Contains("slack", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact(DisplayName = "Run clock report: the recap result line shows the clock, the deaths and the added time, never Slack")]
    public void Recap_line_reports_the_run_clock()
    {
        var r = WithDeaths(3);
        var line = PlaytesterMemory.Observe(r)[0];
        var elapsed = r.Run.State.RunTimerTicks / Summary.TicksPerSecond;
        var clock = (r.Run.State.RunTimerTicks + 900) / Summary.TicksPerSecond;
        Assert.Contains(FormattableString.Invariant($"clock {clock}s of par {r.Run.Encounter.ParTicks / Summary.TicksPerSecond}s"), line, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"run {elapsed}s + 45s added"), line, StringComparison.Ordinal);
        Assert.Contains("deaths 3", line, StringComparison.Ordinal);
        Assert.DoesNotContain("slack", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Run clock report: a cleared run inside par on elapsed time but past par on the run clock is untimed")]
    public void Outcome_is_judged_on_the_run_clock()
    {
        var played = Played.Value;
        var par = played.Run.Encounter.ParTicks;
        var state = played.Run.State.Clone();
        state.RunTimerTicks = par - 100;
        var cleared = played.Run with
        {
            State = state,
            Encounter = SimTimer.ChargeDeath(played.Run.Encounter with { Deaths = 0, PenaltyTicks = 0, PullIndex = played.Run.Dungeon.Pulls.Count, Depleted = false }),
            RunEnd = played.Run.RunEnd with { Result = RunResult.Depleted, ResultTick = state.Tick },
        };
        var outcome = Summary.OutcomeOf(played with { Run = cleared });
        Assert.False(outcome.Timed);
        Assert.Equal("over par", outcome.Why);

        var noDeath = cleared with { Encounter = cleared.Encounter with { Deaths = 0, PenaltyTicks = 0 } };
        Assert.True(Summary.OutcomeOf(played with { Run = noDeath }).Timed);
    }
}
