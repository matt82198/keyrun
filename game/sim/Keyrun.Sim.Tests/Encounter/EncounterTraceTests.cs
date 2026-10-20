using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// Oracle parity of the encounter lane against the frozen TS traces (ORACLE.md), without the combat lanes.
///
/// sim/index.ts:137 stepRun calls stepEncounter (:148) on run.state (tick t-1) BEFORE combat, so every encounter
/// event in record t is a pure function of state t-1: the scripts (scripts.ts), the telegraph registry
/// (index.ts:119-135), pull advance (pulls.ts:82) and, after combat, pull_start (sim/index.ts:166-169) and Slack
/// (run-wiring.ts:124-139, modelled here with this lane's Timer). The model replays the trace's own deaths and
/// pull spawns, so no combat code is needed. Where record t-1 carries a full projection the state is exact and the
/// events are compared byte for byte (canonical JSON); between projections positions are unknown, so a
/// telegraph's origin/yaw and a hit's actor list are left out of the comparison and everything else
/// (tick, caster, cast, shape, lead, resolvesTick, geometry, telegraph ids, pull events) is still exact.
/// </summary>
public sealed class EncounterTraceTests
{
    private static readonly HashSet<string> EncounterTypes = new(StringComparer.Ordinal)
    {
        "telegraph", "telegraph_hit", "pull_clear", "pull_start", "stagger", "echo_spawn", "depleted",
    };

    private readonly ITestOutputHelper _out;

    public EncounterTraceTests(ITestOutputHelper output) => _out = output;

    /// <summary>Counts of what one replay compared, so a vacuous pass is visible and assertable.</summary>
    private sealed class Tally
    {
        public int Records;
        public int Telegraphs;
        public int ExactTelegraphs;
        public int Hits;
        public int ExactHits;
        public int PullEvents;
        public int Projections;
        public int OnDeathCasts;
        public int Depleted;
    }

    private static string Normalise(SimEvent e, bool exact)
    {
        if (exact)
        {
            return e.ToCanonicalJson();
        }
        var drop = e.Type switch
        {
            "telegraph" => new[] { "origin", "yaw" },
            "telegraph_hit" => new[] { "actorIds" },
            _ => Array.Empty<string>(),
        };
        return new SimEvent(e.Tick, e.Type, e.Fields.Members.Where(m => !drop.Contains(m.Key))).ToCanonicalJson();
    }

