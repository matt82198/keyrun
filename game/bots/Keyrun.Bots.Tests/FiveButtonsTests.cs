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
/// BOTS-FIVE-BUTTONS (design/BIBLE.md section 0 test 1, "Classes with real decisions on five buttons"; Matt
/// 2026-10-20: "it should feel almost exact to wow with less button clutter"). Over the seeded Hollow Vault key-2 bot
/// runs (the bots-hv-k2-s7 and bots-hv-k2-s11 scenarios: setup file and seed read from each trace header), every
/// party actor is a real-role bot exactly as <see cref="RolesOracleTraceTests.Drive"/> builds them, and the run steps
/// until it ends. Each bot must complete a cast of every one of its class's five rotational abilities (content
/// ClassDef rot1..rot5) at least once, counted from the sim's own <c>cast_complete</c> events, so every talent fork
/// is reachable by the fork dominance check and the demo shows all five buttons in use.
/// </summary>
public sealed class FiveButtonsTests
{
    private const int MaxTicks = 24000;

    private readonly ITestOutputHelper _output;

    public FiveButtonsTests(ITestOutputHelper output) => _output = output;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    private static readonly Lazy<KitRegistry> Kits = new(() => KitRegistry.From(Registry.Value));

    /// <summary>Runs <paramref name="scenario"/> with the real bots and counts completed casts per actor and ability.</summary>
    /// <param name="scenario">Bot oracle trace name.</param>
    /// <returns>Party actor id to (class id, ability id to completed casts), and the run end.</returns>
    private static (SortedDictionary<int, (string ClassId, SortedDictionary<string, int> Casts)> Party, string Result) Run(string scenario)
    {
        var dir = RolesOracleTraceTests.OracleDir();
        var trace = OracleTrace.Load(Path.Combine(dir, scenario + ".trace.jsonl"));
        var setup = RunSetup.Load(Path.Combine(dir, trace.SetupFile), trace.Seed);
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options);
        var bots = new SortedDictionary<int, IBot>();
        var initial = new SortedDictionary<int, BotMemory>();
        var party = new SortedDictionary<int, (string ClassId, SortedDictionary<string, int> Casts)>();
        foreach (var p in setup.Party)
        {
            bots[p.Id] = Bot.MakeBot(RoleSet.Default);
            initial[p.Id] = BotMemory.Default(100);
            party[p.Id] = (p.ClassId, new SortedDictionary<string, int>(StringComparer.Ordinal));
        }
        IReadOnlyDictionary<int, BotMemory> memories = initial;
        var prng = Skill.SeedPrng(trace.Seed);
        while (run.RunEnd.Result is null && run.State.Tick < MaxTicks)
        {
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, Kits.Value);
            var batch = Bot.RunBots(view, bots, memories, prng);
            memories = batch.Memories;
            prng = batch.Prng;
            var stepped = RunRoot.StepRun(run, batch.Commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type != "cast_complete" || e.Get("actorId") is not JsInt id || e.Get("abilityId") is not JsString ability)
                {
                    continue;
                }
                if (party.TryGetValue(checked((int)id.Value), out var member))
                {
                    member.Casts[ability.Value] = member.Casts.TryGetValue(ability.Value, out var n) ? n + 1 : 1;
                }
            }
        }
        var resultJs = run.RunEnd.Result?.ToJs();
        var result = resultJs is null ? "none" : string.Create(CultureInfo.InvariantCulture, $"{resultJs}@{run.RunEnd.ResultTick}");
        return (party, result);
    }

    [Theory(DisplayName = "five buttons > every real-role bot casts all five rotational abilities over a seeded key-2 run")]
    [InlineData("bots-hv-k2-s7")]
    [InlineData("bots-hv-k2-s11")]
    public void Every_bot_casts_all_five_rotational_abilities(string scenario)
    {
        var (party, result) = Run(scenario);
        var missing = new List<string>();
        foreach (var (actorId, member) in party)
        {
            Assert.True(Registry.Value.Classes.TryGetValue(member.ClassId, out var cls), $"{scenario}: actor {actorId} class {member.ClassId} is not in content");
            var slots = cls!.Abilities;
            var rotation = new[] { slots.Rot1, slots.Rot2, slots.Rot3, slots.Rot4, slots.Rot5 };
            var counts = rotation.Select(id => (Id: id, N: member.Casts.TryGetValue(id, out var n) ? n : 0)).ToList();
            _output.WriteLine($"{scenario} ({result}) actor {actorId} {member.ClassId}: {string.Join(" ", counts.Select(c => $"{c.Id}={c.N}"))}");
            missing.AddRange(counts.Where(c => c.N == 0).Select(c => $"actor {actorId} {member.ClassId} never casts {c.Id}"));
        }
        Assert.True(party.Count > 0, $"{scenario}: no party actor (vacuous)");
        Assert.True(missing.Count == 0, $"{scenario} ({result}): {string.Join("; ", missing)}");
    }
}
