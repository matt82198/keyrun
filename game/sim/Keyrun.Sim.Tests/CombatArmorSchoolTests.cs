using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// L3 SIM-ARMOR-SCHOOL (design/TOOLKIT-CONTRACT.md): class armor reduces physical damage only, as in WoW; every other
/// school bypasses it. Driven through a real cast so the school comes from the ability's DamageOp, not a test seam.
/// </summary>
public sealed class CombatArmorSchoolTests
{
    private const long ArmorBp = 3500; // a Warden: keeps 65 of every 100 physical

    private static long HitTaken(string school)
    {
        var caster = Make(1, ActorKind.Enemy, "boss", ActorRole.Damage);
        var warden = Make(2, ActorKind.Player, "warden", ActorRole.Tank, x: 1000, maxHealth: 1000);
        warden.ArmorBp = ArmorBp;
        var strike = Def("strike", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(100, school) }, castTicks: 1, range: 5000);
        var state = At(0, caster, warden);
        var rules = Rules.Make(Config(AbilityMap(strike), boundsHalf: 100000));
        var events = new List<SimEvent>();
        Tick(state, rules, Cmds(Cast(1, "strike", 2)), events.Add);
        for (var i = 0; i < 4; i++)
        {
            Tick(state, rules, new Command[0], events.Add);
        }
        return 1000 - state.Actors[2].Health;
    }

    [Fact]
    public void Armor_reduces_a_physical_hit() => Assert.Equal(65, HitTaken("physical"));

    [Fact]
    public void Armor_does_not_reduce_a_shadow_hit() => Assert.Equal(100, HitTaken("shadow"));

    [Theory]
    [InlineData("fire")]
    [InlineData("frost")]
    [InlineData("radiant")]
    public void Armor_does_not_reduce_any_other_school(string school) => Assert.Equal(100, HitTaken(school));

    [Fact]
    public void ApplyDamage_defaults_to_physical_so_existing_callers_keep_their_armor()
    {
        var t = Actor(2, ActorKind.Player, maxHealth: 1000);
        t.Health = 1000;
        t.ArmorBp = ArmorBp;
        var (_, emit) = Collector();
        Assert.Equal(65, Damage.ApplyDamage(1, t, 100, 0, 0, NoDefs, emit, 1).ModifiedAmount);
    }
}