    private Tally Replay(string trace)
    {
        var setup = EncounterTestKit.Setup(OracleRun.SetupNameOf(trace));
        var content = (JsObject)setup.Get("content")!;
        var dungeon = EncounterJson.ParseDungeon(setup.Get("dungeon")!);
        var scripts = EncounterJson.ParseScripts(content.Get("enemyScripts")!);
        var abilities = EncounterJson.ParseAbilities(content.Get("abilities")!);
        var keyLevel = OracleRun.KeyLevel(setup);
        var records = OracleRun.Records(trace);
        var tally = new Tally();

        var state = new SimState { Tick = 0, Seed = 1, Prng = new PrngState(1, 2, 3, 4), Phase = "combat" };
        var party = OracleRun.Party(setup);
        foreach (var a in party)
        {
            state.Actors[a.Id] = a;
        }
        foreach (var a in Pulls.SpawnPull(dungeon, 0, 0, _ => { }))
        {
            state.Actors[a.Id] = a;
        }
        var encounter = EncounterState.Create(keyLevel, OracleRun.ParTicks(setup), Clauses.DefaultEncounterRules(keyLevel));
        var noTags = new Dictionary<string, IReadOnlyList<string>?>();

        void Sync(OracleRecord r)
        {
            var line = OracleRun.Parse(r.P!);
            Assert.Equal(r.T, line.T);
            Assert.True(line.Pull == encounter.PullIndex, $"{trace} t={r.T}: pull {encounter.PullIndex}, oracle {line.Pull}");
            Assert.True(line.Slack == encounter.SlackTicks, $"{trace} t={r.T}: slack {encounter.SlackTicks}, oracle {line.Slack}");
            Assert.Equal(line.Actors.Select(a => a.Id), state.Actors.Keys);
            foreach (var pa in line.Actors)
            {
                var a = state.Actors[pa.Id];
                Assert.True(a.Alive == pa.Alive, $"{trace} t={r.T}: actor {pa.Id} alive {a.Alive}, oracle {pa.Alive}");
                a.Pos = new Vec3(pa.X, a.Pos.Y, pa.Z);
                a.Yaw = pa.Yaw;
                a.Health = pa.Hp;
                a.Resource = pa.Res;
                a.CastId = pa.Cast;
                a.CastEndsTick = pa.CastEnd;
            }
            tally.Projections++;
        }

        Sync(records[0]);
        for (var i = 1; i < records.Count; i++)
        {
            var r = records[i];
            var exact = records[i - 1].P is not null;
            state.Tick = r.T - 1;
            state.RunTimerTicks = r.T - 1;

            var ours = new List<SimEvent>();
            var step = EncounterStep.Step(state, encounter, dungeon, scripts, ours.Add,
                new BreakBarWiring(Array.Empty<BreakBarSource>(), r.Commands, noTags), abilities);
            var spawned = step.Encounter.PullIndex != encounter.PullIndex
                ? Pulls.SpawnPull(dungeon, step.Encounter.PullIndex, r.T, ours.Add)
                : Array.Empty<ActorState>();
            encounter = step.Encounter;

            // onDeath casts (castTicks 0, e.g. seed.deathBloom) complete in this same step: combat's cast_complete.
            foreach (var cast in step.Commands.OfType<CastCommand>().Where(c => !state.Actors[c.ActorId].Alive))
            {
                Assert.Contains(r.Events, e => e.Type == "cast_complete" && e.Tick == cast.Tick &&
                    e.Get("actorId") is JsInt { } id && id.Value == cast.ActorId &&
                    e.Get("abilityId") is JsString { } ab && ab.Value == cast.AbilityId);
                tally.OnDeathCasts++;
            }

            // Apply the oracle's own outcome of this step: deaths, then the pull spawn, then Slack.
            var playerDeath = false;
            foreach (var e in r.Events.Where(e => e.Type == "death"))
            {
                var victim = state.Actors[(int)((JsInt)e.Get("actorId")!).Value];
                victim.Alive = false;
                playerDeath |= victim.Kind == ActorKind.Player;
            }
            foreach (var a in spawned)
            {
                state.Actors[a.Id] = a;
            }
            if (playerDeath)
            {
                encounter = Timer.SpendSlackOnDeath(encounter, r.T, ours.Add);
                if (state.Actors.Values.Where(a => a.Kind == ActorKind.Player).All(a => !a.Alive))
                {
                    encounter = Timer.SpendSlackOnWipe(encounter, r.T, ours.Add);
                }
            }

            var expected = r.Events.Where(e => EncounterTypes.Contains(e.Type)).ToList();
            var expectedJson = expected.Select(e => Normalise(e, exact)).ToList();
            var oursJson = ours.Select(e => Normalise(e, exact)).ToList();
            Assert.True(
                expectedJson.SequenceEqual(oursJson),
                $"{trace}: first divergence at t={r.T} (state t={r.T - 1}, exact={exact}){Environment.NewLine}" +
                $"oracle: {string.Join(" ", expectedJson)}{Environment.NewLine}ours:   {string.Join(" ", oursJson)}");

            tally.Records++;
            tally.Telegraphs += expected.Count(e => e.Type == "telegraph");
            tally.ExactTelegraphs += exact ? expected.Count(e => e.Type == "telegraph") : 0;
            tally.Hits += expected.Count(e => e.Type == "telegraph_hit");
            tally.ExactHits += exact ? expected.Count(e => e.Type == "telegraph_hit") : 0;
            tally.PullEvents += expected.Count(e => e.Type is "pull_clear" or "pull_start");
            tally.Depleted += expected.Count(e => e.Type == "depleted");

            if (r.P is not null)
            {
                Sync(r);
            }
        }

        _out.WriteLine($"{trace}: records={tally.Records} telegraphs={tally.Telegraphs} (exact {tally.ExactTelegraphs}) " +
            $"hits={tally.Hits} (exact {tally.ExactHits}) pullEvents={tally.PullEvents} onDeathCasts={tally.OnDeathCasts} " +
            $"depleted={tally.Depleted} projections={tally.Projections}");
        return tally;
    }

