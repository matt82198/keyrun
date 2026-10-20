using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE section 4 "The six Combos": Consecrate is "Burst plus a 6-second heal pulse for allies near the target."
/// content/data/combos.json gives it damage 150 radiant plus <c>applyEffect consecrateHeal</c> (120 ticks; the effect is
/// a periodic heal of 40 every 20 ticks, content/data/effects/player.json). The burst lands on the Sundered enemy; the
/// heal pulse goes to each living ally of the Payoff actor within <see cref="Combos.HealPulseRadiusMm"/> of it, never to
/// the enemy carrier (finding f-b08fbdb5).
/// </summary>
public sealed class ComboConsecrateTests
{
    private static readonly EffectDef ConsecrateHeal = new("consecrateHeal", 1, null, new EffectModifiers(), new EffectPeriodic(20, new HealOp(40)));
    private static readonly IReadOnlyDictionary<string, EffectDef> ConsecrateDefs = Defs(ConsecrateHeal);

    private static readonly EffectOp[] Consecrate =
    {
        new DamageOp(150, "radiant"),
        new ApplyEffectOp("consecrateHeal", 120, 1),
    };

    private static bool Has(ActorState a, string effectId) => a.Effects.Any(e => e.EffectId == effectId);

    [Fact]
    public void Consecrate_never_puts_its_heal_pulse_on_the_enemy_carrier()
    {
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: 1000, z: 0);
        var carrier = Make(9, ActorKind.Enemy, "boss", maxHealth: 1000, x: 0, z: 0);
        var state = At(40, chanter, carrier);

        Combos.ApplyComboEffects(state, chanter, carrier, Consecrate, ConsecrateDefs, _ => { });

        Assert.False(Has(carrier, "consecrateHeal"));
    }

    [Fact]
    public void Consecrate_burst_still_damages_the_enemy_carrier()
    {
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: 1000, z: 0);
        var carrier = Make(9, ActorKind.Enemy, "boss", maxHealth: 1000, x: 0, z: 0);
        var state = At(40, chanter, carrier);

        Combos.ApplyComboEffects(state, chanter, carrier, Consecrate, ConsecrateDefs, _ => { });

        Assert.Equal(850, carrier.Health);
    }

    [Fact]
    public void Consecrate_heal_pulse_reaches_each_living_ally_near_the_target_for_six_seconds()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank, x: 500, z: 0);
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: 0, z: Combos.HealPulseRadiusMm);
        var carrier = Make(9, ActorKind.Enemy, "boss", maxHealth: 1000, x: 0, z: 0);
        var state = At(40, warden, chanter, carrier);

        Combos.ApplyComboEffects(state, chanter, carrier, Consecrate, ConsecrateDefs, _ => { });

        foreach (var ally in new[] { warden, chanter })
        {
            var inst = Assert.Single(ally.Effects, e => e.EffectId == "consecrateHeal");
            Assert.Equal(160, inst.EndsTick);
            Assert.Equal(chanter.Id, inst.SourceId);
        }
    }

    [Fact]
    public void Consecrate_heal_pulse_skips_an_ally_out_of_range_and_a_dead_ally()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank, x: 0, z: 0);
        warden.Alive = false;
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: Combos.HealPulseRadiusMm + 1, z: 0);
        var cinder = Make(3, ActorKind.Player, "cinder", ActorRole.Damage, x: 6000, z: 6000);
        var carrier = Make(9, ActorKind.Enemy, "boss", maxHealth: 1000, x: 0, z: 0);
        var state = At(40, warden, chanter, cinder, carrier);

        Combos.ApplyComboEffects(state, chanter, carrier, Consecrate, ConsecrateDefs, _ => { });

        Assert.False(Has(warden, "consecrateHeal"));
        Assert.False(Has(chanter, "consecrateHeal"));
        Assert.False(Has(cinder, "consecrateHeal"));
        Assert.False(Has(carrier, "consecrateHeal"));
    }

    [Fact]
    public void Consecrate_heal_pulse_does_not_reach_another_enemy_near_the_target()
    {
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: 1000, z: 0);
        var carrier = Make(9, ActorKind.Enemy, "boss", maxHealth: 1000, x: 0, z: 0);
        var add = Make(10, ActorKind.Enemy, "add", x: 300, z: 0);
        var state = At(40, chanter, carrier, add);

        Combos.ApplyComboEffects(state, chanter, carrier, Consecrate, ConsecrateDefs, _ => { });

        Assert.True(Has(chanter, "consecrateHeal"));
        Assert.False(Has(add, "consecrateHeal"));
    }
}
