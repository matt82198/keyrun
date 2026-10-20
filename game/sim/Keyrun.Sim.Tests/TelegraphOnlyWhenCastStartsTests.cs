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
/// TELEGRAPH-READY (BIBLE s3 "Cast bars are the enemy's language"; wrong data shown to the player is P0): for every
/// scripted enemy in content/data/enemies/*.json, at keys 2, 10 and 20 (Tempo-scaled as the host scales them), every
/// floor telegraph the sim draws is for a cast that actually starts. Keyed scaling shrinks a loop's period below the
/// ability's cooldown (the Seed's slam: every 98 at key 2 against a 100-tick cooldown), so a step fires on ticks its
/// cast is refused not_ready; such a step must draw nothing. Both telegraph paths in <see cref="Scripts"/> are run:
/// the content as written (spansCast, as the host plays it) and the same scripts with spansCast cleared (as
/// <see cref="RunSetup"/> loads every oracle and bot harness run, the Run C shape). A spansCast telegraph's cast starts
/// castTicks before it resolves; any other telegraph's cast starts on its resolvesTick.
/// </summary>
public sealed class TelegraphOnlyWhenCastStartsTests
{
    private const int ActorId = 500;
    private const int Ticks = 3000;

    /// <summary>Every scripted enemy, at keys 2, 10 and 20, with spansCast kept and cleared.</summary>
    /// <returns>(file, enemy id, key, spansCast kept) rows.</returns>
    public static IEnumerable<object[]> Cases()
    {
        foreach (var file in new[] { "seed.json", "pair.json", "choir.json", "hollow-warden.json" })
        {
            foreach (var enemy in Enemies(file).Where(e => e.Get("script") is JsArray))
            {
                foreach (var key in new[] { 2, 10, 20 })
                {
                    foreach (var spans in new[] { true, false })
                    {
                        yield return new object[] { file, ((JsString)enemy.Get("id")!).Value, key, spans };
                    }
                }
            }
        }
    }

    private static List<JsObject> Enemies(string file) =>
        ((JsArray)JsValue.Parse(File.ReadAllText(Path.Combine(
            ContentRoots.RepoRootAbove(OracleFixtures.Directory), "content", "data", "enemies", file)))).Items
            .Cast<JsObject>().ToList();

    private static long Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    private sealed record Played(List<SimEvent> Events, HashSet<(string Cast, int Tick)> Starts, Run Run, IReadOnlyList<ScriptStep> Script);

    /// <summary>
    /// One enemy at the origin with the party 1 m away and too healthy to die, stepped <see cref="Ticks"/> ticks with no
    /// party commands, recording every cast the enemy starts (a cast bar's start tick, or an instant's landing tick).
    /// </summary>
    private static Played Play(string file, string enemyId, int key, bool spans)
    {
        var enemyDef = Enemies(file).Single(e => ((JsString)e.Get("id")!).Value == enemyId);
        var script = Tempo.ScaleScript(EncounterJson.ParseScript(enemyDef.Get("script")!), key)
            .Select(s => !spans && s.Telegraph is { } tg ? s with { Telegraph = tg with { SpansCast = false } } : s)
            .ToList();
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("solo", new[] { new PullSpawn(ActorId, enemyId, new Vec3(0, 0, 0), 512, 100_000_000) }),
        });
        var content = setup.Content with
        {
            Bounds = new Keyrun.Sim.Combat.Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [ActorId] = script },
            BreakBarSources = null,
            KeyLevel = null,
            ParTicks = null,
            Clauses = null,
        };
        var party = new[]
        {
            RunTestKit.Member(BossTestKit.Warden, "warden", ActorRole.Tank, 0, 1000, BossTestKit.PartyHealth, 100),
            RunTestKit.Member(BossTestKit.Chanter, "chanter", ActorRole.Healer, 500, 1000, BossTestKit.PartyHealth, 1),
            RunTestKit.Member(BossTestKit.Cinder, "cinder", ActorRole.Damage, -500, 1000, BossTestKit.PartyHealth, 100),
        };
        var run = RunRoot.CreateRun(BossTestKit.Seed, party, dungeon, content);
        var events = new List<SimEvent>();
        var starts = new HashSet<(string, int)>();
        while (run.State.Tick < Ticks)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            foreach (var e in stepped.Events.Where(e => e.Type == "cast_complete" && Int(e, "actorId") == ActorId))
            {
                if (run.Content.Abilities.TryGetValue(Str(e, "abilityId")!, out var def) && def.CastTicks == 0)
                {
                    starts.Add((def.Id, e.Tick));
                }
            }
            if (run.State.Actors[ActorId] is { CastId: { } castId, CastStartTick: { } castStart })
            {
                starts.Add((castId, castStart));
            }
        }
        return new Played(events, starts, run, script);
    }

    [Theory(DisplayName = "Every enemy floor telegraph, at every key, on both telegraph paths, is for a cast that starts")]
    [MemberData(nameof(Cases))]
    public void Every_telegraph_is_for_a_cast_that_starts(string file, string enemyId, int key, bool spans)
    {
        var p = Play(file, enemyId, key, spans);
        var drawn = p.Events.Where(e => e.Type == "telegraph" && Int(e, "actorId") == ActorId).ToList();

        // Not vacuous: a sim that drew no telegraph at all would tell no lies. Every telegraphed step is still shown.
        foreach (var cast in p.Script.Where(s => s.Telegraph is not null && s.Trigger != StepTrigger.OnDeath).Select(s => s.Cast).Distinct())
        {
            Assert.True(drawn.Any(e => Str(e, "cast") == cast), $"{enemyId} at key {key} (spansCast {spans}): no {cast} telegraph drawn in {Ticks} ticks");
        }

        var lies = new List<string>();
        foreach (var tg in drawn)
        {
            var cast = Str(tg, "cast")!;
            var step = p.Script.First(s => s.Cast == cast && s.Telegraph is not null);
            var castTicks = p.Run.Content.Abilities[cast].CastTicks;
            var resolves = (int)Int(tg, "resolvesTick");
            var start = resolves - (step.Telegraph!.SpansCast && castTicks > 0 ? castTicks : 0);
            if (start >= Ticks - 1)
            {
                continue; // its cast tick lies past the watch
            }
            if (!p.Starts.Contains((cast, start)))
            {
                lies.Add($"{cast} telegraph drawn on {tg.Tick} (resolves {resolves}) but no {cast} cast starts on {start}");
            }
        }
        Assert.True(lies.Count == 0, $"{enemyId} at key {key} (spansCast {spans}): {lies.Count} of {drawn.Count} telegraphs for a cast that never started:\n" + string.Join("\n", lies.Take(10)));
    }
}