    [Fact]
    public void Scripted_pull_telegraph_schedule_and_hits_match_the_oracle_every_tick()
    {
        var t = Replay("scripted-pull");
        Assert.Equal(600, t.Records);
        Assert.Equal(6, t.Telegraphs);
        Assert.Equal(5, t.Hits);
        Assert.Equal(6, t.ExactTelegraphs); // telegraphs at 60, 160, ... sit on projection ticks
    }

    [Fact]
    public void Content_pull_emits_no_encounter_events_like_the_oracle()
    {
        var t = Replay("content-pull");
        Assert.Equal(400, t.Records);
        Assert.Equal(0, t.Telegraphs + t.Hits + t.PullEvents);
    }

    // exactTelegraphs / exactHits: how many fell right after a projection tick, where origin, yaw and the hit list
    // are compared byte for byte. Since the 2026-10-18 content tuning (STATE.md D14) the bot traces clear the dungeon
    // and neither key has a telegraph on a projection tick (origins are covered by scripted-pull); each of their
    // 9 / 13 is still compared on tick, caster, cast, shape, lead, resolvesTick and geometry (SLAM-TELEGRAPH B1 added
    // one slam telegraph to each trace: 8 / 12 before; on k2 that telegraph lands right after a projection tick, so
    // k2 now has one exact, byte-for-byte telegraph, where it had none). KF3-SEED-ENGAGE: the Seed now walks to the party
    // and casts seed.lash; the re-recorded traces carry 11 / 14 telegraphs (was 9 / 13), k2 10 hits (was 9), k10 14 hits
    // (was 13) with 3 exact (was 2), measured by this replay against the regenerated oracle.
    [Theory]
    // SLAM-TELEGRAPH Pair leadTicks 140 re-record: k2 was 13, 12, 7, 0, 0, 1 and k10 was 14, 14, 7, 0, 0, 3; the new
    // values are tallied from the re-recorded TS traces with this replay's rules (the same tally reproduces the old ones).
    // Merged with QUENCH-REFUND (main: k2 13, 12, 7, 0, 0, 1; k10 14, 13, 7, 0, 0, 2) and re-recorded on the merged
    // tree: the same tally of the merged traces gives the SLAM-TELEGRAPH rows below unchanged (it reproduces both
    // parents' rows from their own traces).
    // BOTS-FIVE-BUTTONS merged with that main and re-recorded on the merged tree; the same tally (it reproduces
    // BOTS-FIVE-BUTTONS' own rows k2 9, 9, 7, 0, 1, 1 / k10 14, 14, 7, 0, 0, 0 from its traces) gives the rows below.
    [InlineData("bots-hv-k2-s7", 9, 9, 7, 0, 1, 1)] // BOTS-FIVE-BUTTONS merge re-record: main was 12, 12, 7, 0, 1, 1 (run ends t1607). DEAD-CASTER-TELEGRAPH: was 11, 10, 7, 0, 1, 1
    [InlineData("bots-hv-k2-s11", 9, 9, 7, 0, 1, 1)] // BOTS-FIVE-BUTTONS merge re-record: main was 12, 12, 7, 0, 1, 1. DEAD-CASTER-TELEGRAPH: was 11, 10, 7, 0, 1, 1
    [InlineData("bots-hv-k10-s7", 15, 15, 7, 0, 0, 0)] // BOTS-FIVE-BUTTONS merge re-record: main was 16, 16, 7, 0, 0, 3 (run ends t1945)
    public void Hollow_vault_warden_script_pulls_seed_and_slack_match_the_oracle_every_tick(
        string trace, int telegraphs, int hits, int pullEvents, int depleted, int exactTelegraphs, int exactHits)
    {
        var t = Replay(trace);
        Assert.Equal(telegraphs, t.Telegraphs);
        Assert.Equal(hits, t.Hits);
        Assert.Equal(pullEvents, t.PullEvents);
        Assert.Equal(depleted, t.Depleted);
        Assert.Equal(1, t.OnDeathCasts); // the Seed's deathBloom, exactly once
        Assert.Equal(exactTelegraphs, t.ExactTelegraphs);
        Assert.Equal(exactHits, t.ExactHits);
    }
}
