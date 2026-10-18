using System;
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
/// Cast, Window and Combo parity against the frozen TS oracle traces (game/sim/ORACLE.md section 3), ahead of the
/// full replay (lane 2D). A ledger SimState holds every setup actor, the setup's abilities, effects and combo tables,
/// and the real <see cref="SimState.FloorWindows"/>. Each record is walked in emission order and every traced
/// cast/window/combo event is re-produced by the port's own code:
/// <list type="bullet">
/// <item>cast_complete: <see cref="Casting.CompleteCastOn"/> on the ledger caster (effects are 2A's ledger; this
/// checks the event bytes and that the ability exists in the setup).</item>
/// <item>window_opened, combo, combo_denied: <see cref="Windows.ApplyWindowsAndCombosTo"/> with the ability's
/// resolved targets, read from that cast's own damage/heal events (minus targets that died in it).</item>
/// <item>window_expired and lock_enrage: <see cref="Windows.CheckWindowExpiries"/> per actor ascending, then
/// <see cref="Windows.CheckFloorExpiries"/>, after the record's casts, exactly where rules.ts runs them.</item>
/// <item>interrupt and cast_stopped: <see cref="Casting.InterruptCast"/> / <see cref="Casting.Stopcast"/>.</item>
/// </list>
/// The ledger state (Window instances, Lock fail counters and alternation class, floor patches) is ONLY ever
/// changed by the port, so a wrong expiry, a missed consumption, a wrong denial or a wrong enrage count diverges
/// at the record where it happens. Geometry is NOT reproduced (positions are projected only every 20 ticks):
/// a floor Window's centre is read from its window_opened telegraph, a floor Payoff stands at the patch the trace
/// says it closed (or out of reach when the trace says nothing closed), and a target-only Window ability with no
/// damage or heal op takes its target from the trace's window_opened. Those inputs are counted in the output line.
/// Every full projection also checks each in-flight cast: StartCast at <c>castEnd - castTicks</c> must give that
/// castEnd, and the cast must end in a traced cast_complete, interrupt, stop or death.
/// </summary>
public sealed class CombatOracleCastTests
{
    private static readonly HashSet<string> Tracked = new(StringComparer.Ordinal)
    {
        "cast_complete", "cast_stopped", "interrupt", "window_opened", "window_expired", "combo", "combo_denied", "lock_enrage",
    };

    private static readonly Vec3 OutOfReach = new(1_000_000_000, 0, 1_000_000_000);

    private readonly ITestOutputHelper _out;

    /// <summary>xUnit output sink for the coverage table.</summary>
    public CombatOracleCastTests(ITestOutputHelper output) => _out = output;

    /// <summary>The five traces, by name.</summary>
    public static IEnumerable<object[]> Traces => OracleFixtures.TraceNames.Select(n => new object[] { n });

    private static long IntField(JsonElement e, string key) => e.GetProperty(key).GetInt64();

    private static string TypeOf(JsonElement e) => e.GetProperty("type").GetString()!;

    private static bool HasOp<T>(AbilityDef def) => def.Effects.Any(op => op is T);

