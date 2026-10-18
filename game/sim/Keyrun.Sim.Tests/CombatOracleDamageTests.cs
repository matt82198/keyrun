using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Damage parity against the frozen TS oracle traces (game/sim/ORACLE.md section 3), ahead of the full replay
/// (lane 2D). In the TS sim, health changes ONLY in sim/combat/damage.ts (applyDamage :61, applyHeal :87) and
/// alive flips only there (:63) or in actors.ts reviveActor, so a per-actor hp ledger seeded from each full
/// projection and advanced by the C# port over every damage and heal event must land exactly on the next full
/// projection. For each traced damage event the port's absorb/health/death stage (damage.ts:57-75) runs on the
/// ledger actor with the trace's raw and modified amounts, and its emitted events must be byte-equal to the
/// trace's canonical JSON, including the death event that must follow immediately. Where raw == modified the
/// whole ApplyDamage pipeline (bp 0, no Pact) runs too and must agree. Heals run through ApplyHeal. A corpse
/// that the trace still damaged or healed would make the port emit nothing and fail the comparison.
/// telegraph_hit payload actors damaged in the same record are counted (their damage is checked like any other).
/// </summary>
public sealed class CombatOracleDamageTests
{
    private readonly ITestOutputHelper _out;

    /// <summary>xUnit output sink for the coverage line.</summary>
    public CombatOracleDamageTests(ITestOutputHelper output) => _out = output;

    /// <summary>The five traces, by name.</summary>
    public static IEnumerable<object[]> Traces => OracleFixtures.TraceNames.Select(n => new object[] { n });

    private static readonly IReadOnlyDictionary<string, EffectDef> NoDefs = new Dictionary<string, EffectDef>();

    private static Dictionary<int, (long Hp, bool Alive)> ParseProjection(string p)
    {
        var result = new Dictionary<int, (long, bool)>();
        var a = p[(p.IndexOf(";a=", System.StringComparison.Ordinal) + 3)..];
        if (a.Length == 0)
        {
            return result;
        }
        foreach (var group in a.Split('|'))
        {
            var f = group.Split(',');
            result.Add(int.Parse(f[0], CultureInfo.InvariantCulture), (long.Parse(f[1], CultureInfo.InvariantCulture), f[5] == "1"));
        }
        return result;
    }

    private static ActorState LedgerActor(int id, long hp, bool alive) => new()
    {
        Id = id,
        Health = hp,
        MaxHealth = long.MaxValue / 4,
        Alive = alive,
    };

    private static long IntField(JsonElement e, string key) => e.GetProperty(key).GetInt64();

