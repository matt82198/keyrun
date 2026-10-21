using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// HOST-PATH-THREATS (design/BIBLE.md section 3: "cast bars are the enemy's language"): in a default host-driven run, every
/// damaging enemy cast the Hollow Warden (actor 900), the Seed elite (actor 108) and the Sundered Twins (actors 901, 902)
/// perform has a cast bar longer than zero ticks and a floor telegraph that is drawn at or before the cast starts and
/// still up when it lands. The run is the one SimDriver.StartRun builds, headlessly: <see cref="HostRunSetup.Build"/> at
/// the key and seed, <see cref="HostRunSetup.StartFor"/>, <see cref="HostRunSetup.PartyFor"/>,
/// <see cref="HostRunSetup.WithBotLoadouts"/>, <see cref="RunRoot.CreateRun"/> and <see cref="BossRules.Arm"/>, then one
/// <see cref="Backfill.Step"/> per tick with the autorun human (<see cref="ScriptedWarden"/>) and the bot seats
/// (<see cref="BotCommandSource"/>), as MovieFramesDefaultRunTests drives it. Every enemy cast by the four actors is
/// enumerated; a cast is a threat when its ability has a damage op. The lock casts (hollowWarden.lock, seed.lock) and
/// seed.deathBloom deal no damage and must not be classified as threats.
/// </summary>
public sealed class HostPathThreatTelegraphTests
{
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";
    private const int MaxTicks = 24000;

    /// <summary>The Seed elite (content/data/enemies/seed.json), the Hollow Warden boss and the two Sundered Twins.</summary>
    private static readonly int[] CasterIds = { 108, 900, 901, 902 };

    /// <summary>The casts the four actors perform that deal no damage (the Lock channels and the Seed's on-death bloom).</summary>
    private static readonly string[] NonThreatCasts = { "hollowWarden.lock", "seed.lock", "seed.deathBloom" };

    /// <summary>The telegraph shapes a floor telegraph may take (shape, not colour).</summary>
    private static readonly string[] TelegraphShapes = { "circle", "cone", "line" };

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the enumerated casts are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public HostPathThreatTelegraphTests(ITestOutputHelper output) => _output = output;

    /// <summary>One enemy cast by a caster: its start tick and the tick it lands (the bar's end).</summary>
    private sealed record Cast(int ActorId, string AbilityId, int StartTick, int LandTick);

    /// <summary>One "telegraph" event: the caster, the cast it announces, the tick it was drawn and the tick it resolves.</summary>
    private sealed record Telegraph(int ActorId, string AbilityId, int EmittedTick, int ResolvesTick, string Shape);

    /// <summary>One whole default run: its casts by the four actors, every telegraph drawn, and the content abilities used.</summary>
    private sealed record Drive(
        int Key,
        long Seed,
        int EndTick,
        IReadOnlyList<Cast> Casts,
        IReadOnlyList<Telegraph> Telegraphs,
        IReadOnlyDictionary<string, Keyrun.Sim.Combat.AbilityDef> Abilities);

    private static readonly Lazy<Drive> Key2Seed1 = new(() => DriveRun(2, 1));
    private static readonly Lazy<Drive> Key10Seed7 = new(() => DriveRun(10, 7));

    /// <summary>The two default launches the proof covers: key 2 seed 1 and key 10 seed 7.</summary>
    public static IEnumerable<object[]> Launches()
    {
        yield return new object[] { 2, 1L };
        yield return new object[] { 10, 7L };
    }

    private static Drive Launch(int key, long seed) => key == 2 && seed == 1 ? Key2Seed1.Value : key == 10 && seed == 7 ? Key10Seed7.Value : DriveRun(key, seed);