    [Theory]
    [MemberData(nameof(Traces))]
    public void Every_traced_cast_window_and_combo_event_matches_the_port(string name)
    {
        var trace = OracleFixtures.LoadTrace(name);
        var setup = OracleSetupContent.Load(trace.Header.GetProperty("setup").GetString()!);
        var state = SimState.Create(0);
        foreach (var a in setup.Actors)
        {
            a.MaxHealth = long.MaxValue / 4;
            a.Health = a.MaxHealth;
            state.Actors[a.Id] = a;
        }
        var hooks = new ClauseHooks(null);
        var castCtx = new CastContext(setup.Abilities, setup.Effects, setup.LockoutTicks) { Hooks = hooks };
        var floor = new FloorWindowRegistry(state.FloorWindows);
        var wctx = new WindowsContext(setup.Effects, setup.Combos, setup.FieldCombos, floor, hooks);

        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var checkedCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int derivedTargets = 0, traceTargets = 0, floorCentres = 0, floorStands = 0, inFlight = 0, records = 0;
        var pendingCasts = new Dictionary<(int Actor, string Ability, int End), int>();
        var endedCasts = new HashSet<(int Actor, string Ability, int End)>();
        var castEnders = new Dictionary<int, List<int>>();
        var finalTick = 0;

        ActorState Ledger(int id)
        {
            if (!state.Actors.TryGetValue(id, out var a))
            {
                a = Actors.Create(id, ActorKind.Enemy, "untracked", ActorRole.None, Vec3.Zero, long.MaxValue / 4, 0);
                state.Actors[id] = a;
            }
            return a;
        }

        foreach (var line in trace.RecordLines)
        {
            using var doc = JsonDocument.Parse(line);
            var rec = doc.RootElement;
            var t = rec.GetProperty("t").GetInt32();
            finalTick = t;
            var events = rec.TryGetProperty("e", out var evs) ? evs.EnumerateArray().ToList() : new List<JsonElement>();
            var expected = events.Where(e => Tracked.Contains(TypeOf(e))).Select(e => e.GetRawText()).ToList();
            foreach (var e in events.Where(e => Tracked.Contains(TypeOf(e))))
            {
                totals[TypeOf(e)] = totals.GetValueOrDefault(TypeOf(e)) + 1;
            }
            var actual = new List<SimEvent>();
            if (t > 0)
            {
                records++;
                var tick = t - 1;
                state.Tick = tick;
                var blockStart = 0;
                for (var i = 0; i < events.Count; i++)
                {
                    var ev = events[i];
                    switch (TypeOf(ev))
                    {
                        case "death":
                            Ledger((int)IntField(ev, "actorId")).Alive = false;
                            AddEnder(castEnders, (int)IntField(ev, "actorId"), tick);
                            break;
                        case "interrupt":
                        {
                            var target = Ledger((int)IntField(ev, "targetId"));
                            target.CastId = ev.GetProperty("abilityId").GetString();
                            Casting.InterruptCast(state, target, (int)IntField(ev, "sourceId"), castCtx, actual.Add);
                            AddEnder(castEnders, target.Id, tick);
                            break;
                        }
                        case "cast_stopped":
                        {
                            var actor = Ledger((int)IntField(ev, "actorId"));
                            actor.CastId = ev.GetProperty("abilityId").GetString();
                            Casting.Stopcast(actor, actual.Add, tick);
                            AddEnder(castEnders, actor.Id, tick);
                            break;
                        }
                        case "cast_complete":
                        {
                            var caster = Ledger((int)IntField(ev, "actorId"));
                            var abilityId = ev.GetProperty("abilityId").GetString()!;
                            Assert.True(setup.Abilities.TryGetValue(abilityId, out var def), $"{name} t={t}: cast_complete of {abilityId}, which the setup does not define");
                            endedCasts.Add((caster.Id, abilityId, tick));
                            var targets = TargetsOf(events, blockStart, i, caster.Id).Select(Ledger).ToList();
                            caster.CastId = abilityId;
                            Casting.CompleteCastOn(state, caster, def!, new List<ActorState>(), castCtx, actual.Add);
                            var nextCast = events.FindIndex(i + 1, e => TypeOf(e) == "cast_complete");
                            var tail = events.Skip(i + 1).Take((nextCast < 0 ? events.Count : nextCast) - i - 1).ToList();
                            if (!HasOp<WindowOp>(def!) && !HasOp<PayoffOp>(def!))
                            {
                                blockStart = i + 1;
                                break;
                            }
                            if (targets.Count > 0)
                            {
                                derivedTargets++;
                            }
                            else if (def!.Effects.OfType<WindowOp>().FirstOrDefault(w => w.OnTarget && w.Self != true) is { } targetWindow)
                            {
                                var opened = tail.FirstOrDefault(e => TypeOf(e) == "window_opened" && IntField(e, "setupActorId") == caster.Id && e.GetProperty("windowId").GetString() == targetWindow.WindowId);
                                if (opened.ValueKind == JsonValueKind.Object && opened.TryGetProperty("targetId", out var tid))
                                {
                                    targets.Add(Ledger(tid.GetInt32()));
                                    traceTargets++;
                                }
                            }
                            foreach (var w in def!.Effects.OfType<WindowOp>().Where(w => !w.OnTarget))
                            {
                                var opened = tail.FirstOrDefault(e => TypeOf(e) == "window_opened" && IntField(e, "setupActorId") == caster.Id && e.GetProperty("windowId").GetString() == w.WindowId);
                                if (opened.ValueKind == JsonValueKind.Object)
                                {
                                    var c = opened.GetProperty("telegraph").GetProperty("center");
                                    caster.Pos = new Vec3(IntField(c, "x"), 0, IntField(c, "z"));
                                    floorCentres++;
                                }
                            }
                            if (HasOp<PayoffOp>(def!))
                            {
                                var closed = tail.FirstOrDefault(e =>
                                    e.TryGetProperty("payoffActorId", out var pa) && pa.GetInt64() == caster.Id &&
                                    ((TypeOf(e) == "combo" && e.TryGetProperty("field", out _)) || (TypeOf(e) == "combo_denied" && !e.TryGetProperty("targetId", out _))));
                                if (closed.ValueKind == JsonValueKind.Object && floor.Patches.TryGetValue(closed.GetProperty("windowId").GetString()!, out var patch))
                                {
                                    caster.Pos = patch.Pos;
                                    floorStands++;
                                }
                                else if (!HasOp<WindowOp>(def!))
                                {
                                    caster.Pos = OutOfReach;
                                }
                            }
                            var before = actual.Count;
                            Windows.ApplyWindowsAndCombosTo(state, caster, def!, targets, wctx, actual.Add);
                            var trackedEmitted = actual.Skip(before).Count(e => Tracked.Contains(e.Type));
                            var end = i;
                            for (var j = i + 1; j < events.Count && trackedEmitted > 0; j++)
                            {
                                if (Tracked.Contains(TypeOf(events[j])))
                                {
                                    trackedEmitted--;
                                    end = j;
                                }
                            }
                            for (var j = i + 1; j <= end; j++)
                            {
                                if (TypeOf(events[j]) == "death")
                                {
                                    Ledger((int)IntField(events[j], "actorId")).Alive = false;
                                    AddEnder(castEnders, (int)IntField(events[j], "actorId"), tick);
                                }
                            }
                            i = end;
                            blockStart = end + 1;
                            break;
                        }
                    }
                }
                foreach (var a in state.Actors.Values.ToList())
                {
                    Windows.CheckWindowExpiries(state, a, actual.Add);
                    Effects.ExpireEffects(a, tick);
                }
                Windows.CheckFloorExpiries(state, floor, actual.Add);
            }

            var got = actual.Where(e => Tracked.Contains(e.Type)).Select(e => e.ToCanonicalJson()).ToList();
            if (!expected.SequenceEqual(got))
            {
                Assert.Fail($"{name} t={t}: cast/window/combo events diverge.{Environment.NewLine}expected:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", expected)}{Environment.NewLine}port:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", got)}");
            }
            foreach (var e in got)
            {
                var type = JsonDocument.Parse(e).RootElement.GetProperty("type").GetString()!;
                checkedCounts[type] = checkedCounts.GetValueOrDefault(type) + 1;
            }

