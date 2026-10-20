using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// LOCK-TELEGRAPH (BIBLE s3 "Cast bars are the enemy's language", f-dfafcec6): the Hollow Warden's Lock was the one boss
/// mechanic with no cast bar and no floor telegraph (content castTicks 0, no telegraph on its script step, and the armed
/// rules opened the Window on the tick the boss crossed 50% with nothing shown first). Armed with
/// <see cref="BossConfig.CastLock"/> (as the host arms it from content), each Lock begins with the boss casting
/// <c>hollowWarden.lock</c>: a real cast bar on the boss for the content cast time, a floor circle from the boss's Lock
/// script step that is drawn as the bar starts and resolves on the tick it lands, and the Window opening on that tick.
/// The cast's own content effects (a self Window and shield) never land: the armed rules own the shield and the Window.
/// Runs on the BossTestKit boss-only run (k2 oracle content) with the boss script from content/data, boss at 50%.
/// </summary>
public sealed class LockTelegraphTests
{
    private const int Key = 2;
    private const int Ticks = 200;
    private const string LockId = "hollowWarden.lock";

    private static IReadOnlyList<ScriptStep> ContentBossScript()
    {
        var path = Path.Combine(ContentRoots.RepoRootAbove(OracleFixtures.Directory), "content", "data", "enemies", "hollow-warden.json");
        var boss = ((JsArray)JsValue.Parse(File.ReadAllText(path))).Items.Cast<JsObject>()
            .Single(e => ((JsString)e.Get("id")!).Value == "hollowWarden.boss");
        return Tempo.ScaleScript(EncounterJson.ParseScript(boss.Get("script")!), Key);
    }

    private sealed record Played(
        List<SimEvent> Events,
        Dictionary<int, (string? CastId, int? Start, int? Ends)> BossCast,
        Dictionary<int, IReadOnlyList<Telegraph>> Pending,
        Dictionary<int, List<EffectInstance>> BossEffects,
        Run Run);

    /// <summary>Steps the armed run with no party commands; records per state tick the boss's cast and effects, and per stepped tick the pending telegraphs.</summary>
    private static Played Play()
    {
        var run = BossTestKit.BuildRun(bossHealth: 30000, armed: false);
        run = run with
        {
            Content = run.Content with
            {
                EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [BossTestKit.Boss] = ContentBossScript() },
            },
        };
        run = run with { Encounter = BossRules.Arm(run.Encounter, BossTestKit.Config() with { CastLock = true }) };

        var events = new List<SimEvent>();
        var casts = new Dictionary<int, (string?, int?, int?)>();
        var pending = new Dictionary<int, IReadOnlyList<Telegraph>>();
        var effects = new Dictionary<int, List<EffectInstance>>();
        while (run.State.Tick < Ticks)
        {
            var tick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            pending[tick] = run.Encounter.Telegraphs;
            var boss = run.State.Actors[BossTestKit.Boss];
            casts[run.State.Tick] = (boss.CastId, boss.CastStartTick, boss.CastEndsTick);
            effects[run.State.Tick] = boss.Effects.ToList();
        }
        return new Played(events, casts, pending, effects, run);
    }

    private static long Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    [Fact(DisplayName = "The Lock is a boss cast with a bar and a floor circle, and its Window opens on the tick the cast lands")]
    public void Lock_begins_with_a_cast_bar_and_a_floor_circle_that_land_as_the_window_opens()
    {
        var played = Play();
        var events = played.Events;
        var castTicks = played.Run.Content.Abilities[LockId].CastTicks;
        Assert.True(castTicks > 0, $"{LockId}: castTicks {castTicks}, no cast bar");

        var begin = events.FirstOrDefault(e => e.Type == "lock_cast");
        Assert.True(begin is not null, "no lock_cast: the Lock began with no cast. Lock events: " +
            string.Join(" | ", events.Where(e => e.Type.StartsWith("lock_", StringComparison.Ordinal)).Select(e => e.ToCanonicalJson())));
        var start = begin!.Tick;
        Assert.Equal(1, Int(begin, "lockIndex"));
        var open = Assert.Single(events, e => e.Type == "lock_open");
        var lands = start + castTicks;
        Assert.Equal(lands, open.Tick);
        Assert.Equal(lands, Int(begin, "landsTick"));
        Assert.Equal(1, Int(open, "lockIndex"));

        // The cast bar: the boss is casting the Lock on every state from the bar's start to the landing tick, and
        // nothing else it casts replaces it; the cast lands (cast_complete) on the tick the Window opens.
        for (var t = start + 1; t <= lands; t++)
        {
            Assert.Equal<(string?, int?, int?)>((LockId, start, lands), played.BossCast[t]);
        }
        Assert.Contains(events, e => e.Type == "cast_complete" && e.Tick == lands && Int(e, "actorId") == BossTestKit.Boss && Str(e, "abilityId") == LockId);

        // The floor circle: drawn on the tick the bar starts, resolving on the landing tick, on the floor every tick between.
        var circle = Assert.Single(events, e => e.Type == "telegraph" && Int(e, "actorId") == BossTestKit.Boss && Str(e, "cast") == LockId);
        Assert.Equal(start, circle.Tick);
        Assert.Equal(lands, Int(circle, "resolvesTick"));
        Assert.Equal("circle", Str(circle, "shape"));
        Assert.True(Int(circle, "radius") > 0, "the Lock's circle has no radius");
        for (var t = start; t < lands; t++)
        {
            Assert.True(
                played.Pending[t].Any(tg => tg.SourceId == BossTestKit.Boss && tg.AbilityId == LockId && tg.ResolvesTick == lands),
                $"no Lock circle on the floor on tick {t}, {lands - t} ticks before the Window opens on {lands}");
        }

        // The armed rules own the Lock's effects: the cast's content self Window never opens, and the boss carries
        // exactly one shield, the armed rules', ending with the Window.
        Assert.DoesNotContain(events, e => e.Type == "window_opened" && Int(e, "setupActorId") == BossTestKit.Boss);
        var shields = played.BossEffects[lands + 1].Where(e => e.EffectId == BossTestKit.Config().ShieldEffectId).ToList();
        var shield = Assert.Single(shields);
        Assert.Equal(Int(open, "endsTick"), shield.EndsTick);
    }

    [Fact(DisplayName = "Every boss telegraph on the armed Lock run resolves on a tick its cast lands (the Lock never strands a slam or cleave circle)")]
    public void Boss_telegraphs_resolve_only_on_casts_that_land()
    {
        var played = Play();
        var events = played.Events;
        Assert.Contains(events, e => e.Type == "lock_open");
        var drawn = events.Where(e => e.Type == "telegraph" && Int(e, "actorId") == BossTestKit.Boss && Int(e, "resolvesTick") < played.Run.State.Tick).ToList();
        Assert.NotEmpty(drawn);
        foreach (var tg in drawn)
        {
            var cast = Str(tg, "cast");
            var resolves = Int(tg, "resolvesTick");
            Assert.True(
                events.Any(e => e.Type == "cast_complete" && e.Tick == resolves && Int(e, "actorId") == BossTestKit.Boss && Str(e, "abilityId") == cast),
                $"{cast}: telegraph drawn on tick {tg.Tick} resolves on {resolves}, but no {cast} lands then");
        }
    }
}