    /// <summary>The default host run at <paramref name="key"/> and <paramref name="seed"/>, stepped to its end or <see cref="MaxTicks"/>.</summary>
    private static Drive DriveRun(int key, long seed)
    {
        var registry = Registry.Value;
        var setup = HostRunSetup.Build(registry, DungeonId, key, HumanClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();

        var backfill = BackfillState.Empty;
        var seen = new HashSet<(int ActorId, int StartTick)>();
        var casts = new List<Cast>();
        var telegraphs = new List<Telegraph>();
        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null)
        {
            var commands = new List<Command>();
            // SimDriver.StepOnce order: the human's commands first, then every bot seat's.
            warden.Sample(run.State.Tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;

            foreach (var e in step.Step.Events.Where(e => e.Type == "telegraph"))
            {
                telegraphs.Add(new Telegraph(
                    e.Get("actorId") is JsInt actor ? (int)actor.Value : -1,
                    e.Get("cast") is JsString cast ? cast.Value : string.Empty,
                    e.Tick,
                    e.Get("resolvesTick") is JsInt resolves ? (int)resolves.Value : -1,
                    e.Get("shape") is JsString shape ? shape.Value : string.Empty));
            }

            foreach (var a in run.State.Actors.Values)
            {
                if (!CasterIds.Contains(a.Id) || a.CastId is null || a.CastStartTick is not { } startTick)
                {
                    continue;
                }
                if (seen.Add((a.Id, startTick)))
                {
                    // A cast with no recorded end lands on its start, which fails the bar check below (fail closed).
                    casts.Add(new Cast(a.Id, a.CastId, startTick, a.CastEndsTick ?? startTick));
                }
            }
        }

        return new Drive(key, seed, run.State.Tick, casts, telegraphs, setup.Content.Abilities);
    }

    private static bool Damages(Drive d, string abilityId) =>
        d.Abilities.TryGetValue(abilityId, out var def) && def.Effects.Any(op => op is Keyrun.Sim.Combat.DamageOp);

    private static bool IsThreat(Drive d, Cast c) => Damages(d, c.AbilityId);

    /// <summary>The threat casts of a run, in the order they started.</summary>
    private static IEnumerable<Cast> ThreatCasts(Drive d) => d.Casts.Where(c => IsThreat(d, c));

    private void Print(Drive d)
    {
        _output.WriteLine($"HOSTTHREAT key={d.Key} seed={d.Seed} endTick={d.EndTick} casts={d.Casts.Count} telegraphs={d.Telegraphs.Count}");
        foreach (var c in d.Casts)
        {
            var bar = d.Abilities.TryGetValue(c.AbilityId, out var def) ? def.CastTicks : -1;
            _output.WriteLine(
                $"HOSTTHREAT cast actor={c.ActorId} ability={c.AbilityId} start={c.StartTick} land={c.LandTick} castTicks={bar} threat={IsThreat(d, c)}");
        }
        foreach (var t in d.Telegraphs.Where(t => CasterIds.Contains(t.ActorId)))
        {
            _output.WriteLine(
                $"HOSTTHREAT telegraph actor={t.ActorId} cast={t.AbilityId} emitted={t.EmittedTick} resolves={t.ResolvesTick} shape={t.Shape}");
        }
    }

    [Theory(DisplayName = "Host threats (bible s3): every damaging cast by 108, 900, 901, 902 has a cast bar above zero ticks")]
    [MemberData(nameof(Launches))]
    public void Every_damaging_host_cast_has_a_bar(int key, long seed)
    {
        var d = Launch(key, seed);
        Print(d);
        var threats = ThreatCasts(d).ToList();
        // Non-vacuity: the default run must reach the enemy casts, or the checks below prove nothing.
        Assert.True(threats.Count >= 1, $"key {key} seed {seed}: no damaging cast by {string.Join(",", CasterIds)} in {d.EndTick} ticks");

        var violations = new List<string>();
        foreach (var c in threats)
        {
            var def = d.Abilities[c.AbilityId];
            // A cast's bar is its content cast time; the sim's cast end (CastEndsTick) must also lie past its start.
            if (def.CastTicks <= 0 || c.LandTick <= c.StartTick)
            {
                violations.Add($"actor {c.ActorId} {c.AbilityId} start={c.StartTick} land={c.LandTick} castTicks={def.CastTicks}: no visible bar");
            }
        }
        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }

    [Theory(DisplayName = "Host threats (bible s3): every damaging cast by 108, 900, 901, 902 has a floor telegraph drawn by its start and up until it lands")]
    [MemberData(nameof(Launches))]
    public void Every_damaging_host_cast_has_a_floor_telegraph_in_time(int key, long seed)
    {
        var d = Launch(key, seed);
        var threats = ThreatCasts(d).ToList();
        Assert.True(threats.Count >= 1, $"key {key} seed {seed}: no damaging cast by {string.Join(",", CasterIds)} in {d.EndTick} ticks");

        var violations = new List<string>();
        foreach (var c in threats)
        {
            // The telegraph of this cast: the same caster and ability, drawn at or before its start, resolving no earlier than its landing.
            var match = d.Telegraphs.FirstOrDefault(t =>
                t.ActorId == c.ActorId && t.AbilityId == c.AbilityId && t.EmittedTick <= c.StartTick && t.ResolvesTick >= c.LandTick);
            if (match is null)
            {
                var near = d.Telegraphs.Where(t => t.ActorId == c.ActorId && t.AbilityId == c.AbilityId)
                    .Select(t => $"emitted={t.EmittedTick} resolves={t.ResolvesTick}");
                violations.Add(
                    $"actor {c.ActorId} {c.AbilityId} start={c.StartTick} land={c.LandTick}: no floor telegraph in time ({string.Join(", ", near)})");
                continue;
            }
            if (!TelegraphShapes.Contains(match.Shape, StringComparer.Ordinal))
            {
                violations.Add($"actor {c.ActorId} {c.AbilityId} start={c.StartTick}: telegraph shape \"{match.Shape}\" is not circle, cone or line");
            }
        }
        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }

    [Theory(DisplayName = "Host threats (bible s3): the enemy cast enumeration is complete; lock and seed.deathBloom are not threats")]
    [MemberData(nameof(Launches))]
    public void Enumeration_classifies_lock_and_deathbloom_as_non_threats(int key, long seed)
    {
        var d = Launch(key, seed);
        var nonThreats = d.Casts.Where(c => !IsThreat(d, c)).ToList();
        _output.WriteLine($"HOSTTHREAT non-threat casts: {string.Join(", ", nonThreats.Select(c => $"{c.ActorId}:{c.AbilityId}").Distinct())}");

        // Every enumerated cast resolves to a known ability; a cast that is not in the content would be a silent gap.
        var unknown = d.Casts.Where(c => !d.Abilities.ContainsKey(c.AbilityId)).Select(c => c.AbilityId).Distinct().ToList();
        Assert.True(unknown.Count == 0, $"casts with no content ability: {string.Join(", ", unknown)}");
        // A lock or the bloom that dealt damage would be a threat the telegraph checks above must cover; none may.
        foreach (var name in NonThreatCasts)
        {
            Assert.False(Damages(d, name), $"{name} carries a damage op, so it is a threat and must be covered by the telegraph checks");
        }
        Assert.All(nonThreats, c => Assert.Contains(c.AbilityId, NonThreatCasts));
    }
}
