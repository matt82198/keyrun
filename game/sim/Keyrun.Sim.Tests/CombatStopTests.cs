using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// FORCES-ROUTE-PLAN M2 STOPS (design/BIBLE.md section 9.3, "Stops as well as kicks"): a stun, freeze or silence that
/// lands on a caster during an interruptible cast ends it with an <c>interrupt {by: "stop"}</c> event. Drives the
/// REAL content through <see cref="Rules.Make"/>: Choral Mending (choir.healCast), Cold Snap, the Shatter combo and
/// the effect defs from the hollow-vault-k2 oracle setup, and Dissonance's ward variant from
/// content/data/abilities/chanter.json.
/// </summary>
public class CombatStopTests
{
    private const int WardenId = 1;
    private const int CinderId = 2;
    private const int ChanterId = 3;
    private const int CantorId = 10;

    private static readonly OracleSetupContent Content = OracleSetupContent.Load("hollow-vault-k2.setup.json");

    // The Warden's sundered setup (Window on its target) and Cinder's bare Payoff: Shatter's real combo row closes it.
    private static readonly AbilityDef Sunder = Def("test.sunder", new AbilityShape(ShapeKind.Target), new EffectOp[] { new WindowOp("sundered", 60, true, "warden") }, costResource: "none", range: 25000);
    private static readonly AbilityDef Detonate = Def("test.detonate", new AbilityShape(ShapeKind.Target), new EffectOp[] { new PayoffOp() }, costResource: "none", range: 25000);

    // A cast the Cantor cannot be stopped out of: Choral Mending with interruptible false.
    private static readonly AbilityDef Unstoppable = Content.Abilities["choir.healCast"] with { Id = "test.unstoppableMending", Interruptible = false };

    public enum Stop
    {
        Shatter,
        ColdSnap,
        WardDissonance,
    }

    [Theory]
    [InlineData(Stop.Shatter)]
    [InlineData(Stop.ColdSnap)]
    [InlineData(Stop.WardDissonance)]
    public void Each_stop_ends_a_live_Choral_Mending_with_an_interrupt_by_stop(Stop stop)
    {
        var run = Arrange("choir.healCast");
        var events = run.Apply(stop);
        Assert.Null(run.Cantor.CastId);
        var interrupt = Assert.Single(events, e => e.Type == "interrupt");
        Assert.Equal("stop", Str(interrupt, "by"));
        Assert.Equal(CantorId, Int(interrupt, "targetId"));
        Assert.Equal("choir.healCast", Str(interrupt, "abilityId"));
        Assert.Equal(stop == Stop.WardDissonance ? ChanterId : CinderId, Int(interrupt, "sourceId"));
        // A stop locks no school (a Mythic+ stop, not a kick).
        Assert.DoesNotContain(run.Cantor.Effects, e => e.EffectId.StartsWith("lockout:", StringComparison.Ordinal));
        // The cast never completes: no heal and no cast_complete for it later.
        run.RunTo(120);
        Assert.DoesNotContain(run.Events, e => e.Type == "cast_complete" && Str(e, "abilityId") == "choir.healCast");
    }

    [Theory]
    [InlineData(Stop.Shatter)]
    [InlineData(Stop.ColdSnap)]
    [InlineData(Stop.WardDissonance)]
    public void Each_stop_is_a_no_op_on_a_cast_that_is_not_interruptible(Stop stop)
    {
        var run = Arrange(Unstoppable.Id);
        var events = run.Apply(stop);
        Assert.Equal(Unstoppable.Id, run.Cantor.CastId);
        Assert.DoesNotContain(events, e => e.Type == "interrupt");
    }

    [Fact]
    public void A_stop_counts_as_an_interrupt_for_Echo()
    {
        var run = Arrange("choir.healCast");
        var events = run.Apply(Stop.ColdSnap);
        var echo = new ClauseDef("echo", "Echo", ClauseKind.Echo, new Dictionary<string, long> { ["addMaxHealth"] = 600, ["spawnDelayTicks"] = 40 }, "test");
        var rules = Clauses.ApplyClause(Clauses.DefaultEncounterRules(2), echo);
        var spawn = Assert.Single(Clauses.PendingEchoSpawns(events, rules));
        Assert.Equal(CantorId, spawn.SourceActorId);
    }

    [Fact]
    public void A_stop_does_not_refund_Quenchs_Heat()
    {
        var run = Arrange("choir.healCast");
        run.Cinder.Resource = 50;
        run.Apply(Stop.WardDissonance);
        Assert.Null(run.Cantor.CastId);
        // Quench on the stopped (now idle) Cantor whiffs: no kick landed, so its +20 Heat is not credited.
        var events = run.Step(Cast(CinderId, "cinder.quench", CantorId));
        Assert.DoesNotContain(events, e => e.Type == "interrupt");
        Assert.Equal(50, run.Cinder.Resource);
    }

