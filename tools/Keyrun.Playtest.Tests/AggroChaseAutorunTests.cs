using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Content;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Encounter;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// AGGRO-CHASE on the shipped run (finding f-f0c886eb; Matt 2026-10-22, capture kf-main-0e748ca1: "the first couple packs
/// just stand still?"). The scripted autorun at key 2, seed 1 (the capture's run): the Choir and the Pair each engage
/// while the party is still well outside melee (their members aggro on their own radius, run.json enemyAggro), every
/// melee member then runs at the party along its path before it swings, and the bots still clear the key within par.
/// </summary>
public sealed class AggroChaseAutorunTests
{
    /// <summary>The party is at least this far from every member of the pack on its engage tick, mm (melee reach is 2000).</summary>
    private const long EngageGapMm = 6000;

    /// <summary>Every melee member of the pack walks at least this far between its engage and its first hit, mm.</summary>
    private const long RunInMm = 3000;

    private readonly ITestOutputHelper _out;

    public AggroChaseAutorunTests(ITestOutputHelper output) => _out = output;

    private sealed record Trace(PlaytestResult Result, Dictionary<int, Dictionary<int, Vec3>> At, List<JsonElement> Events);

    private static readonly Lazy<Trace> Autorun = new(() =>
    {
        var at = new Dictionary<int, Dictionary<int, Vec3>>();
        var result = PlaytestRunner.Play(new PlaytestOptions(2, 1, null)
        {
            Observe = (tick, run, _) => at[tick] = run.State.Actors.Values.Where(a => a.Alive).ToDictionary(a => a.Id, a => a.Pos),
        }, ContentLoader.Load());
        var events = result.Log.Split('\n')
            .Where(line => line.StartsWith("E ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line.AsSpan(2).ToString()).RootElement)
            .ToList();
        return new Trace(result, at, events);
    });

    private static long Dist(Vec3 a, Vec3 b) => Movement.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Tick(JsonElement e) => e.GetProperty("tick").GetInt32();

    [Fact(DisplayName = "Aggro chase autorun (k2 seed 1): the bots still clear the key, timed within par")]
    public void The_bots_still_clear_the_key_within_par()
    {
        var t = Autorun.Value;
        var starts = t.Events.Where(e => Str(e, "type") is "pull_start" or "pack_engage" or "pack_evade" or "pack_reset")
            .Select(e => $"{Str(e, "type")} {Str(e, "pullId") ?? Str(e, "packId") ?? Str(e, "groupId")} @{Tick(e)}");
        _out.WriteLine(string.Join("\n", starts));
        _out.WriteLine($"result {t.Result.Run.RunEnd.Result?.ToJs()} at tick {t.Result.Run.RunEnd.ResultTick} (par {t.Result.Setup.Content.ParTicks})");
        Assert.Equal("timed", t.Result.Run.RunEnd.Result?.ToJs());
        Assert.True(t.Result.Run.RunEnd.ResultTick <= t.Result.Setup.Content.ParTicks, "the clear ran past par");
    }

    [Theory(DisplayName = "Aggro chase autorun (k2 seed 1): the pack engages with the party out of melee, and every melee member runs in before it swings")]
    [InlineData("choir")]
    [InlineData("pair")]
    public void The_pack_engages_from_range_and_runs_in(string pullId)
    {
        var t = Autorun.Value;
        var start = t.Events.Single(e => Str(e, "type") == "pull_start" && Str(e, "pullId") == pullId);
        var tick = Tick(start);
        var members = start.GetProperty("actorIds").EnumerateArray().Select(v => v.GetInt32()).ToList();
        var party = t.Result.Setup.Party.Select(p => p.Id).ToList();
        // The positions the engage tick's step read (Observe runs before each step), so tick - 1 is the state it engaged on.
        var engagedOn = t.At[tick - 1];
        var gap = members.Min(m => party.Where(engagedOn.ContainsKey).Min(p => Dist(engagedOn[m], engagedOn[p])));
        _out.WriteLine($"{pullId}: engage tick {tick}, nearest party member {gap} mm from the nearest member");
        Assert.True(gap >= EngageGapMm, $"{pullId} engaged with the party {gap} mm away: walked into, not aggroed from range");

        var melee = members.Where(m => t.Result.Run.State.Actors[m].ClassId.EndsWith(".melee", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(melee);
        foreach (var m in melee)
        {
            // Until its first hit, or its death if it never lands one.
            var end = t.Events.FirstOrDefault(e =>
                (Str(e, "type") == "damage" && e.TryGetProperty("sourceId", out var s) && s.GetInt32() == m) ||
                (Str(e, "type") == "death" && e.TryGetProperty("actorId", out var d) && d.GetInt32() == m));
            Assert.True(end.ValueKind != JsonValueKind.Undefined, $"{pullId} member {m} neither hit nor died");
            var home = engagedOn[m];
            var ran = Enumerable.Range(tick, Tick(end) - tick).Where(k => t.At.ContainsKey(k) && t.At[k].ContainsKey(m)).Max(k => Dist(home, t.At[k][m]));
            _out.WriteLine($"{pullId}: member {m} ran {ran} mm from {home} before its {Str(end, "type")} at tick {Tick(end)}");
            Assert.True(ran >= RunInMm, $"{pullId} member {m} moved only {ran} mm before it fought: it stood still");
        }
    }
}
