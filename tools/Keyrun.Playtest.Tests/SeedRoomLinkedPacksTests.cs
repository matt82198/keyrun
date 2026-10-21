using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Content;
using Keyrun.Playtest;
using Keyrun.Sim.Encounter;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-SEED-1 (finding f-244d102b; WoW Mythic+ linked packs; design/BIBLE.md section 9: "The Seed teaches the boss's core
/// rule with no stakes", section 3: a mechanic is read before it lands). Matt's verdict on the Seed room: "the seed room
/// the extra mobs dont aggro and they just one tap it lmfao". Restated by the orchestrator (2026-10-21) after the previous
/// lane's measurements: only the warrior-remnant pack (actors 512-518) is linked to the Seed (layout.json links ["seed"]);
/// cantor-vigil (500-506) is a choir-room pack and is not in this acceptance.
/// The acceptance, one headless playthrough (<see cref="PlaytestRunner.Play"/>, key 2, seed 1, every seat the scripted bot):
/// warrior-remnant's pack_engage fires at the Seed's pull_start tick; death events for all seven actors 512-518 occur
/// before the boss pull_start; the Seed dies; forces PercentBp exceed 8400; RunEnd.Result is Timed; WipeStartTick is null;
/// the boss pull starts.
/// </summary>
public sealed class SeedRoomLinkedPacksTests
{
    /// <summary>The default headless run's key level (the playtest default, PlaytestOptions key 2).</summary>
    private const int Key = 2;

    /// <summary>The default headless run's seed.</summary>
    private const long Seed = 1;

    /// <summary>The Seed's pull id (content/data/dungeons/hollow-vault.json).</summary>
    private const string SeedPull = "seed";

    /// <summary>The boss pull id (<see cref="Keyrun.Content.Model.Layout.BossPullId"/>).</summary>
    private const string BossPull = "boss";

    /// <summary>The linked pack the Seed's pull engages (game/content/data/layout.json warrior-remnant, links ["seed"]).</summary>
    private const string WarriorRemnantPack = "warrior-remnant";

    /// <summary>Warrior Remnant's member actor ids (game/content/data/layout.json warrior-remnant, 7 members).</summary>
    private static readonly IReadOnlyList<int> WarriorRemnantActors = Enumerable.Range(512, 7).ToList();

    /// <summary>Enemy Forces must read above this many basis points (84.00%).</summary>
    private const int ForcesAboveBp = 8400;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the counts are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public SeedRoomLinkedPacksTests(ITestOutputHelper output) => _output = output;

    /// <summary>One parsed line of the run log: an event's type and tick, and its raw JSON for field reads.</summary>
    private sealed record LoggedEvent(int Tick, string Type, JsonElement Json);

    /// <summary>The default headless run, played to its end.</summary>
    private static readonly Lazy<PlaytestResult> Played = new(() => PlaytestRunner.Play(new PlaytestOptions(Key, Seed, null)));

    private static IEnumerable<LoggedEvent> Events(string log)
    {
        foreach (var line in log.Split('\n'))
        {
            if (!line.StartsWith("E {", StringComparison.Ordinal))
            {
                continue;
            }
            var json = JsonDocument.Parse(line[2..]).RootElement.Clone();
            yield return new LoggedEvent(json.GetProperty("tick").GetInt32(), json.GetProperty("type").GetString()!, json);
        }
    }

    private static int ActorOf(LoggedEvent e) => e.Json.GetProperty("actorId").GetInt32();

    /// <summary>The first tick of the pack's engage or the pull's start, or null when it never happened.</summary>
    private static LoggedEvent? First(IEnumerable<LoggedEvent> events, string type, string key, string id) =>
        events.FirstOrDefault(e => e.Type == type && e.Json.TryGetProperty(key, out var v) && v.GetString() == id);

    [Fact(DisplayName = "R-SEED-1 f-244d102b: the warrior-remnant pack engages with the Seed, both pack and Seed die before the boss pull, forces read above 84%, and the run is timed with no wipe")]
    public void R_SEED_1_f_244d102b_warrior_remnant_fights_the_seed_and_the_bots_close_on_its_casters()
    {
        var played = Played.Value;
        var events = Events(played.Log).ToList();

        var seedStart = First(events, "pull_start", "pullId", SeedPull);
        var bossStart = First(events, "pull_start", "pullId", BossPull);
        var engage = First(events, "pack_engage", "packId", WarriorRemnantPack);
        var seedId = seedStart is null
            ? -1
            : seedStart.Json.GetProperty("actorIds").EnumerateArray().Select(v => v.GetInt32()).DefaultIfEmpty(-1).First();

        var deathTick = events.Where(e => e.Type == "death").GroupBy(ActorOf).ToDictionary(g => g.Key, g => g.Min(e => e.Tick));
        var missing = WarriorRemnantActors.Where(id => !deathTick.ContainsKey(id)).ToList();
        var late = WarriorRemnantActors.Where(id => deathTick.TryGetValue(id, out var t) && (bossStart is null || t >= bossStart.Tick)).ToList();
        int? seedDeath = seedId >= 0 && deathTick.TryGetValue(seedId, out var sd) ? sd : null;

        var forces = ContentLoader.Load().Forces;
        var table = forces.Values.ToDictionary(v => v.EnemyId, v => v.Points, StringComparer.Ordinal);
        var forcesBp = Forces.Read(played.Run.State, table, forces.Required).PercentBp;

        _output.WriteLine(
            $"R-SEED-1 f-244d102b headless key={Key} seed={Seed} seedPullTick={seedStart?.Tick} seedId={seedId} " +
            $"engageTick={engage?.Tick} seedDeathTick={seedDeath} bossPullTick={bossStart?.Tick} " +
            $"warriorRemnantDeaths={WarriorRemnantActors.Count(id => deathTick.ContainsKey(id))}/7 forcesBp={forcesBp} " +
            $"result={played.Run.RunEnd.Result} wipeStartTick={played.Run.RunEnd.WipeStartTick?.ToString() ?? "null"}");
        foreach (var e in events.Where(e => e.Type is "pack_engage" or "pull_start" or "run_end" or "death"))
        {
            _output.WriteLine($"R-SEED-1 event tick={e.Tick} type={e.Type} {e.Json}");
        }

        Assert.NotNull(seedStart);
        Assert.NotNull(engage);
        Assert.True(engage!.Tick == seedStart!.Tick, $"warrior-remnant engaged at tick {engage.Tick}, not at the Seed's pull_start tick {seedStart.Tick}");
        Assert.True(missing.Count == 0, $"no death logged for warrior-remnant actors {string.Join(",", missing)}");
        Assert.NotNull(bossStart);
        Assert.True(late.Count == 0, $"warrior-remnant actors {string.Join(",", late)} died at or after the boss pull_start (tick {bossStart!.Tick})");
        Assert.True(seedDeath is not null, "the Seed did not die");
        Assert.True(forcesBp > ForcesAboveBp, $"Enemy Forces {forcesBp} bp; need above {ForcesAboveBp}");
        Assert.Equal(RunResult.Timed, played.Run.RunEnd.Result);
        Assert.Null(played.Run.RunEnd.WipeStartTick);
    }
}