    /// <summary>
    /// BIBLE section 3, "Interrupts resolve by intended tick with a fixed tiebreak, never arrival order": a kick and a
    /// stop landing on the same tick resolve the same way whichever actor acts first. The kick wins (it locks the
    /// school), and the stop finds no cast left to end. Cinder (id 2) acts before Chanter (id 3), so the two rows put
    /// the kick first in one and the stop first in the other.
    /// </summary>
    [Theory]
    [InlineData("cinder.quench", CinderId, "chanter.dissonance", ChanterId)]
    [InlineData("chanter.rebuke", ChanterId, "cinder.coldSnap", CinderId)]
    public void A_kick_and_a_stop_on_the_same_tick_resolve_as_the_kick_whichever_actor_acts_first(string kick, int kickerId, string stop, int stopperId)
    {
        var run = Arrange("choir.healCast");
        var events = run.Step(Cast(kickerId, kick, CantorId), Cast(stopperId, stop, CantorId));
        Assert.Null(run.Cantor.CastId);
        var interrupt = Assert.Single(events, e => e.Type == "interrupt");
        Assert.Equal(kickerId, Int(interrupt, "sourceId"));
        Assert.Null(interrupt.Get("by"));
        Assert.Contains(run.Cantor.Effects, e => e.EffectId.StartsWith("lockout:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_control_the_caster_already_carried_does_not_stop_a_later_cast()
    {
        var run = Arrange(null);
        run.Step(Cast(CinderId, "cinder.coldSnap", CantorId));
        Assert.Contains(run.Cantor.Effects, e => e.EffectId == "stunned");
        var events = run.Step(Cast(CantorId, "choir.healCast", CantorId));
        Assert.Equal("choir.healCast", run.Cantor.CastId);
        Assert.DoesNotContain(events, e => e.Type == "interrupt");
    }

    private static Run Arrange(string? cantorCast)
    {
        var warden = Make(WardenId, ActorKind.Player, "warden", ActorRole.Tank, maxResource: 100);
        var cinder = Make(CinderId, ActorKind.Player, "cinder", ActorRole.Damage, x: 200, maxResource: 100);
        var chanter = Make(ChanterId, ActorKind.Player, "chanter", ActorRole.Healer, x: 400, maxResource: 100);
        var cantor = Make(CantorId, ActorKind.Enemy, "choir.lieutenant", x: 3000, maxHealth: 2000);
        var state = At(0, warden, cinder, chanter, cantor);
        var abilities = new Dictionary<string, AbilityDef>(Content.Abilities, StringComparer.Ordinal)
        {
            [Sunder.Id] = Sunder,
            [Detonate.Id] = Detonate,
            [Unstoppable.Id] = Unstoppable,
            ["chanter.dissonance"] = WardDissonance(Content.Abilities["chanter.dissonance"]),
        };
        var rules = Rules.Make(Config(abilities, Content.Effects, boundsHalf: 50000, combos: Content.Combos, fieldCombos: Content.FieldCombos));
        var run = new Run(state, rules, cinder, cantor);
        if (cantorCast is not null)
        {
            run.Step(Cast(CantorId, cantorCast, CantorId));
            Assert.Equal(cantorCast, cantor.CastId);
        }
        return run;
    }

    // Dissonance as the Ward verse plays it: the ward variant's ops, read from the real chanter.json.
    private static AbilityDef WardDissonance(AbilityDef baseDef)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ContentFile("abilities", "chanter.json")));
        var entry = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "chanter.dissonance");
        var ops = entry.GetProperty("variants").GetProperty("ward").EnumerateArray().Select(o => (EffectOp)(o.GetProperty("op").GetString() switch
        {
            "damage" => new DamageOp(o.GetProperty("amount").GetInt64(), o.GetProperty("school").GetString()!),
            "applyEffect" => new ApplyEffectOp(o.GetProperty("effectId").GetString()!, o.GetProperty("durationTicks").GetInt32(), o.GetProperty("stacks").GetInt64()),
            "payoff" => new PayoffOp(),
            var op => throw new InvalidOperationException($"chanter.dissonance ward: op {op} not mapped by this test"),
        })).ToList();
        Assert.Contains(ops, op => op is ApplyEffectOp { EffectId: "silenced" });
        return baseDef with { Effects = ops };
    }

    private static string ContentFile(string dir, string file)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "content", "data", dir, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException($"content/data/{dir}/{file} not found above " + AppContext.BaseDirectory);
    }

    private sealed class Run
    {
        private readonly SimState _state;
        private readonly StepRules _rules;

        public Run(SimState state, StepRules rules, ActorState cinder, ActorState cantor)
        {
            _state = state;
            _rules = rules;
            Cinder = cinder;
            Cantor = cantor;
        }

        public ActorState Cinder { get; }

        public ActorState Cantor { get; }

        public List<SimEvent> Events { get; } = new();

        public List<SimEvent> Step(params Command[] cmds)
        {
            var (events, emit) = Collector();
            Tick(_state, _rules, cmds, emit);
            Events.AddRange(events);
            return events;
        }

        public void RunTo(int tick)
        {
            while (_state.Tick < tick)
            {
                Step();
            }
        }

        // Lands the stop on the Cantor; returns the events of the tick it lands on.
        public List<SimEvent> Apply(Stop stop)
        {
            switch (stop)
            {
                case Stop.Shatter:
                    Step(Cast(WardenId, Sunder.Id, CantorId));
                    var shatter = Step(Cast(CinderId, Detonate.Id, CantorId));
                    Assert.Equal("shatter", Str(shatter.Single(e => e.Type == "combo"), "comboId"));
                    return shatter;
                case Stop.ColdSnap:
                    return Step(Cast(CinderId, "cinder.coldSnap", CantorId));
                default:
                    return Step(Cast(ChanterId, "chanter.dissonance", CantorId));
            }
        }
    }
}
