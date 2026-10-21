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

    private static readonly Dictionary<string, int> CooldownTicks = new()
    {
        ["boss.slam"] = 60,
        ["boss.cleave"] = 160,
        ["boss.swing"] = 0,
    };

    [Fact(DisplayName = "encounter cast ledger: a cast its cooldown refuses never starts, so it never holds its caster (f-df833cf3)")]
    public void A_cast_still_on_cooldown_is_not_in_flight()
    {
        // bots-hv-k10-s7 (mf-362, pr307-mf2): Tempo fires the cleave step every 135 ticks but the cleave's cooldown is
        // 160, so every other cleave the step issues is refused by combat (sim/combat/casting.ts startCast not_ready)
        // and never starts. The cleave begun at 1620 (cast_complete 1660) is ready again at 1780; the step's cleave at
        // 1755 never started, and the slam due at 1778 found its caster free.
        var ledger = new EncounterCastLedger(new Dictionary<string, int> { ["boss.cleave"] = 40, ["boss.slam"] = 10 }, CooldownTicks);
        ledger.Completed(Boss, "boss.cleave", 1660);
        ledger.Completed(Boss, "boss.slam", 1788);
        ledger.Started(Boss, "boss.cleave", 1755);
        foreach (var t in new[] { 1756, 1770, 1778 })
        {
            Assert.True(ledger.InFlight(Boss, t) is null, $"t={t}: in flight {ledger.InFlight(Boss, t)}, expected none (cleave on cooldown until 1780)");
        }
        Assert.Equal(("boss.slam", 1788), ledger.InFlight(Boss, 1779));
        var st = StateWithBoss();
        ledger.Apply(st, 1778);
        Assert.Null(st.Actors[Boss].CastId);
    }

    [Fact(DisplayName = "encounter cast ledger: a cast started on the tick its cooldown ends is in flight")]
    public void A_cast_started_the_tick_its_cooldown_ends_is_in_flight()
    {
        // cooldowns.ts chargesAvailable: ready when now >= the recharge tick (1620 + 160 = 1780).
        var ledger = new EncounterCastLedger(new Dictionary<string, int> { ["boss.cleave"] = 40 }, CooldownTicks);
        ledger.Completed(Boss, "boss.cleave", 1660);
        ledger.Started(Boss, "boss.cleave", 1780);
        Assert.Equal(("boss.cleave", 1820), ledger.InFlight(Boss, 1781));
        var early = new EncounterCastLedger(new Dictionary<string, int> { ["boss.cleave"] = 40 }, CooldownTicks);
        early.Completed(Boss, "boss.cleave", 1660);
        early.Started(Boss, "boss.cleave", 1779);
        Assert.Null(early.InFlight(Boss, 1781));
    }

    [Fact(DisplayName = "encounter cast ledger: a refused start does not restart the cooldown of the cast that did start")]
    public void A_refused_start_leaves_the_cooldown_on_the_real_start()
    {
        // The refused 1755 cleave consumes no charge (startCast returns before consumeCharge), so 1780 is still ready.
        var ledger = new EncounterCastLedger(new Dictionary<string, int> { ["boss.cleave"] = 40 }, CooldownTicks);
        ledger.Completed(Boss, "boss.cleave", 1660);
        ledger.Started(Boss, "boss.cleave", 1755);
        ledger.Started(Boss, "boss.cleave", 1780);
        Assert.Null(ledger.InFlight(Boss, 1760));
        Assert.Equal(("boss.cleave", 1820), ledger.InFlight(Boss, 1800));
    }

    private const int Pair = 31;

    private static readonly Dictionary<string, int> PairCastTicks = new() { ["pair.spreadMark"] = 40 };

    private static readonly Dictionary<string, int> PairCooldownTicks = new() { ["pair.spreadMark"] = 400 };

    [Fact(DisplayName = "encounter cast ledger: a target-shaped cast combat refuses out of range never starts, so its caster is free the next tick (f-fa598b6b)")]
    public void A_cast_refused_out_of_range_is_not_in_flight()
    {
        // content/data/abilities/enemies.json pair.spreadMark: target shape, range 25000, castTicks 40, cooldown 400.
        // The encounter step issues it on schedule wherever its target stands; combat (sim/combat/casting.ts startCast,
        // Casting.cs StartError) refuses it out_of_range before it consumes a charge, so the Pair never casts it and
        // is free on 601. Combat reports the refusal as a cast_refused event, which the replay feeds the ledger.
        var ledger = new EncounterCastLedger(PairCastTicks, PairCooldownTicks);
        ledger.Refused(Pair, "pair.spreadMark", 600);
        ledger.Started(Pair, "pair.spreadMark", 600);
        foreach (var t in new[] { 601, 620, 640 })
        {
            Assert.True(ledger.InFlight(Pair, t) is null, $"t={t}: in flight {ledger.InFlight(Pair, t)}, expected none (refused out_of_range at 600)");
        }
        var st = StateWithBoss();
        st.Actors[Pair] = new ActorState { Id = Pair, Kind = ActorKind.Enemy, ClassId = "pair.melee", Health = 100, MaxHealth = 100 };
        ledger.Apply(st, 601);
        Assert.Null(st.Actors[Pair].CastId);
        Assert.Null(st.Actors[Pair].CastEndsTick);
    }

    [Fact(DisplayName = "encounter cast ledger: a range refusal consumes no charge, so the next in-range start is in flight")]
    public void A_range_refusal_leaves_the_cooldown_untouched()
    {
        // startCast returns out_of_range before consumeCharge: the refused 600 start does not put the mark on cooldown.
        var ledger = new EncounterCastLedger(PairCastTicks, PairCooldownTicks);
        ledger.Refused(Pair, "pair.spreadMark", 600);
        ledger.Started(Pair, "pair.spreadMark", 600);
        ledger.Started(Pair, "pair.spreadMark", 610);
        Assert.Null(ledger.InFlight(Pair, 605));
        Assert.Equal(("pair.spreadMark", 650), ledger.InFlight(Pair, 611));
    }

    [Fact(DisplayName = "encounter cast ledger: a refusal is per caster, ability and tick")]
    public void A_refusal_does_not_cancel_other_starts()
    {
        var ledger = new EncounterCastLedger(PairCastTicks, PairCooldownTicks);
        ledger.Refused(Pair, "pair.spreadMark", 599);
        ledger.Refused(Pair + 1, "pair.spreadMark", 600);
        ledger.Started(Pair, "pair.spreadMark", 600);
        Assert.Equal(("pair.spreadMark", 640), ledger.InFlight(Pair, 601));
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
