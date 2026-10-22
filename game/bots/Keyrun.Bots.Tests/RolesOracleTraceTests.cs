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
/// ENGINE-PLAN 3B exit gate, as properties: the three bot oracle traces (game/sim/ORACLE.md section 6) are the INPUTS
/// (setup file and seed) of a run whose every command the C# bots GENERATE. Mirrors tests/oracle/traces.ts botRun: every
/// party actor is <c>makeBot()</c> with the real roles (<see cref="RoleSet.Default"/>), <c>defaultMemory(100)</c>, the
/// bot PRNG is <c>seedPrng(runSeed)</c>, and each tick's view is
/// <c>buildBotView(state, encounter.telegraphs.map(toBotTelegraph), EMPTY_KIT)</c> over <c>loadContent()</c>'s registry
/// (run-setup.ts setContentRegistry), with no pings, until the run ends (or tick 24000). The run must show every named
/// property manifest.json states for that trace (ORACLE.md section 10): the kick lands on the casting lieutenant, the
/// taunt never roots the tank, every pull clears and the key times, and the rest. No whole-trace hash and no tick-for-tick
/// comparison with the TS recording is asserted, so a bot change that keeps every property needs no re-recorded fixture.
/// How far the C# run still matches the TS recording is printed, not asserted.
/// </summary>
public sealed class RolesOracleTraceTests
{
    /// <summary>tests/oracle/traces.ts botRun's stop rule: the run end, or this many ticks.</summary>
    private const int MaxTicks = 24000;

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

    /// <summary>The outcome of one bot-driven run.</summary>
    /// <param name="Run">The C# run, every tick recorded.</param>
    /// <param name="CommandsGenerated">Bot commands generated.</param>
    /// <param name="TsMatchedThrough">Records from t=0 whose projection hash, commands and events equal the TS recording's (reported only).</param>
    /// <param name="TsRecords">Records in the TS recording.</param>
    public sealed record BotReplay(RunRecording Run, int CommandsGenerated, int TsMatchedThrough, int TsRecords);

    /// <summary>Drives the oracle <paramref name="name"/>'s setup and seed with the C# bots over <paramref name="roles"/>.</summary>
    /// <param name="name">Trace name (e.g. bots-hv-k2-s7).</param>
    /// <param name="roles">Role set the bots dispatch through.</param>
    /// <returns>The run.</returns>
    public static BotReplay Drive(string name, RoleSet roles)
    {
        var dir = OracleDir();
        var trace = OracleTrace.Load(Path.Combine(dir, name + ".trace.jsonl"));
        // R-BOTS-9: the trace's setup is the frozen TS run's, which has no verse rule (RunSetup.WithTsKit).
        var setup = RunSetup.Load(Path.Combine(dir, trace.SetupFile), trace.Seed).WithTsKit();
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options);
        var kits = Kits.Value;
        var bots = new SortedDictionary<int, IBot>();
        var initial = new SortedDictionary<int, BotMemory>();
        foreach (var p in setup.Party)
        {
            bots[p.Id] = Bot.MakeBot(roles);
            initial[p.Id] = BotMemory.Default(100);
        }
        IReadOnlyDictionary<int, BotMemory> memories = initial;
        var prng = Skill.SeedPrng(trace.Seed);
        var generated = 0;
        var ticks = new List<RecordedTick>();

        void Add(IReadOnlyList<string> commands, IReadOnlyList<string> events)
        {
            var line = Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks);
            ticks.Add(new RecordedTick(run.State.Tick, Fnv1a.Hex(Projection.RecordHash(line)), line, commands, events));
        }

        Add(Array.Empty<string>(), Array.Empty<string>());
        while (run.RunEnd.Result is null && run.State.Tick < MaxTicks)
        {
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits);
            var batch = Bot.RunBots(view, bots, memories, prng);
            memories = batch.Memories;
            prng = batch.Prng;
            generated += batch.Commands.Count;
            var stepped = RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            Add(batch.Commands.Select(CommandJson.ToCanonicalJson).ToList(), stepped.Events.Select(e => e.ToCanonicalJson()).ToList());
        }

        var recording = new RunRecording(name, ticks, RunRecording.Render(run.RunEnd.Result?.ToJs(), run.RunEnd.ResultTick));
        var ts = RunRecording.FromTrace(trace).Ticks;
        var matched = 0;
        while (matched < Math.Min(ts.Count, ticks.Count) && ts[matched].H == ticks[matched].H
            && ts[matched].Commands.SequenceEqual(ticks[matched].Commands, StringComparer.Ordinal)
            && ts[matched].Events.SequenceEqual(ticks[matched].Events, StringComparer.Ordinal))
        {
            matched++;
        }
        return new BotReplay(recording, generated, matched, ts.Count);
    }

    private BotReplay AssertProperties(string name)
    {
        var properties = OracleProperties.Of(OracleDir(), name);
        Assert.True(properties.Count > 0, $"{name}: manifest.json states no property; a trace that protects nothing is vacuous");
        var r = Drive(name, RoleSet.Default);
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name}: records {r.Run.Ticks.Count} commands {r.CommandsGenerated} result {r.Run.Result} properties {properties.Count} (reported, not asserted: matches the TS recording through {r.TsMatchedThrough}/{r.TsRecords} records)"));
        Assert.True(r.CommandsGenerated > 0, "a bot run that generates no command is vacuous");        var failures = OracleProperties.CheckAll(properties, r.Run, other => Drive(other, RoleSet.Default).Run);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        return r;
    }

    [Fact(DisplayName = "bot oracle > bots-hv-k2-s7: the C#-bot run shows every property the oracle protects")]
    public void Key2Seed7() => AssertProperties("bots-hv-k2-s7");

    [Fact(DisplayName = "bot oracle > bots-hv-k2-s11: the C#-bot run shows every property the oracle protects")]
    public void Key2Seed11() => AssertProperties("bots-hv-k2-s11");

    [Fact(DisplayName = "bot oracle > bots-hv-k10-s7: the C#-bot run shows every property the oracle protects")]
    public void Key10Seed7() => AssertProperties("bots-hv-k10-s7");

    [Fact(DisplayName = "bot oracle > anti-vacuity: with no roles the bot run breaks the oracle's properties")]
    public void NoRolesBreakTheProperties()
    {
        var r = Drive("bots-hv-k2-s7", RoleSet.None);
        var failures = OracleProperties.CheckAll(OracleProperties.Of(OracleDir(), "bots-hv-k2-s7"), r.Run, other => Drive(other, RoleSet.None).Run);
        _output.WriteLine(string.Join("\n", failures));
        Assert.Contains(failures, f => f.Contains("party-clears-every-pull-in-order-and-times", StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Contains("warden-kicks-the-casting-lieutenant", StringComparison.Ordinal));
    }
}
