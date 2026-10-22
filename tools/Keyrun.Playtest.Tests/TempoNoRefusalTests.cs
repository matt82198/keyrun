using System.Collections.Generic;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// TEMPO-LOOP-CLAMP (f-57c04b4a; BIBLE s0 test 1): in shipped content, built exactly as the host builds it
/// (<see cref="HostRunSetup.Build"/>), no enemy cast is ever refused not_ready, so Tempo never makes a key easier by
/// losing casts. The run reports a refusal as <c>cast_refused</c> (reason not_ready); before the fix the Warden's
/// Cleave, pushed a few ticks late by its own Slam or Lock bar, came due again inside its cooldown and was dropped, and
/// the Seed's slam was dropped the same way at key 10. The default playtest run still times, and its boss fight stays
/// inside R-BOSS-1 (90 to 180 s at key 2, 1800 to 3600 ticks).
/// </summary>
public sealed class TempoNoRefusalTests
{
    private readonly ITestOutputHelper _out;

    public TempoNoRefusalTests(ITestOutputHelper output) => _out = output;

    private static string Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : "";

    private static List<string> NotReady(IEnumerable<SimEvent> events) =>
        events.Where(e => e.Type == "cast_refused" && Str(e, "reason") == "not_ready").Select(e => e.ToCanonicalJson()).ToList();

    public static IEnumerable<object[]> Pulls() =>
        from key in new[] { 1, 2, 10 } from pull in new[] { 0, 1, 2, 3 } select new object[] { key, pull };

    [Theory(DisplayName = "Tempo loop clamp: each host-built pull, alone on an idle unkillable party for 3000 ticks, refuses no enemy cast not_ready")]
    [MemberData(nameof(Pulls))]
    public void A_host_built_pull_refuses_no_cast(int key, int pullIndex)
    {
        var setup = HostRunSetup.Build(ContentLoader.Load(), "hollow-vault", key, "warden", withBots: false);
        var pull = setup.Dungeon.Pulls[pullIndex];
        var party = setup.Party.Select(p => p with { Pos = pull.Spawns[0].Pos, MaxHealth = 100_000_000 }).ToList();
        var run = RunRoot.CreateRun(1, party, setup.Dungeon with { Pulls = new[] { pull } }, setup.Content with { Layout = null }, null);
        var events = new List<SimEvent>();
        while (run.State.Tick < 3000)
        {
            var stepped = RunRoot.StepRun(run, new List<Command>());
            run = stepped.Run;
            events.AddRange(stepped.Events);
        }
        var casts = events.Count(e => e.Type == "cast_complete" && e.Get("actorId") is JsInt a && a.Value >= 100);
        Assert.True(casts >= 10, $"key {key} pull {pull.Id}: only {casts} enemy casts landed, the test is vacuous");
        var refused = NotReady(events);
        Assert.True(refused.Count == 0, $"key {key} pull {pull.Id}: {refused.Count} enemy casts refused not_ready: {string.Join(" | ", refused.Take(5))}");
    }

    [Fact(DisplayName = "Tempo loop clamp: the default key-2 playtest run times, refuses no enemy cast not_ready, and its boss fight stays inside R-BOSS-1")]
    public void The_default_run_times_and_refuses_nothing()
    {
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null));
        var events = result.Log.Split('\n').Where(l => l.StartsWith("E ", System.StringComparison.Ordinal))
            .Select(l => System.Text.Json.JsonDocument.Parse(l.Substring(2)).RootElement).ToList();
        int TickOf(System.Text.Json.JsonElement e) => e.GetProperty("tick").GetInt32();
        string TypeOf(System.Text.Json.JsonElement e) => e.GetProperty("type").GetString() ?? "";
        var bossStart = events.Single(e => TypeOf(e) == "pull_start" && e.GetProperty("pullId").GetString() == "boss");
        var end = events.Single(e => TypeOf(e) == "run_end");
        var fight = TickOf(end) - TickOf(bossStart);
        var refused = events.Where(e => TypeOf(e) == "cast_refused" && e.GetProperty("reason").GetString() == "not_ready").ToList();
        _out.WriteLine($"boss pull_start {TickOf(bossStart)} run_end {TickOf(end)} ({end.GetProperty("result").GetString()}) fight {fight} ticks, not_ready refusals {refused.Count}");
        Assert.Equal("timed", end.GetProperty("result").GetString());
        Assert.Empty(refused);
        Assert.InRange(fight, 1800, 3600);
    }
}
