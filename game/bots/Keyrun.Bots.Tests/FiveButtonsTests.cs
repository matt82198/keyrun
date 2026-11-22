using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
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
    private readonly ITestOutputHelper _output;

    public FiveButtonsTests(ITestOutputHelper output) => _output = output;

    /// <summary>Runs <paramref name="scenario"/> with the real bots and counts completed casts per actor and ability.</summary>
    /// <param name="scenario">Bot oracle trace name.</param>
    /// <returns>Party actor id to (class id, ability id to completed casts), and the run end.</returns>
    private static (SortedDictionary<int, (string ClassId, SortedDictionary<string, int> Casts)> Party, string Result) Run(string scenario)
    {
        var dir = RolesOracleTraceTests.OracleDir();
        var trace = OracleTrace.Load(Path.Combine(dir, scenario + ".trace.jsonl"));
        var seeded = ForkParityTests.LoadScenario((scenario, trace.SetupFile, trace.Seed, 100));
        var party = new SortedDictionary<int, (string ClassId, SortedDictionary<string, int> Casts)>();
        foreach (var p in seeded.Setup.Party)
        {
            party[p.Id] = (p.ClassId, new SortedDictionary<string, int>(StringComparer.Ordinal));
        }
        var measured = ForkParityTests.Measure(seeded, null, null, events =>
        {
            foreach (var e in events)
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
        });
        var result = measured.Result == "none" ? "none" : string.Create(CultureInfo.InvariantCulture, $"{measured.Result}@{measured.ClearTick}");
        return (party, result);
    }

    [Fact]
    public void Guard_break_uses_the_live_kit_cooldown_when_the_oracle_differs()
    {
        const string id = "warden.guardBreak";
        var scenario = ForkParityTests.LoadScenario(("kit-parity", "hollow-vault-k2.setup.json", 7, 100));
        var setup = scenario.Setup;
        var liveCooldown = ForkParityTests.Registry.Value.Abilities[id].CooldownTicks;
        var abilities = setup.Content.Abilities.ToDictionary(pair => pair.Key, pair => pair.Value);
        abilities[id] = abilities[id] with { CooldownTicks = liveCooldown + 37 };
        var staleSetup = setup with { Content = setup.Content with { Abilities = abilities } };

        int? simCooldown = null;
        ForkParityTests.Measure(scenario with { Setup = staleSetup, MaxTicks = 1 }, null, null,
            beforeStep: run => simCooldown = run.Content.Abilities[id].CooldownTicks);

        Assert.True(simCooldown == liveCooldown,
            $"{id} cooldown ticks: bot kit {liveCooldown}, sim {simCooldown}, oracle {abilities[id].CooldownTicks}");
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
            Assert.True(ForkParityTests.Registry.Value.Classes.TryGetValue(member.ClassId, out var cls), $"{scenario}: actor {actorId} class {member.ClassId} is not in content");
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
