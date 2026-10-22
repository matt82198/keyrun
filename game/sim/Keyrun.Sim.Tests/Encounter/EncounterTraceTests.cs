using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// The encounter lane alone, without the combat lanes, reproduces the encounter events of the full C# run root over each
/// oracle trace's inputs (ORACLE.md section 10). The run replayed is the C# one (<see cref="Keyrun.Tools.Replay.TraceRecorder"/>:
/// the trace's setup, seed and recorded caller commands through RunRoot), never the TS recording, so a rules change
/// moves both sides together and no fixture is re-recorded; what each trace must show is asserted as its named
/// properties (TraceReplayTests, RolesOracleTraceTests). Every record of the C# run carries its full projection, so every
/// tick is exact.
///
/// sim/index.ts:137 stepRun calls stepEncounter (:148) on run.state (tick t-1) BEFORE combat, so every encounter
/// event in record t is a pure function of state t-1: the scripts (scripts.ts), the telegraph registry
/// (index.ts:119-135), pull advance (pulls.ts:82) and, after combat, pull_start (sim/index.ts:166-169) and Slack
/// (run-wiring.ts:124-139, modelled here with this lane's Timer). The model replays the trace's own deaths and
/// pull spawns, so no combat code is needed. Where record t-1 carries a full projection the state is exact and the
/// events are compared byte for byte (canonical JSON); between projections positions are unknown, so a
/// telegraph's origin/yaw and a hit's actor list are left out of the comparison and everything else
/// (tick, caster, cast, shape, lead, resolvesTick, geometry, telegraph ids, pull events) is still exact.
/// HOLDER-LOCK (STATE.md D30): enemy holders are not projected, so the model replays them as the run root makes them:
/// each completed taunt in the record (its cast command's target) moves that enemy's holder, then
/// <see cref="Keyrun.Sim.Combat.Holder.LockHolders"/> runs on the post-step state (after the projection sync when the
/// record carries one), exactly where RunWiring.FixupTargeting runs it.
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

    // Combat/Holder.cs IsTaunt over the setup's raw ability JSON: target shape, the "utility" tag and a threat op.
    private static HashSet<string> TauntIds(JsObject abilities)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, value) in abilities.Members)
        {
            var def = (JsObject)value;
            var target = def.Get("shape") is JsObject shape && shape.Get("kind") is JsString { Value: "target" };
            var utility = def.Get("tags") is JsArray tags && tags.Items.Any(t => t is JsString { Value: Keyrun.Sim.Combat.Holder.TauntTag });
            var threat = def.Get("effects") is JsArray ops && ops.Items.Any(o => o is JsObject op && op.Get("op") is JsString { Value: "threat" });
            if (target && utility && threat)
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    // The run root's holder bookkeeping for one recorded step: completed taunts (combat), then LockHolders.
    private static void ApplyHolders(SimState state, OracleRecord r, HashSet<string> taunts)
    {
        foreach (var done in r.Events.Where(e => e.Type == "cast_complete"))
        {
            var casterId = (int)((JsInt)done.Get("actorId")!).Value;
            var abilityId = ((JsString)done.Get("abilityId")!).Value;
            if (!taunts.Contains(abilityId))
            {
                continue;
            }
            var cmd = r.Commands.OfType<CastCommand>().LastOrDefault(c => c.ActorId == casterId && c.AbilityId == abilityId);
            if (cmd?.TargetId is { } tid && state.Actors.TryGetValue(tid, out var target) && state.Actors.TryGetValue(casterId, out var caster))
            {
                Keyrun.Sim.Combat.Holder.ApplyTaunt(caster, target);
            }
        }
        Keyrun.Sim.Combat.Holder.LockHolders(state);
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
        var records = OracleRun.RecordsOf(Keyrun.Tools.Replay.TraceRecorder.RecordFile(OracleFixtures.PathOf(trace + ".trace.jsonl")));
        var tally = new Tally();

        // ... and each enemy's cast in flight on every tick, not only on the full lines (this replay runs no combat):
        // EncounterCastLedger learns completed casts from the oracle's cast_complete, cast starts from this replay's own
        // encounter step (the trace has no cast_start) and cuts from the oracle's interrupt (R-415-C); a start its
        // cooldown refuses is not a start (f-df833cf3), so the ledger also needs each ability's cooldownTicks. R-HASTE-1:
        // haste from every live effect shortens a cast (Casting.HastedCastTicks), so each cast's length comes from the
        // caster's live haste as the trace's own combo events apply it (EncounterHasteModel).
        var castTicks = abilities.ToDictionary(kv => kv.Key, kv => kv.Value.CastTicks, StringComparer.Ordinal);
        var haste = new EncounterHasteModel(content, records);
        var castLedger = new EncounterCastLedger(
            castTicks,
            abilities.Keys.ToDictionary(
                id => id,
                id => abilityJson.Get(id) is JsObject ao && ao.Get("cooldownTicks") is JsInt cd ? (int)cd.Value : 0,
                StringComparer.Ordinal),
            (actor, ability, start) => haste.CastTicksAt(actor, castTicks.TryGetValue(ability, out var c) ? c : 0, start));
        foreach (var rec in records)
        {
            foreach (var e in rec.Events.Where(e => e.Type == "cast_complete"))
            {
                castLedger.Completed((int)((JsInt)e.Get("actorId")!).Value, ((JsString)e.Get("abilityId")!).Value, e.Tick);
            }
            // f-fa598b6b: a cast combat refused out of range never started (cast_refused, enemies only).
            foreach (var e in rec.Events.Where(e => e.Type == "cast_refused"))
            {
                castLedger.Refused((int)((JsInt)e.Get("actorId")!).Value, ((JsString)e.Get("abilityId")!).Value, e.Tick);
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
        var taunts = TauntIds((JsObject)content.Get("abilities")!);
        Keyrun.Sim.Combat.Holder.LockHolders(state); // createRun's fixupTargeting

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

            // A telegraph drawn between projections took its origin/yaw from unknown (stale) positions, and only those two
            // fields went uncompared above; its caster can move before it resolves (the Seed walks while it casts), so a
            // resolution on an exact tick would hit-test our stale geometry against an exact state. Register the oracle's
            // own origin/yaw instead (every other field already matched), so the exact-tick hit list stays a real check.
            if (!exact)
            {
                var oracleGeometry = expected.Where(e => e.Type == "telegraph").ToList();
                var firstNewId = encounter.NextTelegraphId - oracleGeometry.Count; // this step's ids, in draw order
                encounter = encounter with
                {
                    Telegraphs = encounter.Telegraphs.Select(tg =>
                    {
                        var j = tg.Id - firstNewId;
                        if (j < 0 || j >= oracleGeometry.Count)
                        {
                            return tg;
                        }
                        var o = (JsObject)oracleGeometry[j].Get("origin")!;
                        return tg with
                        {
                            Origin = new Vec3(((JsInt)o.Get("x")!).Value, ((JsInt)o.Get("y")!).Value, ((JsInt)o.Get("z")!).Value),
                            Yaw = (int)((JsInt)oracleGeometry[j].Get("yaw")!).Value,
                        };
                    }).ToList(),
                };
            }

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
            ApplyHolders(state, r, taunts);
        }

        _out.WriteLine($"{trace}: records={tally.Records} telegraphs={tally.Telegraphs} (exact {tally.ExactTelegraphs}) " +
            $"hits={tally.Hits} (exact {tally.ExactHits}) pullEvents={tally.PullEvents} onDeathCasts={tally.OnDeathCasts} " +
            $"depleted={tally.Depleted} projections={tally.Projections}");
        return tally;
    }

    // No pinned per-trace counts: each count was a property of one TS recording and moved on every re-record. The replay
    // above already compares every encounter event of every tick with the full C# run's; these guards only make a
    // vacuous pass visible. What each trace must show (the scripted schedule, the pull order, the Seed's one deathBloom)
    // is stated once, as named properties in manifest.json.

    [Fact]
    public void Scripted_pull_telegraph_schedule_and_hits_match_the_csharp_run_every_tick()
    {
        var t = Replay("scripted-pull");
        Assert.Equal(600, t.Records);
        Assert.True(t.Telegraphs > 0 && t.Hits > 0, "the scripted cleave drew and resolved nothing");
        Assert.Equal(t.Telegraphs, t.ExactTelegraphs); // every tick is exact: origin, yaw and hit lists compared byte for byte
        Assert.Equal(t.Hits, t.ExactHits);
    }

    [Fact]
    public void Content_pull_emits_no_encounter_events_like_the_csharp_run()
    {
        var t = Replay("content-pull");
        Assert.Equal(400, t.Records);
        Assert.Equal(0, t.Telegraphs + t.Hits + t.PullEvents);
    }

    [Theory]
    [InlineData("bots-hv-k2-s7")]
    [InlineData("bots-hv-k2-s11")]
    [InlineData("bots-hv-k10-s7")]
    public void Hollow_vault_warden_script_pulls_seed_and_slack_match_the_csharp_run_every_tick(string trace)
    {
        var t = Replay(trace);
        Assert.True(t.Records > 0 && t.Projections == t.Records + 1, $"{trace}: every record must be synced from its full line");
        Assert.True(t.Telegraphs > 0 && t.Hits > 0, $"{trace}: no script telegraph was drawn and resolved");
        Assert.True(t.PullEvents > 0, $"{trace}: no pull advanced");
        Assert.Equal(1, t.OnDeathCasts); // the Seed's deathBloom, exactly once
        Assert.Equal(t.Telegraphs, t.ExactTelegraphs);
        Assert.Equal(t.Hits, t.ExactHits);
    }
}
