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
        // A frozen-TS replay, as RunSetup loads one: the TS has no spansCast rule and drew every scheduled telegraph
        // (TelegraphSpec.TsUnchecked), and fired every step on schedule, holding it while its caster was busy
        // (ScriptStep.TsCastReplace, CastDeferral).
        var scripts = EncounterJson.ParseScripts(content.Get("enemyScripts")!).ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<ScriptStep>)kv.Value
                .Select(s => s.Telegraph is { } tg ? s with { Telegraph = tg with { SpansCast = false, TsUnchecked = true } } : s)
                .Select(s => s with { TsCastReplace = true }).ToList());
        // STARTCAST-BUSY (#325): a step due while its caster is busy is held until the cast lands, so the replay needs each
        // ability's cast time (a cast-time cast claims its caster for the rest of the tick) ...
        var abilityJson = (JsObject)content.Get("abilities")!;
        var abilities = EncounterJson.ParseAbilities(abilityJson).ToDictionary(
            kv => kv.Key,
            kv => kv.Value with { CastTicks = abilityJson.Get(kv.Key) is JsObject ao && ao.Get("castTicks") is JsInt ct ? (int)ct.Value : 0 },
            StringComparer.Ordinal);
        var keyLevel = OracleRun.KeyLevel(setup);
        var records = OracleRun.Records(trace);
        var tally = new Tally();

        // ... and each enemy's cast in flight on every tick, not only on the full lines (this replay runs no combat):
        // EncounterCastLedger learns completed casts from the oracle's cast_complete, cast starts from this replay's own
        // encounter step (the trace has no cast_start) and cuts from the oracle's interrupt (R-415-C); a start its
        // cooldown refuses is not a start (f-df833cf3), so the ledger also needs each ability's cooldownTicks.
        var castLedger = new EncounterCastLedger(
            abilities.ToDictionary(kv => kv.Key, kv => kv.Value.CastTicks, StringComparer.Ordinal),
            abilities.Keys.ToDictionary(
                id => id,
                id => abilityJson.Get(id) is JsObject ao && ao.Get("cooldownTicks") is JsInt cd ? (int)cd.Value : 0,
                StringComparer.Ordinal));
        foreach (var rec in records)
        {
            foreach (var e in rec.Events.Where(e => e.Type == "cast_complete"))
            {
                castLedger.Completed((int)((JsInt)e.Get("actorId")!).Value, ((JsString)e.Get("abilityId")!).Value, e.Tick);
            }
        }

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
            castLedger.Apply(state, state.Tick);

            var ours = new List<SimEvent>();
            var step = EncounterStep.Step(state, encounter, dungeon, scripts, ours.Add,
                new BreakBarWiring(Array.Empty<BreakBarSource>(), r.Commands, noTags), abilities);
            var spawned = step.Encounter.PullIndex != encounter.PullIndex
                ? Pulls.SpawnPull(dungeon, step.Encounter.PullIndex, r.T, ours.Add)
                : Array.Empty<ActorState>();
            encounter = step.Encounter;

            // The cast starts this step issued to living enemies, then the oracle's interrupts of this step (R-415-C).
            foreach (var cast in step.Commands.OfType<CastCommand>())
            {
                if (state.Actors.TryGetValue(cast.ActorId, out var caster) && caster.Kind == ActorKind.Enemy && caster.Alive)
                {
                    castLedger.Started(cast.ActorId, cast.AbilityId, cast.Tick);
                }
            }
            foreach (var e in r.Events.Where(e => e.Type == "interrupt"))
            {
                if (e.Get("targetId") is JsInt victim)
                {
                    castLedger.Interrupted((int)victim.Value, e.Tick);
                }
            }

            // onDeath casts (castTicks 0, e.g. seed.deathBloom) complete in this same step: combat's cast_complete.
            foreach (var cast in step.Commands.OfType<CastCommand>().Where(c => !state.Actors[c.ActorId].Alive))
            {
                Assert.Contains(r.Events, e => e.Type == "cast_complete" && e.Tick == cast.Tick &&
                    e.Get("actorId") is JsInt { } id && id.Value == cast.ActorId &&
                    e.Get("abilityId") is JsString { } ab && ab.Value == cast.AbilityId);
                tally.OnDeathCasts++;
            }

            // Apply the oracle's own outcome of this step: deaths, then the pull spawn, then death and wipe time (D46).
            var playerDeaths = new HashSet<int>();
            foreach (var e in r.Events.Where(e => e.Type == "death"))
            {
                var victim = state.Actors[(int)((JsInt)e.Get("actorId")!).Value];
                victim.Alive = false;
                if (victim.Kind == ActorKind.Player)
                {
                    playerDeaths.Add(victim.Id);
                }
            }
            foreach (var a in spawned)
            {
                state.Actors[a.Id] = a;
            }
            if (playerDeaths.Count > 0)
            {
                foreach (var _ in playerDeaths)
                {
                    encounter = Timer.ChargeDeath(encounter);
                }
                if (state.Actors.Values.Where(a => a.Kind == ActorKind.Player).All(a => !a.Alive))
                {
                    encounter = Timer.ChargeWipe(encounter);
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
    // ENEMY-ENGAGE (in-reach chasers turn to face their target) merged with SLAM-TELEGRAPH and re-recorded on the merged
    // tree: the same tally gives the SLAM-TELEGRAPH rows unchanged (ENEMY-ENGAGE alone: k2 12, 12, 7, 0, 0, 1; k10
    // 14, 14, 7, 0, 0, 3; the tally reproduces both parents' committed rows from their own traces).
    // ZERO-GCD + NO-PRESS-MID-CAST (f-80b5cf16) merged with ENEMY-ENGAGE and re-recorded on the merged tree, measured by
    // this replay (ZERO-GCD + NO-PRESS-MID-CAST alone: k2 10, 10, 7, 0, 0, 0; k10 15, 14, 7, 0, 0, 3; main: k2 12, 12,
    // 7, 0, 1, 1; k10 16, 16, 7, 0, 0, 3).
    // SPELL-QUEUE (PR #370: bots press in the last 8 ticks of their own cast; Combat/SpellQueue.cs) re-recorded, measured
    // by this replay: k2 was 10, 10, 7, 0, 0, 0 (now 8, 8, 7, 0, 1, 1); k10 unchanged.
    // PR #370 merged with INTEGRATE-PLAYABLE (#396) and re-recorded on the merged tree (ORACLE_WRITE=1), measured by
    // this replay: the k2 traces are byte-identical to PR #370's own, so the k2 rows are PR #370's (main alone: k2 12,
    // 12, 7, 0, 1, 1); k10 PR #370 alone 15, 15, 7, 0, 0, 3, main alone 19, 18, 7, 0, 0, 2.
    [InlineData("bots-hv-k2-s7", 7, 7, 7, 0, 0, 1)] // CADENCE-SETUP (f-b31e8f34) re-record: was 8, 8, 7, 0, 1, 1 (measured by this replay: telegraphs=7 (exact 0) hits=7 (exact 1)). DEAD-CASTER-TELEGRAPH re-record: was 11, 10, 7, 0, 1, 1 (measured by this replay)
    [InlineData("bots-hv-k2-s11", 7, 7, 7, 0, 0, 1)] // CADENCE-SETUP (f-b31e8f34) re-record: was 8, 8, 7, 0, 1, 1 (measured by this replay: telegraphs=7 (exact 0) hits=7 (exact 1)). DEAD-CASTER-TELEGRAPH re-record: was 11, 10, 7, 0, 1, 1 (measured by this replay)
    [InlineData("bots-hv-k10-s7", 13, 12, 7, 0, 0, 2)] // CADENCE-SETUP (f-b31e8f34) re-record, R-415-C: was 15, 15, 7, 0, 0, 3 (measured by this replay on the aa65cde8 trace once EncounterCastLedger tracks the slam begun at 2065: telegraphs=13 (exact 0) hits=12 (exact 2); the trace holds 13 telegraph and 12 telegraph_hit events; the run ends at 2074). MERGE-370 re-record: was 19, 18, 7, 0, 0, 2 on main (measured by this replay on the merged trace: telegraphs=15 hits=15 exact 0 / 3; the same as PR #370 alone). INTEGRATE-PLAYABLE CAST-DEFERRAL re-record: was 16, 16, 7, 0, 0, 2 (measured by this replay: telegraphs=19 (exact 0) hits=18 (exact 2); held boss casts re-draw their telegraph when issued). INTEGRATE-PLAYABLE #325 re-fold re-record: was 17, 17, 7, 0, 0, 3 (measured by this replay: telegraphs=16 (exact 0) hits=16 (exact 2); the run now ends at 2071, before the 2126 cleave). INTEGRATE-PLAYABLE re-record (integrated tree, e19bc046 traces): was 16, 15, 7, 0, 0, 2, tallied by this replay from the recorded trace; SLAM-CIRCLE-HITS re-record (slam circle + CHASE-AREA-REACH): was 16, 16, 7, 0, 0, 3 (measured by this replay against the regenerated oracle); k2 rows unchanged
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
