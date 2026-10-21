using System.Collections.Generic;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// Harness fidelity for the encounter-only replay (<see cref="EncounterTraceTests"/>, PR #415 ruling R-415-C). The
/// oracle trace carries no cast_start event (ORACLE.md section 3: encounter-generated enemy casts are not in a record's
/// commands, and combat emits only cast_complete and interrupt), so a cast that never completes before the run ends,
/// such as bots-hv-k10-s7's hollowWarden.slam begun at 2065 in a run that ends at 2074, was invisible to the model:
/// the boss looked free and the model drew a hollowWarden.lock telegraph the oracle never drew.
/// </summary>
public sealed class EncounterCastLedgerTests
{
    private const int Boss = 900;

    private static readonly Dictionary<string, int> CastTicks = new()
    {
        ["boss.slam"] = 10,
        ["boss.cleave"] = 5,
        ["boss.swing"] = 0,
    };

    private static SimState StateWithBoss()
    {
        var st = new SimState { Tick = 0, Seed = 1, Prng = new PrngState(1, 2, 3, 4), Phase = "combat" };
        st.Actors[Boss] = new ActorState { Id = Boss, Kind = ActorKind.Enemy, ClassId = "boss", Health = 100, MaxHealth = 100 };
        return st;
    }

    [Fact(DisplayName = "encounter cast ledger: a cast started with no cast_complete before the run ends is in flight for its cast time")]
    public void A_cast_in_flight_at_run_end_keeps_its_caster_busy()
    {
        // The trace's shape at bots-hv-k10-s7's end: cleave completes at 2065, the slam starts the same tick, the run
        // ends at 2074 with the slam due at 2075, so no cast_complete for it ever appears.
        var ledger = new EncounterCastLedger(CastTicks);
        ledger.Completed(Boss, "boss.cleave", 2065);
        ledger.Started(Boss, "boss.slam", 2065);
        var st = StateWithBoss();

        ledger.Apply(st, 2065);
        Assert.Equal("boss.cleave", st.Actors[Boss].CastId);
        foreach (var t in new[] { 2066, 2070, 2074, 2075 })
        {
            ledger.Apply(st, t);
            Assert.True(st.Actors[Boss].CastId == "boss.slam", $"t={t}: cast {st.Actors[Boss].CastId ?? "none"}, expected boss.slam");
            Assert.Equal(2075, st.Actors[Boss].CastEndsTick);
        }
        ledger.Apply(st, 2076);
        Assert.Null(st.Actors[Boss].CastId);
    }

    [Fact(DisplayName = "encounter cast ledger: a started cast that the trace completes is one span, (X - C, X]")]
    public void A_started_cast_that_completes_is_not_double_counted()
    {
        var ledger = new EncounterCastLedger(CastTicks);
        ledger.Completed(Boss, "boss.slam", 110);
        ledger.Started(Boss, "boss.slam", 100);
        Assert.Null(ledger.InFlight(Boss, 100));
        Assert.Equal(("boss.slam", 110), ledger.InFlight(Boss, 101));
        Assert.Equal(("boss.slam", 110), ledger.InFlight(Boss, 110));
        Assert.Null(ledger.InFlight(Boss, 111));
    }

    [Fact(DisplayName = "encounter cast ledger: an interrupted cast stops being in flight after the interrupt tick")]
    public void An_interrupted_cast_frees_its_caster()
    {
        var ledger = new EncounterCastLedger(CastTicks);
        ledger.Started(Boss, "boss.slam", 100);
        ledger.Interrupted(Boss, 104);
        Assert.Equal(("boss.slam", 110), ledger.InFlight(Boss, 104));
        Assert.Null(ledger.InFlight(Boss, 105));
        var st = StateWithBoss();
        ledger.Apply(st, 104);
        Assert.Equal("boss.slam", st.Actors[Boss].CastId);
        ledger.Apply(st, 105);
        Assert.Null(st.Actors[Boss].CastId);
    }

    [Fact(DisplayName = "encounter cast ledger: an instant cast is never in flight")]
    public void An_instant_cast_is_never_in_flight()
    {
        var ledger = new EncounterCastLedger(CastTicks);
        ledger.Started(Boss, "boss.swing", 100);
        ledger.Completed(Boss, "boss.swing", 100);
        Assert.Null(ledger.InFlight(Boss, 100));
        Assert.Null(ledger.InFlight(Boss, 101));
    }
}