    [Theory]
    [MemberData(nameof(Traces))]
    public void Every_traced_damage_heal_and_death_matches_the_port_and_the_hp_ledger(string name)
    {
        var trace = OracleFixtures.LoadTrace(name);
        var ledger = new Dictionary<int, ActorState>();
        int damages = 0, deaths = 0, heals = 0, untrackedDamage = 0, untrackedHeals = 0, fullPipeline = 0, telegraphHits = 0, snapshots = 0;
        var totalDamage = 0;

        foreach (var line in trace.RecordLines)
        {
            using var doc = JsonDocument.Parse(line);
            var rec = doc.RootElement;
            var t = rec.GetProperty("t").GetInt32();
            if (rec.TryGetProperty("e", out var evs))
            {
                var list = evs.EnumerateArray().ToList();
                var damagedThisRecord = new HashSet<int>(list.Where(x => x.GetProperty("type").GetString() == "damage").Select(x => (int)IntField(x, "targetId")));
                for (var i = 0; i < list.Count; i++)
                {
                    var ev = list[i];
                    var type = ev.GetProperty("type").GetString();
                    var tick = (int)IntField(ev, "tick");
                    if (type is "damage" or "death" or "heal")
                    {
                        // Combat events carry the pre-step tick (ORACLE.md section 3). Encounter events such as
                        // pull_start are stamped with the post-step tick, so they are not checked here.
                        Assert.Equal(t - 1, tick);
                    }
                    if (type == "damage")
                    {
                        totalDamage++;
                        var targetId = (int)IntField(ev, "targetId");
                        if (!ledger.TryGetValue(targetId, out var target))
                        {
                            untrackedDamage++;
                            if (i + 1 < list.Count && list[i + 1].GetProperty("type").GetString() == "death" && IntField(list[i + 1], "actorId") == targetId)
                            {
                                i++;
                            }
                            continue;
                        }
                        var raw = IntField(ev, "rawAmount");
                        var modified = IntField(ev, "modifiedAmount");
                        var sourceId = (int)IntField(ev, "sourceId");
                        var probe = LedgerActor(targetId, target.Health, target.Alive);
                        var emitted = new List<SimEvent>();
                        Damage.ResolveModified(tick, target, raw, modified, NoDefs, emitted.Add, sourceId);
                        Assert.True(emitted.Count >= 1, $"{name} t={t}: the port emitted nothing for {ev.GetRawText()} (target hp {probe.Health} alive {probe.Alive})");
                        Assert.Equal(ev.GetRawText(), emitted[0].ToCanonicalJson());
                        damages++;
                        if (emitted.Count == 2)
                        {
                            Assert.True(i + 1 < list.Count, $"{name} t={t}: port emitted a death the trace lacks");
                            Assert.Equal(list[i + 1].GetRawText(), emitted[1].ToCanonicalJson());
                            deaths++;
                            i++;
                        }
                        if (raw == modified)
                        {
                            var whole = new List<SimEvent>();
                            Damage.ApplyDamage(tick, probe, raw, 0, 0, NoDefs, whole.Add, sourceId);
                            Assert.Equal(emitted.Select(x => x.ToCanonicalJson()), whole.Select(x => x.ToCanonicalJson()));
                            fullPipeline++;
                        }
                    }
                    else if (type == "death")
                    {
                        var actorId = (int)IntField(ev, "actorId");
                        Assert.False(ledger.ContainsKey(actorId), $"{name} t={t}: death of tracked actor {actorId} not produced by the port's damage stage");
                    }
                    else if (type == "heal")
                    {
                        var targetId = (int)IntField(ev, "targetId");
                        if (!ledger.TryGetValue(targetId, out var target))
                        {
                            untrackedHeals++;
                            continue;
                        }
                        var emitted = new List<SimEvent>();
                        Damage.ApplyHeal(tick, target, IntField(ev, "amount"), (int)IntField(ev, "sourceId"), emitted.Add);
                        Assert.Equal(ev.GetRawText(), Assert.Single(emitted).ToCanonicalJson());
                        heals++;
                    }
                    else if (type == "telegraph_hit")
                    {
                        // telegraph_hit is who stood in the shape, not damage (sim/encounter/index.ts:7, :122):
                        // allies of the caster and no-damage calls appear too. The damage comes from the enemy
                        // cast's own target resolution; count the hit actors whose damage in this record the
                        // port resolves above.
                        telegraphHits += ev.GetProperty("actorIds").EnumerateArray()
                            .Count(idEl => ledger.ContainsKey(idEl.GetInt32()) && damagedThisRecord.Contains(idEl.GetInt32()));
                    }
                }
            }
            if (rec.TryGetProperty("p", out var pEl))
            {
                var proj = ParseProjection(pEl.GetString()!);
                foreach (var (id, actor) in ledger)
                {
                    Assert.True(proj.TryGetValue(id, out var want), $"{name} t={t}: actor {id} vanished from the projection");
                    Assert.True(want == (actor.Health, actor.Alive), $"{name} t={t}: actor {id} ledger hp={actor.Health} alive={actor.Alive}, projection hp={want.Hp} alive={want.Alive}");
                }
                ledger = proj.ToDictionary(kv => kv.Key, kv => LedgerActor(kv.Key, kv.Value.Hp, kv.Value.Alive));
                snapshots++;
            }
        }

        _out.WriteLine($"{name}: damage {damages}/{totalDamage} checked, untracked damage {untrackedDamage} heals {untrackedHeals}, deaths {deaths}, heals {heals}, whole-pipeline {fullPipeline}, telegraph hits {telegraphHits}, snapshots {snapshots}");
        Assert.True(damages > 0, "vacuous: no damage event checked");
        Assert.Equal(totalDamage, damages + untrackedDamage);
        Assert.True(untrackedDamage * 20 <= totalDamage, $"coverage below 95%: {untrackedDamage} of {totalDamage} damage events untracked");
    }
}
