using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// HEAL-NEVER-HOSTILE: a player's heal never changes an enemy's health. A policy playtest at seed 1 key 2 showed the
/// Chanter's Cadence (Mend) Heal over Time ticking on the Choir Acolyte and the Pair: a "target" ability resolves to
/// whatever the caster targets when the cast completes, with no faction filter, so a Cadence that started on the tank
/// and finished after the seat retargeted the pack landed its HoT on the enemy. Area abilities that carry a heal next
/// to a hostile op resolve hostile (affinity.ts mixed = hostile) and reached enemies the same way. The fix: a heal op,
/// or a beneficial applyEffect (a HoT, a shield, a haste), lands only on the caster's own faction; every other op on
/// the same ability (damage, the Attuned Window, a slow) still lands on the enemy.
/// </summary>
public sealed class HealNeverHostileTests
{
    private static readonly EffectDef Hot = new("mendingHot", 1, null, new EffectModifiers(), new EffectPeriodic(20, new HealOp(30)));
    private static readonly EffectDef Slow = new("slowed", 1, null, new EffectModifiers(MovementBp: -3000));

    private static (SimState State, ActorState Caster, ActorState Ally, ActorState Enemy) Scene()
    {
        var caster = Make(1, ActorKind.Player, "chanter", ActorRole.Healer, maxHealth: 1000);
        var ally = Make(2, ActorKind.Player, "warden", ActorRole.Tank, x: 1000, maxHealth: 1000);
        var enemy = Make(100, ActorKind.Enemy, "choir.acolyte", x: 2000, maxHealth: 1000);
        ally.Health = 500;
        enemy.Health = 500;
        caster.Health = 500;
        return (At(0, caster, ally, enemy), caster, ally, enemy);
    }

    private static void Complete(SimState state, ActorState caster, AbilityDef def, int? targetId)
    {
        caster.TargetId = targetId;
        caster.CastId = def.Id;
        Casting.CompleteCast(state, caster, new CastContext(AbilityMap(def), Defs(Hot, Slow), 60), _ => { });
    }

    private static AbilityDef Cadence() => Def(
        "chanter.cadence",
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new HealOp(150), new ApplyEffectOp("mendingHot", 60, 1), new ApplyEffectOp("slowed", 60, 1) },
        range: 25000);

    [Fact(DisplayName = "heal never hostile > a single-target heal and HoT finished on an enemy leave its health and effects alone")]
    public void Single_target_heal_on_an_enemy_does_nothing_to_its_health()
    {
        var (state, caster, _, enemy) = Scene();
        Complete(state, caster, Cadence(), enemy.Id);
        Assert.Equal(500, enemy.Health);
        Assert.DoesNotContain(enemy.Effects, e => e.EffectId == "mendingHot");
        Assert.Contains(enemy.Effects, e => e.EffectId == "slowed");
    }

    [Fact(DisplayName = "heal never hostile > the same single-target heal and HoT still land on an ally")]
    public void Single_target_heal_on_an_ally_still_heals()
    {
        var (state, caster, ally, _) = Scene();
        Complete(state, caster, Cadence(), ally.Id);
        Assert.Equal(650, ally.Health);
        Assert.Contains(ally.Effects, e => e.EffectId == "mendingHot");
    }

    [Fact(DisplayName = "heal never hostile > an area ability with a heal beside its damage never heals the enemies it hits")]
    public void Area_heal_on_a_hostile_circle_does_not_heal_enemies()
    {
        var (state, caster, _, enemy) = Scene();
        var chorus = Def(
            "chanter.chorus",
            new AbilityShape(ShapeKind.Circle, Radius: 5000),
            new EffectOp[] { new DamageOp(100, "radiant"), new HealOp(120), new ApplyEffectOp("mendingHot", 60, 1) },
            range: 15000);
        Complete(state, caster, chorus, null);
        Assert.Equal(400, enemy.Health);
        Assert.DoesNotContain(enemy.Effects, e => e.EffectId == "mendingHot");
    }

    [Fact(DisplayName = "heal never hostile > a combo heal closed on an enemy carrier (Hymn field) never heals the enemy")]
    public void Combo_heal_on_an_enemy_carrier_does_not_heal_it()
    {
        var (state, caster, ally, enemy) = Scene();
        Combos.ApplyComboEffects(state, caster, enemy, new EffectOp[] { new HealOp(60) }, Defs(Hot), _ => { });
        Assert.Equal(500, enemy.Health);
        Combos.ApplyComboEffects(state, caster, ally, new EffectOp[] { new HealOp(60) }, Defs(Hot), _ => { });
        Assert.Equal(560, ally.Health);
    }

    [Fact(DisplayName = "heal never hostile > a friendly area heal reaches the party and never the enemy")]
    public void Friendly_area_heal_reaches_the_party_only()
    {
        var (state, caster, ally, enemy) = Scene();
        var chorus = Def("chanter.chorus", new AbilityShape(ShapeKind.Circle, Radius: 5000), new EffectOp[] { new HealOp(120) }, range: 15000);
        Complete(state, caster, chorus, null);
        Assert.Equal(620, ally.Health);
        Assert.Equal(620, caster.Health);
        Assert.Equal(500, enemy.Health);
    }
}
