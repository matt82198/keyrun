using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// TELEGRAPH-HONEST (design/BIBLE.md section 3: cast bars and floor telegraphs are the enemy's language). A script
/// telegraph announces a cast <c>leadTicks</c> ahead (<see cref="Scripts.StepEnemyScripts"/>), so a telegraph whose
/// cast never comes lies to the player. Found by the #289 lane: at key 2 Tempo shrinks seed.slam's loop from 100 to 98
/// ticks against its 100-tick cooldown, so every second slam telegraph is drawn and combat rejects the cast it promised.
/// Every pull of both scripted oracle runs (hollow-vault k2 and k10, the Tempo-scaled content the host plays) is run
/// on its own through the real <see cref="RunRoot.StepRun"/> path with the party standing on the pull and too healthy
/// to die, so every enemy script, not just the Seed's, fires its full schedule; each telegraph must be followed by its
/// own cast, started by the same caster on the telegraph's resolvesTick (a spansCast telegraph: castTicks before it).
/// </summary>
public sealed class TelegraphHonestTests
{
    /// <summary>Ticks each pull is watched: past two boss Lock cycles and many periods of every loop.</summary>
    public const int WatchTicks = 2400;

    private const long PartyHealth = 1_000_000_000L;

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public TelegraphHonestTests(ITestOutputHelper output) => _out = output;

    private sealed record Watched(List<(int Tick, int Actor, string Cast, int Resolves)> Telegraphs, HashSet<(int Actor, string Cast, int Tick)> Casts);

    private static Watched WatchPull(RunSetup setup, PullDef pull)
    {
        var anchor = pull.Spawns[0].Pos;
        var party = setup.Party.Select((m, i) => m with
        {
            Pos = new Vec3(anchor.X + ((i - 2) * 600), 0, anchor.Z),
            MaxHealth = PartyHealth,
        }).ToList();
        var dungeon = setup.Dungeon with { Pulls = new[] { pull } };
        var run = RunRoot.CreateRun(setup.Seed, party, dungeon, setup.Content, setup.Options);

        var telegraphs = new List<(int, int, string, int)>();
        var casts = new HashSet<(int, string, int)>();
        while (run.State.Tick < WatchTicks)
        {
            var stepped = RunRoot.StepRun(run, new List<Command>());
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "telegraph")
                {
                    telegraphs.Add((e.Tick, (int)((JsInt)e.Get("actorId")!).Value, ((JsString)e.Get("cast")!).Value, (int)((JsInt)e.Get("resolvesTick")!).Value));
                }
                else if (e.Type == "cast_complete" && e.Get("abilityId") is JsString ab &&
                    setup.Content.Abilities.TryGetValue(ab.Value, out var def) && def.CastTicks == 0)
                {
                    // An instant starts and completes on one tick, so it never shows as a CastId between steps.
                    casts.Add(((int)((JsInt)e.Get("actorId")!).Value, ab.Value, e.Tick));
                }
            }
            foreach (var a in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.CastId is not null && a.CastStartTick is not null))
            {
                casts.Add((a.Id, a.CastId!, a.CastStartTick!.Value));
            }
        }
        return new Watched(telegraphs, casts);
    }

    [Theory(DisplayName = "telegraph honest: every scripted telegraph is followed by its own cast on its resolvesTick, every pull, every enemy")]
    [InlineData("hollow-vault-k2.setup.json")]
    [InlineData("hollow-vault-k10.setup.json")]
    public void Every_scripted_telegraph_is_followed_by_its_cast(string setupFile)
    {
        var path = OracleFixtures.PathOf(setupFile);
        var loaded = RunSetup.Load(path, 7);
        // The host plays the content's telegraphs as written, spansCast included (HostRunSetup copies it); RunSetup
        // clears spansCast so oracle replays stay TS-equal, so the scripts are re-read here with it kept.
        var raw = (JsObject)((JsObject)JsValue.Parse(System.IO.File.ReadAllText(path))).Get("content")!;
        var setup = loaded with { Content = loaded.Content with { EnemyScripts = EncounterJson.ParseScripts(raw.Get("enemyScripts")!) } };
        Assert.Contains(setup.Content.EnemyScripts.Values.SelectMany(s => s), s => s.Telegraph is { SpansCast: true });
        var lies = new List<string>();
        var unannounced = new List<string>();
        var total = 0;
        var castersSeen = new HashSet<string>();
        // SLAM-TELEGRAPH (#313): a spansCast telegraph resolves on the tick its cast LANDS, so its cast starts castTicks
        // earlier (Tempo never scales castTicks); any other telegraph resolves on the cast's start tick.
        int Span(int actor, string cast) =>
            setup.Content.EnemyScripts.TryGetValue(actor, out var sc) &&
            sc.Any(s => s.Cast == cast && s.Telegraph is { SpansCast: true }) &&
            setup.Content.Abilities.TryGetValue(cast, out var d) && d.CastTicks > 0 ? d.CastTicks : 0;
        foreach (var pull in setup.Dungeon.Pulls)
        {
            var w = WatchPull(setup, pull);
            total += w.Telegraphs.Count;
            foreach (var (tick, actor, cast, resolves) in w.Telegraphs)
            {
                castersSeen.Add(cast);
                if (resolves >= WatchTicks)
                {
                    continue; // its cast tick lies past the watch
                }
                var start = resolves - Span(actor, cast);
                if (!w.Casts.Contains((actor, cast, start)))
                {
                    lies.Add(string.Create(CultureInfo.InvariantCulture, $"{pull.Id}: actor {actor} {cast} telegraphed at {tick} to resolve at {resolves}, no cast started at {start}"));
                }
            }
            // The converse, so the fix cannot pass by dropping telegraphs: every cast of a telegraphed step was announced.
            foreach (var (actor, cast, castTick) in w.Casts)
            {
                var telegraphed = setup.Content.EnemyScripts.TryGetValue(actor, out var script) &&
                    script.Any(s => s.Cast == cast && s.Trigger != StepTrigger.OnDeath && s.Telegraph is not null);
                var resolvesAt = castTick + Span(actor, cast);
                if (telegraphed && resolvesAt < WatchTicks && !w.Telegraphs.Any(t => t.Actor == actor && t.Cast == cast && t.Resolves == resolvesAt))
                {
                    unannounced.Add(string.Create(CultureInfo.InvariantCulture, $"{pull.Id}: actor {actor} {cast} cast at {castTick} with no telegraph"));
                }
            }
        }
        _out.WriteLine($"{setupFile}: {setup.Dungeon.Pulls.Count} pulls, {total} telegraphs ({string.Join(", ", castersSeen.OrderBy(c => c, System.StringComparer.Ordinal))}), {lies.Count} with no cast");
        foreach (var l in lies.Concat(unannounced))
        {
            _out.WriteLine(l);
        }
        // Non-vacuity: every telegraphed ability in the run's scripts must be drawn, or a silent script passes.
        var scripted = setup.Content.EnemyScripts.Values.SelectMany(s => s)
            .Where(s => s.Trigger != StepTrigger.OnDeath && s.Telegraph is not null).Select(s => s.Cast).ToHashSet();
        Assert.True(scripted.SetEquals(castersSeen), $"telegraphed abilities never drawn: {string.Join(", ", scripted.Except(castersSeen))}");
        Assert.True(unannounced.Count == 0, $"{unannounced.Count} casts with no telegraph, first: {string.Join("; ", unannounced.Take(5))}");
        Assert.True(lies.Count == 0, $"{lies.Count} telegraphs with no cast, first: {string.Join("; ", lies.Take(5))}");
    }
}
