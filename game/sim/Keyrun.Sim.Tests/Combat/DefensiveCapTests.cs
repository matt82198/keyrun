using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-DEFCAP-1 (ruling 735, finding f-fe32ef35): the player total damageTakenBp is capped at -7500, so a fully
/// stacked defensive set never reduces incoming damage to zero. Defensives are percentage mitigation, never
/// invulnerability (Matt 2026-10-25: "defensives are never 100% invulnerabilities").
/// </summary>
public sealed class DefensiveCapTests
{
    private static readonly EffectDef BraceSelf = new("braceSelf", 1, null, new EffectModifiers(DamageTakenBp: -4000));
    private static readonly EffectDef BracedSelf = new("bracedSelf", 1, null, new EffectModifiers(DamageTakenBp: -3000));
    private static readonly EffectDef AegisShield = new("aegisShield", 1, null, new EffectModifiers(DamageTakenBp: -3500));
    private static readonly IReadOnlyDictionary<string, EffectDef> Defs3 = Defs(BraceSelf, BracedSelf, AegisShield);

    [Fact]
    public void Tank_with_full_defensive_stack_still_takes_damage()
    {
        // Warden tank with Brace (-4000), Vigil (-3000) and Aegis (-3500): raw total -10500, capped at -7500.
        var attacker = Actor(1, ActorKind.Enemy, maxHealth: 1000);
        var tank = Actor(2, ActorKind.Player, maxHealth: 1000);
        Effects.ApplyEffectInstance(tank, BraceSelf, tank.Id, 100, 1, 0);
        Effects.ApplyEffectInstance(tank, BracedSelf, tank.Id, 100, 1, 0);
        Effects.ApplyEffectInstance(tank, AegisShield, tank.Id, 100, 1, 0);

        var result = Mitigation.DealDamage(0, attacker, tank, 1000, Defs3, _ => { }, attacker.Id);

        // At least 25% of the hit lands: 1000 * (10000 - 7500) / 10000 = 250.
        Assert.True(
            result.ModifiedAmount >= 250,
            $"full defensive stack (total damageTakenBp -10500) must be capped at -7500: expected ModifiedAmount >= 250, got {result.ModifiedAmount}");
        Assert.True(
            1000 - tank.Health >= 250,
            $"full defensive stack must still take damage: expected tank to lose >= 250 health, lost {1000 - tank.Health}");
    }
}
