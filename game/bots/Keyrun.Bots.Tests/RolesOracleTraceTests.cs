using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// ENGINE-PLAN 3B exit gate: the three bot oracle traces (game/sim/ORACLE.md section 6) replayed with the C# bots
/// GENERATING every command, not replaying the recorded ones. Mirrors tests/oracle/traces.ts botRun: every party
/// actor is <c>makeBot()</c> with the real roles (<see cref="RoleSet.Default"/>), <c>defaultMemory(100)</c>, the bot
/// PRNG is <c>seedPrng(runSeed)</c>, and each tick's view is
/// <c>buildBotView(state, encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT)</c> over <c>loadContent()</c>'s registry
/// (run-setup.ts setContentRegistry), with no pings. Per tick the bot commands must equal the record's <c>c</c>
/// (canonical JSON, in order), then the stepped projection hash, full line and events must match; finally the trace
/// hash and the run result.
/// </summary>
public sealed class RolesOracleTraceTests
{
    private readonly ITestOutputHelper _output;

    public RolesOracleTraceTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<KitRegistry> Kits = new(() => KitRegistry.From(ContentLoader.Load()));

    /// <summary>The oracle fixture directory (game/sim/Keyrun.Sim.Tests/Fixtures/oracle).</summary>
    public static string OracleDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Keyrun.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new DirectoryNotFoundException("could not locate game/Keyrun.sln above " + AppContext.BaseDirectory);
        }
        return Path.Combine(dir.FullName, "sim", "Keyrun.Sim.Tests", "Fixtures", "oracle");
    }

    /// <summary>The outcome of one bot-driven replay.</summary>
    /// <param name="Checked">Records that matched (commands, hash, line, events), from t=0.</param>
    /// <param name="Total">Records in the trace.</param>
    /// <param name="CommandsGenerated">Bot commands generated and matched.</param>
    /// <param name="TraceHash">The C# trace hash over the matched lines.</param>
    /// <param name="Result">The C# run end, "result@tick".</param>
    /// <param name="Failure">The first divergence, or null.</param>
    public sealed record BotReplay(int Checked, int Total, int CommandsGenerated, string TraceHash, string Result, string? Failure);

    /// <summary>Drives <paramref name="name"/> with the C# bots over <paramref name="roles"/>.</summary>
    /// <param name="name">Trace name (e.g. bots-hv-k2-s7).</param>
    /// <param name="roles">Role set the bots dispatch through.</param>
    /// <returns>The replay outcome.</returns>
    public static BotReplay Drive(string name, RoleSet roles)
    {
        var dir = OracleDir();
        var trace = OracleTrace.Load(Path.Combine(dir, name + ".trace.jsonl"));
        var setup = RunSetup.Load(Path.Combine(dir, trace.SetupFile), trace.Seed);
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options);
        var kits = Kits.Value;
        var bots = new SortedDictionary<int, IBot>();
        IReadOnlyDictionary<int, BotMemory> memories = new SortedDictionary<int, BotMemory>();
        var initial = new SortedDictionary<int, BotMemory>();
        foreach (var p in setup.Party)
        {
            bots[p.Id] = Bot.MakeBot(roles);
            initial[p.Id] = BotMemory.Default(100);
        }
        memories = initial;
        var prng = Skill.SeedPrng(trace.Seed);
        var lines = new List<string>();
        var generated = 0;
        string? failure = null;

        foreach (var record in trace.Records)
        {
            var t = record.T.ToString(CultureInfo.InvariantCulture);
            IReadOnlyList<string> events = Array.Empty<string>();
            if (record.T > 0)
            {
                var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits);
                var batch = Bot.RunBots(view, bots, memories, prng);
                memories = batch.Memories;
                prng = batch.Prng;
                var want = record.Commands.Select(CommandJson.ToCanonicalJson).ToList();
                var got = batch.Commands.Select(CommandJson.ToCanonicalJson).ToList();
                if (!want.SequenceEqual(got, StringComparer.Ordinal))
                {
                    failure = $"t={t} commands\n  expected: [{string.Join(",", want)}]\n  actual:   [{string.Join(",", got)}]";
                    break;
                }
                generated += got.Count;
                var stepped = RunRoot.StepRun(run, batch.Commands);
                run = stepped.Run;
                events = stepped.Events.Select(e => e.ToCanonicalJson()).ToList();
            }
            var line = Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks);
            if (Fnv1a.Hex(Projection.RecordHash(line)) != record.H || (record.P is not null && record.P != line))
            {
                failure = $"t={t} projection\n  expected: {record.P ?? record.H}\n  actual:   {line}";
                break;
            }
            if (!record.Events.SequenceEqual(events, StringComparer.Ordinal))
            {
                failure = $"t={t} events\n  expected: {string.Join(" ", record.Events)}\n  actual:   {string.Join(" ", events)}";
                break;
            }
            lines.Add(line);
        }

        var traceHash = Fnv1a.Hex(Projection.TraceHash(lines));
        var resultJs = run.RunEnd.Result?.ToJs();
        var result = resultJs is null ? "none" : string.Create(CultureInfo.InvariantCulture, $"{resultJs}@{run.RunEnd.ResultTick}");
        var expectedResult = trace.Result is null ? "none" : string.Create(CultureInfo.InvariantCulture, $"{trace.Result}@{trace.ResultTick}");
        if (failure is null && traceHash != trace.TraceHash)
        {
            failure = $"traceHash {traceHash} (expected {trace.TraceHash})";
        }
        if (failure is null && result != expectedResult)
        {
            failure = $"result {result} (expected {expectedResult})";
        }
        return new BotReplay(lines.Count, trace.Records.Count, generated, traceHash, result, failure);
    }

    private void AssertDrives(string name, string expectedHash)
    {
        var r = Drive(name, RoleSet.Default);
        _output.WriteLine($"{name}: ticks {r.Checked}/{r.Total} commands {r.CommandsGenerated} traceHash {r.TraceHash} (expected {expectedHash}) result {r.Result}");
        Assert.True(r.Failure is null, $"{name}: diverged after {r.Checked}/{r.Total} records: {r.Failure}");
        Assert.Equal(r.Total, r.Checked);
        Assert.Equal(expectedHash, r.TraceHash);
        Assert.True(r.CommandsGenerated > 0, "a bot replay that generates no command is vacuous");
    }

    [Fact(DisplayName = "bot oracle > bots-hv-k2-s7 replays every tick from C#-generated bot commands")]
    public void Key2Seed7() => AssertDrives("bots-hv-k2-s7", "c3d7a583"); // was 4df1e3f1; re-recorded: healer no longer swaps verse to self-heal. DEAD-CASTER-TELEGRAPH (was 902b84a7), KF3-SEED-ENGAGE (was 71eb4d2e)

    [Fact(DisplayName = "bot oracle > bots-hv-k2-s11 replays every tick from C#-generated bot commands")]
    public void Key2Seed11() => AssertDrives("bots-hv-k2-s11", "c3d7a583"); // was 4df1e3f1; re-recorded: healer no longer swaps verse to self-heal. DEAD-CASTER-TELEGRAPH (was 902b84a7), KF3-SEED-ENGAGE (was 71eb4d2e)

    [Fact(DisplayName = "bot oracle > bots-hv-k10-s7 replays every tick from C#-generated bot commands")]
    public void Key10Seed7() => AssertDrives("bots-hv-k10-s7", "3b33f4ad"); // DEAD-CASTER-TELEGRAPH (was 8c7e9098), CAST-RANGE-RECHECK (the t1760 seed.lash on actor 1 at 2093 mm now misses; was 867dee64), KF3-SEED-ENGAGE (was e1286ac0; SLAM-TELEGRAPH B1 was 27a3ceaf); equals the TS manifest

    [Fact(DisplayName = "bot oracle > anti-vacuity: with no roles the bots diverge from the trace")]
    public void NoRolesDiverge()
    {
        var r = Drive("bots-hv-k2-s7", RoleSet.None);
        Assert.NotNull(r.Failure);
        Assert.True(r.Checked < r.Total);
    }
}
