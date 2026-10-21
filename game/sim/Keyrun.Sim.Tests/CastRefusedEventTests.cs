using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// CAST-REFUSED (f-fa598b6b): an enemy cast that combat refuses out_of_range (Casting.StartError; sim/combat/casting.ts
/// startCast) is reported as a <c>cast_refused</c> event {actorId, abilityId, reason}. Encounter-generated enemy casts
/// are not in a trace record's commands (game/sim/ORACLE.md section 3), so without the event a replay that runs no
/// combat cannot tell a refused cast from a started one (the encounter replay's EncounterCastLedger showed a phantom
/// busy caster). Players' presses are in the record and are never reported; other refusal reasons are not reported
/// either (the recorded traces hold enemy not_ready refusals, and the ledger already models cooldowns). The ability
/// mirrors content/data/abilities/enemies.json seed.lash: target shape, range 1500, castTicks 20. Twin of
/// tests/sim/combat/cast-refused.test.ts.
/// </summary>
public sealed class CastRefusedEventTests
{
    private const int Enemy = 1;
    private const int Player = 2;

    private static readonly AbilityDef Lash = Def("seed.lash", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(70, "physical") },
        castTicks: 20, cooldownTicks: 200, range: 1500);

    private static (SimState State, StepRules Rules, List<SimEvent> Events) Scenario(long playerX)
    {
        var enemy = Make(Enemy, ActorKind.Enemy, "seed.elite", x: 0, maxHealth: 3000);
        var player = Make(Player, ActorKind.Player, "warrior", ActorRole.Tank, x: playerX, maxHealth: 500);
        return (At(0, enemy, player), Rules.Make(Config(AbilityMap(Lash), boundsHalf: 100000)), new List<SimEvent>());
    }

    private static List<SimEvent> Refusals(List<SimEvent> events) => events.Where(e => e.Type == "cast_refused").ToList();

    [Fact]
    public void An_enemy_cast_refused_out_of_range_emits_cast_refused_and_never_starts()
    {
        var (state, rules, events) = Scenario(playerX: 2000);
        Tick(state, rules, Cmds(Cast(Enemy, "seed.lash", Player)), events.Add);

        var refused = Assert.Single(Refusals(events));
        Assert.Equal("{\"abilityId\":\"seed.lash\",\"actorId\":1,\"reason\":\"out_of_range\",\"tick\":0,\"type\":\"cast_refused\"}", refused.ToCanonicalJson());
        Assert.Null(state.Actors[Enemy].CastId);
    }

    [Fact]
    public void An_enemy_cast_in_range_starts_and_emits_no_refusal()
    {
        var (state, rules, events) = Scenario(playerX: 1500);
        Tick(state, rules, Cmds(Cast(Enemy, "seed.lash", Player)), events.Add);

        Assert.Empty(Refusals(events));
        Assert.Equal("seed.lash", state.Actors[Enemy].CastId);
    }

    [Fact]
    public void A_player_press_refused_out_of_range_emits_no_refusal()
    {
        var player = Make(Player, ActorKind.Player, "warrior", ActorRole.Tank, x: 0, maxHealth: 500);
        var enemy = Make(Enemy, ActorKind.Enemy, "seed.elite", x: 2000, maxHealth: 3000);
        var state = At(0, enemy, player);
        var rules = Rules.Make(Config(AbilityMap(Lash), boundsHalf: 100000));
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(Player, "seed.lash", Enemy)), events.Add);

        Assert.Empty(Refusals(events));
        Assert.Null(state.Actors[Player].CastId);
    }

    [Fact]
    public void An_enemy_cast_refused_not_ready_emits_no_refusal()
    {
        // The recorded oracle traces hold enemy not_ready refusals; reporting them would move every one of those traces.
        var (state, rules, events) = Scenario(playerX: 1000);
        Tick(state, rules, Cmds(Cast(Enemy, "seed.lash", Player)), events.Add);
        while (state.Tick <= 25)
        {
            Tick(state, rules, Cmds(), events.Add);
        }
        Tick(state, rules, Cmds(Cast(Enemy, "seed.lash", Player)), events.Add);

        Assert.Empty(Refusals(events));
        Assert.Null(state.Actors[Enemy].CastId);
    }
}