            if (rec.TryGetProperty("p", out var pEl))
            {
                inFlight += CheckProjection(name, t, pEl.GetString()!, setup, state, castCtx, pendingCasts);
            }
        }

        foreach (var ((actor, ability, end), seenAt) in pendingCasts)
        {
            var ended = endedCasts.Contains((actor, ability, end)) ||
                end >= finalTick ||
                (castEnders.TryGetValue(actor, out var ticks) && ticks.Any(x => x >= seenAt - 1 && x <= end));
            Assert.True(ended, $"{name}: {actor} was casting {ability} (castEnd {end}, seen at t={seenAt}) but no cast_complete, interrupt, stop or death ends it");
        }

        var table = string.Join(", ", totals.Keys.Select(k => $"{k} {checkedCounts.GetValueOrDefault(k)}/{totals[k]}"));
        _out.WriteLine($"{name}: {table}; in-flight casts {inFlight}; records {records}; targets derived {derivedTargets}, from trace {traceTargets}; floor centres {floorCentres}, floor stands {floorStands}");
        foreach (var k in totals.Keys)
        {
            Assert.Equal(totals[k], checkedCounts.GetValueOrDefault(k));
        }
        Assert.True(checkedCounts.GetValueOrDefault("cast_complete") > 0, "vacuous: no cast_complete checked");
    }

    private static void AddEnder(Dictionary<int, List<int>> enders, int actorId, int tick)
    {
        if (!enders.TryGetValue(actorId, out var list))
        {
            enders[actorId] = list = new List<int>();
        }
        list.Add(tick);
    }

    /// <summary>The live targets a cast resolved: its own damage/heal events in [from, to), minus those it killed.</summary>
    private static List<int> TargetsOf(List<JsonElement> events, int from, int to, int casterId)
    {
        var hit = new SortedSet<int>();
        var died = new HashSet<int>();
        for (var k = from; k < to; k++)
        {
            var e = events[k];
            var type = TypeOf(e);
            if ((type == "damage" || type == "heal") && IntField(e, "sourceId") == casterId)
            {
                hit.Add((int)IntField(e, "targetId"));
            }
            else if (type == "death")
            {
                died.Add((int)IntField(e, "actorId"));
            }
        }
        return hit.Where(id => !died.Contains(id)).ToList();
    }

    /// <summary>
    /// Every actor casting in a full projection: syncs alive, then StartCast on a fresh, ready copy at
    /// <c>castEnd - castTicks</c> must land on the projected castEnd, with the cast active in this record.
    /// </summary>
    private static int CheckProjection(
        string name,
        int t,
        string p,
        OracleSetupContent setup,
        SimState ledger,
        CastContext ctx,
        Dictionary<(int, string, int), int> pending)
    {
        var groups = p[(p.IndexOf(";a=", StringComparison.Ordinal) + 3)..];
        if (groups.Length == 0)
        {
            return 0;
        }
        var count = 0;
        foreach (var g in groups.Split('|'))
        {
            var f = g.Split(',');
            var id = int.Parse(f[0], CultureInfo.InvariantCulture);
            if (ledger.Actors.TryGetValue(id, out var la))
            {
                la.Alive = f[5] == "1";
            }
            if (f[7] == "-")
            {
                Assert.Equal("-", f[8]);
                continue;
            }
            var abilityId = f[7];
            var castEnd = int.Parse(f[8], CultureInfo.InvariantCulture);
            Assert.True(setup.Abilities.TryGetValue(abilityId, out var def), $"{name} t={t}: actor {id} casts {abilityId}, which the setup does not define");
            var start = castEnd - def!.CastTicks;
            Assert.True(start + 1 <= t && t <= castEnd, $"{name} t={t}: actor {id} cast {abilityId} castEnd {castEnd} is not in flight here");
            var probe = Actors.Create(id, ActorKind.Player, "probe", ActorRole.None, Vec3.Zero, 100, def.Cost.Amount);
            var target = Actors.Create(id + 1, ActorKind.Enemy, "probe", ActorRole.None, Vec3.Zero, 100, 0);
            var s = SimState.Create(0);
            s.Tick = start;
            s.Actors[probe.Id] = probe;
            s.Actors[target.Id] = target;
            Assert.Null(Casting.StartCast(s, probe, def, target.Id, null, start, ctx.Hooks));
            Assert.Equal((abilityId, castEnd, start), (probe.CastId, probe.CastEndsTick!.Value, probe.CastStartTick!.Value));
            pending.TryAdd((id, abilityId, castEnd), t);
            count++;
        }
        return count;
    }
}
