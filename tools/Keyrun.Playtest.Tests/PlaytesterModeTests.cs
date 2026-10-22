using System;
using System.Collections.Generic;
using System.Text.Json;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// G2.4 K3a (design/PLAYTESTER-GUIDE.md section 1, "The rule, mechanical"): the playtester mode is read from the plan
/// (a missing field is guided), every run Summary carries it, every K2 receipt carries it, and a balance line from a
/// blind row is refused. An unknown mode is a plan error.
/// </summary>
public sealed class PlaytesterModeTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    [Fact(DisplayName = "Mode: a plan with no mode field is guided (the mode that reproduces today's behaviour)")]
    public void A_plan_without_a_mode_is_guided()
    {
        Assert.Equal(PlaytesterMode.Guided, PolicyReader.Parse("{}", Registry).Mode);
    }

    [Fact(DisplayName = "Mode: \"guided\" and \"blind\" are read from the plan")]
    public void Guided_and_blind_are_read_from_the_plan()
    {
        Assert.Equal(PlaytesterMode.Guided, PolicyReader.Parse("{\"mode\":\"guided\"}", Registry).Mode);
        Assert.Equal(PlaytesterMode.Blind, PolicyReader.Parse("{\"mode\":\"blind\"}", Registry).Mode);
    }

    [Theory(DisplayName = "Mode: an unknown or mistyped mode is a plan error, refused")]
    [InlineData("\"Blind\"")]
    [InlineData("\"expert\"")]
    [InlineData("\"\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void An_unknown_mode_is_refused(string value)
    {
        var error = Assert.Throws<PolicyException>(() => PolicyReader.Parse("{\"mode\":" + value + "}", Registry));
        Assert.Contains("mode", error.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Mode: the wire names are exactly \"guided\" and \"blind\"")]
    public void Wire_names_are_exact()
    {
        Assert.Equal("guided", PlaytesterMode.Guided.Wire());
        Assert.Equal("blind", PlaytesterMode.Blind.Wire());
        Assert.True(PlaytesterModeWire.TryParse("blind", out var parsed));
        Assert.Equal(PlaytesterMode.Blind, parsed);
        Assert.False(PlaytesterModeWire.TryParse("Blind", out _));
    }

    [Theory(DisplayName = "Summary: carries the run's mode")]
    [InlineData("{}", "guided")]
    [InlineData("{\"mode\":\"guided\"}", "guided")]
    [InlineData("{\"mode\":\"blind\"}", "blind")]
    public void The_summary_carries_the_mode(string plan, string expected)
    {
        var policy = PolicyReader.Parse(plan, Registry);
        var options = new PlaytestOptions(2, 1, policy) { MaxTicks = 300 };
        var result = PlaytestRunner.Play(options, Registry);
        var json = Summary.ToJson(result, options, new SummaryHeader(null, null, "run.log", 2, 2, null));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, doc.RootElement.GetProperty("mode").GetString());
    }

    [Fact(DisplayName = "Receipts: every K2 receipt of a blind run carries mode blind")]
    public void Every_receipt_of_a_blind_run_is_blind()
    {
        var sink = new CollectingSink();
        var policy = PolicyReader.Parse("{\"mode\":\"blind\"}", Registry);
        PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = 600, Receipts = sink }, Registry);
        Assert.True(sink.Receipts.Count > 20, $"vacuous run: only {sink.Receipts.Count} receipts");
        Assert.All(sink.Receipts, r => Assert.Equal("blind", r.Mode));
    }

    [Fact(DisplayName = "Receipts: a run with no mode field records guided receipts")]
    public void Receipts_of_a_plan_without_a_mode_are_guided()
    {
        var sink = new CollectingSink();
        var policy = PolicyReader.Parse("{}", Registry);
        PlaytestRunner.Play(new PlaytestOptions(2, 1, policy) { MaxTicks = 600, Receipts = sink }, Registry);
        Assert.True(sink.Receipts.Count > 20, $"vacuous run: only {sink.Receipts.Count} receipts");
        Assert.All(sink.Receipts, r => Assert.Equal("guided", r.Mode));
    }

    [Fact(DisplayName = "Balance: a blind row is refused, naming the rule and the guide")]
    public void A_blind_row_cannot_print_a_balance_line()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BalanceGuard.RequireGuided(PlaytesterMode.Blind, "par margin"));
        Assert.Contains("PLAYTESTER-GUIDE.md", error.Message, StringComparison.Ordinal);
        Assert.Contains("section 1", error.Message, StringComparison.Ordinal);
        Assert.Contains("blind", error.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Balance: a guided row may print a balance line")]
    public void A_guided_row_may_print_a_balance_line()
    {
        BalanceGuard.RequireGuided(PlaytesterMode.Guided, "par margin");
    }

    private sealed class CollectingSink : IBotReceiptSink
    {
        public List<BotReceipt> Receipts { get; } = new();

        public void Record(BotReceipt receipt) => Receipts.Add(receipt);
    }
}
