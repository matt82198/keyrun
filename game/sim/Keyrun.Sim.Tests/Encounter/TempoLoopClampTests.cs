using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// TEMPO-LOOP-CLAMP (f-57c04b4a; BIBLE s0 test 1, "keys that are genuinely hard because of what enemies do"): Tempo
/// shortens an enemy's loop, but a step never fires its cast before the cast's cooldown allows it. The effective
/// interval is max(loop, cooldown), and a cast combat refuses not_ready is a scheduling bug that the run reports as a
/// <c>cast_refused</c> event (reason not_ready), never a silent skip. Before the fix a loop that came due inside its
/// cooldown (Tempo shrank the period, or a busy caster pushed the last cast late) issued a cast combat refused, and that
/// occurrence of the loop was lost: the Seed's slam, a 98-tick loop over a 100-tick cooldown, landed every 196 ticks.
/// The Seed stands alone in the real run root (<see cref="RunRoot.StepRun"/>) against an idle, unkillable party.
/// </summary>
public sealed class TempoLoopClampTests
{
    private const int ActorId = 500;
    private const int Ticks = 3000;
    private const string Slam = "seed.slam";

    private static JsObject SeedDef() =>
        ((JsArray)JsValue.Parse(File.ReadAllText(Path.Combine(
            ContentRoots.RepoRootAbove(OracleFixtures.Directory), "content", "data", "enemies", "seed.json")))).Items
            .Cast<JsObject>().Single(e => ((JsString)e.Get("id")!).Value == "seed.elite");

    private static IReadOnlyList<ScriptStep> RawScript() => EncounterJson.ParseScript(SeedDef().Get("script")!);

    /// <summary>The Seed alone on the run root for <see cref="Ticks"/> ticks; every event the run emitted.</summary>
    private static List<SimEvent> Play(long seed, IReadOnlyList<ScriptStep> script, IReadOnlyDictionary<string, Combat.AbilityDef> abilities)
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("solo", new[] { new PullSpawn(ActorId, "seed.elite", new Vec3(0, 0, 0), 512, 100_000_000) }),
        });
        var content = setup.Content with
        {
            Abilities = abilities,
            Bounds = new Bounds(-20000, 20000, -20000, 20000),
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
        var run = RunRoot.CreateRun(seed, party, dungeon, content);
        var events = new List<SimEvent>();
        while (run.State.Tick < Ticks)
        {
            var stepped = RunRoot.StepRun(run, System.Array.Empty<Command>());
            run = stepped.Run;
            events.AddRange(stepped.Events);
        }
        return events;
    }

    /// <summary>The script and ability table as HostRunSetup.Build scales them at <paramref name="key"/> (TEMPO-CAST-FLOOR).</summary>
    private static (IReadOnlyList<ScriptStep> Script, IReadOnlyDictionary<string, Combat.AbilityDef> Abilities) AsHostScales(int key)
    {
        var table = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), 1).Content.Abilities;
        var script = Tempo.ScaleScript(RawScript(), key,
            id => table.TryGetValue(id, out var a) ? Tempo.ScaledCastFloorTicks(a.CooldownTicks, a.CastTicks, key) : 0);
        return (script, Tempo.ScaleEnemyCooldowns(table, new[] { script }, key));
    }

    private static string Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : "";

    private static List<SimEvent> NotReady(List<SimEvent> events) =>
        events.Where(e => e.Type == "cast_refused" && Str(e, "reason") == "not_ready").ToList();

    private static List<int> SlamGaps(List<SimEvent> events)
    {
        var ticks = events.Where(e => e.Type == "cast_complete" && Str(e, "abilityId") == Slam).Select(e => e.Tick).ToList();
        return ticks.Zip(ticks.Skip(1), (a, b) => b - a).ToList();
    }

    public static IEnumerable<object[]> SeedsAndKeys() =>
        from key in new[] { 2, 10 } from seed in Enumerable.Range(1, 20) select new object[] { key, (long)seed };

    [Theory(DisplayName = "Tempo loop clamp: keys 2 and 10, seeds 1-20, the Seed's slam lands at its Tempo interval within a tick and no cast is refused not_ready")]
    [MemberData(nameof(SeedsAndKeys))]
    public void Keyed_loops_land_at_their_interval_and_nothing_is_refused(int key, long seed)
    {
        var (script, abilities) = AsHostScales(key);
        var events = Play(seed, script, abilities);
        Assert.Empty(NotReady(events));
        var every = script.Single(s => s.Cast == Slam).Every!.Value;
        Assert.Equal(Tempo.ScaleCastEvery(100, key), every);
        var gaps = SlamGaps(events);
        Assert.True(gaps.Count >= Ticks / every - 3, $"key {key} seed {seed}: only {gaps.Count + 1} slams in {Ticks} ticks at every {every}");
        // A slam the caster's lash pushes late lands up to a tick late; the next one is then clamped to the cooldown.
        Assert.All(gaps, g => Assert.InRange(g, every - 1, every + 1));
        Assert.True(gaps.Count(g => g == every) * 10 >= gaps.Count * 9, $"key {key} seed {seed}: slam gaps [{string.Join(",", gaps)}], every {every}");
    }

    [Fact(DisplayName = "Tempo loop clamp: without Tempo (key 1) the Seed's slam lands every 100 ticks exactly, as before")]
    public void Without_tempo_the_cadence_is_unchanged()
    {
        var (script, abilities) = AsHostScales(1);
        var events = Play(1, script, abilities);
        Assert.Empty(NotReady(events));
        var gaps = SlamGaps(events);
        Assert.True(gaps.Count >= 25, $"only {gaps.Count + 1} slams");
        Assert.All(gaps, g => Assert.Equal(100, g));
    }

    [Fact(DisplayName = "Tempo loop clamp: a loop shorter than its cooldown (98 over 100) waits for the cooldown: every 100 ticks, never 196, nothing refused")]
    public void A_loop_inside_its_cooldown_waits_for_it()
    {
        // The pre-TEMPO-CAST-FLOOR shape (key-2 Tempo on the loop, the authored cooldown on the ability), standing in for
        // any loop a Tempo spike (the key, or the boss's failed-Lock stacks) shrinks below its cast's cooldown.
        var table = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), 1).Content.Abilities;
        Assert.Equal(100, table[Slam].CooldownTicks);
        var script = Tempo.ScaleScript(RawScript(), 2);
        Assert.Equal(98, script.Single(s => s.Cast == Slam).Every);
        var events = Play(1, script, table);
        var refused = NotReady(events);
        Assert.True(refused.Count == 0, $"{refused.Count} casts refused not_ready, first: {refused.FirstOrDefault()}");
        var gaps = SlamGaps(events);
        Assert.True(gaps.Count >= 25, $"only {gaps.Count + 1} slams");
        Assert.All(gaps, g => Assert.InRange(g, 100, 101));
    }
}
