using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G1.1 / G2.4 (R-EVENTSTORE-2, plan PER-PROJECT-EVENT-STORES.md sections 3.1 and 3.2, lane K2): a receipt sink attached to
/// the bot command source writes one JSONL line per issued bot command, in tick order, and the file is append-only. The
/// harness is the K1 autorun (seed 1, key 2, hollow-vault, ScriptedWarden as the human seat).
/// </summary>
public sealed class BotReceiptSinkTests : IDisposable
{
    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const int Ticks = 3000;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    private readonly string _dir = Directory.CreateTempSubdirectory("keyrun-bot-receipts-").FullName;

    /// <summary>Removes the scratch directory.</summary>
    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Runs the autorun to <see cref="Ticks"/>, writing receipts to <paramref name="receiptPath"/> when non-null.</summary>
    /// <returns>The BOTS proof line and the number of bot commands issued.</returns>
    private static (string ProofLine, long Count) Drive(string? receiptPath)
    {
        var registry = Registry.Value;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        using var sink = receiptPath is null ? null : new JsonlBotReceiptSink(receiptPath);
        if (sink is not null)
        {
            bots.Receipts = sink;
        }
        while (run.State.Tick < Ticks && run.RunEnd.Result is null)
        {
            var commands = new List<Command>();
            warden.Sample(run.State.Tick, run, commands, new List<int>());
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            run = RunRoot.StepRun(run, commands).Run;
        }
        return (bots.ProofLine(), bots.CommandCount);
    }

    /// <summary>One line per issued bot command, in tick order, each with the fixed receipt shape; the sink changes no behaviour.</summary>
    [Fact]
    public void EachIssuedBotCommandWritesOneJsonlLineInTickOrder()
    {
        var path = Path.Combine(_dir, "bot-receipts.jsonl");
        var (proofWithSink, count) = Drive(path);
        var (proofWithout, countWithout) = Drive(null);

        Assert.True(count > 50, $"vacuous run: only {count} commands");
        Assert.Equal(countWithout, count);
        Assert.Equal(proofWithout, proofWithSink);

        var lines = File.ReadAllLines(path);
        Assert.Equal(count, lines.Length);
        long previousTick = int.MinValue;
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            Assert.Equal(JsonValueKind.Number, root.GetProperty("tick").ValueKind);
            Assert.Equal(JsonValueKind.Number, root.GetProperty("seat").ValueKind);
            Assert.Equal(JsonValueKind.String, root.GetProperty("class").ValueKind);
            Assert.Equal(JsonValueKind.String, root.GetProperty("ability").ValueKind);
            var target = root.GetProperty("target").ValueKind;
            Assert.True(target is JsonValueKind.Number or JsonValueKind.Null, "target is an int or null");
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("reason").GetString()), "every bot command carries its reason phrase");
            var tick = root.GetProperty("tick").GetInt64();
            Assert.True(tick >= previousTick, $"tick {tick} is before {previousTick}");
            previousTick = tick;
        }
    }

    /// <summary>The sink only appends: lines already in the file survive, and new receipts follow them.</summary>
    [Fact]
    public void OpeningAnExistingFileAppendsAndNeverTruncates()
    {
        var path = Path.Combine(_dir, "bot-receipts.jsonl");
        File.WriteAllText(path, "{\"sentinel\":true}\n");

        var (_, count) = Drive(path);

        var lines = File.ReadAllLines(path);
        Assert.Equal("{\"sentinel\":true}", lines[0]);
        Assert.Equal(count + 1, lines.Length);
    }
}
