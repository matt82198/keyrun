using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// TOOLKIT-CONTRACT lane L2 SIM-DISPLACE (ruling R-TOOLKIT-2): the <see cref="DisplaceOp"/> moves the CASTER, in one
/// tick, in integer mm. Until it existed the only displacement op was <see cref="PullOp"/>, which moves the target.
/// Driven through <see cref="Rules.Make"/> like CasterOpsTests.
/// </summary>
public class DisplaceOpTests
{
    private const int CasterId = 1;
    private const int FoeId = 2;

    private static readonly EffectDef Rooted = new("rooted", 1, null, new EffectModifiers(MovementBp: -10000));
    private static readonly EffectDef Slowed = new("slowed", 1, "magic", new EffectModifiers(MovementBp: -3000));
    private static readonly EffectDef Marked = new("marked", 1, null, new EffectModifiers());

    private static AbilityDef Self(string id, DisplaceOp op) =>
        Def(id, new AbilityShape(ShapeKind.Self), new EffectOp[] { op }, costResource: "none", range: 0);

    private static AbilityDef Toward(string id, DisplaceOp op) =>
        Def(id, new AbilityShape(ShapeKind.Target), new EffectOp[] { op }, costResource: "none", range: 40000);

    [Fact]
    public void A_self_displace_moves_the_caster_along_its_facing_by_the_distance_and_frees_binds()
    {
        var blink = Self("test.blink", new DisplaceOp(8000, FreesBinds: true));
        var (state, step) = Arrange(blink, boundsHalf: 50000);
        var caster = state.Actors[CasterId];
        Effects.ApplyEffectInstance(caster, Rooted, FoeId, 200, 1, 0);
        Effects.ApplyEffectInstance(caster, Slowed, FoeId, 200, 1, 0);
        Effects.ApplyEffectInstance(caster, Marked, FoeId, 200, 1, 0);

        Tick(state, step, new[] { Cast(CasterId, blink.Id, CasterId) }, _ => { });

        Assert.Equal(8000, caster.Pos.X);
        Assert.Equal(0, caster.Pos.Z);
        Assert.DoesNotContain(caster.Effects, e => e.EffectId is "rooted" or "slowed");
        Assert.Contains(caster.Effects, e => e.EffectId == "marked");
    }

    [Fact]
    public void A_displace_without_freesBinds_leaves_rooted_and_slowed_alone()
    {
        var hop = Self("test.hop", new DisplaceOp(2000));
        var (state, step) = Arrange(hop, boundsHalf: 50000);
        var caster = state.Actors[CasterId];
        Effects.ApplyEffectInstance(caster, Rooted, FoeId, 200, 1, 0);

        Tick(state, step, new[] { Cast(CasterId, hop.Id, CasterId) }, _ => { });

        Assert.Equal(2000, caster.Pos.X);
        Assert.Contains(caster.Effects, e => e.EffectId == "rooted");
    }

    [Fact]
    public void A_displace_is_clamped_to_the_arena_bounds()
    {
        var blink = Self("test.blink", new DisplaceOp(8000));
        var (state, step) = Arrange(blink, boundsHalf: 5000);

        Tick(state, step, new[] { Cast(CasterId, blink.Id, CasterId) }, _ => { });

        Assert.Equal(5000, state.Actors[CasterId].Pos.X);
    }

    [Fact]
    public void A_toward_target_displace_closes_the_distance_and_is_capped_at_it()
    {
        var charge = Toward("test.charge", new DisplaceOp(8000, TowardTarget: true));
        var (state, step) = Arrange(charge, boundsHalf: 50000);
        var foe = state.Actors[FoeId];

        foe.Pos = foe.Pos with { X = 20000 };
        Tick(state, step, new[] { Cast(CasterId, charge.Id, FoeId) }, _ => { });
        Assert.Equal(8000, state.Actors[CasterId].Pos.X);

        foe.Pos = foe.Pos with { X = 9500 };
        state.Actors[CasterId].Pos = new Vec3(0, 0, 0);
        Tick(state, step, new[] { Cast(CasterId, charge.Id, FoeId) }, _ => { });
        Assert.Equal(8000, state.Actors[CasterId].Pos.X);
        Assert.Equal(9500, foe.Pos.X);

        foe.Pos = foe.Pos with { X = 3000 };
        state.Actors[CasterId].Pos = new Vec3(0, 0, 0);
        Tick(state, step, new[] { Cast(CasterId, charge.Id, FoeId) }, _ => { });
        Assert.InRange(state.Actors[CasterId].Pos.X, 2800, 3000);
    }

    [Fact]
    public void A_displace_op_is_a_known_op_to_the_ability_validator()
    {
        var blink = Self("test.blink", new DisplaceOp(8000));
        Assert.Empty(Abilities.ValidateAbilityDef(blink));
    }

    private static (SimState State, StepRules Step) Arrange(AbilityDef def, long boundsHalf)
    {
        var caster = Make(CasterId, ActorKind.Player, "warden", ActorRole.Tank, maxHealth: 10000);
        var foe = Make(FoeId, ActorKind.Enemy, "vault.brute", x: 30000, maxHealth: 5000);
        var state = At(0, caster, foe);
        var effects = new Dictionary<string, EffectDef> { [Rooted.Id] = Rooted, [Slowed.Id] = Slowed, [Marked.Id] = Marked };
        return (state, Rules.Make(Config(AbilityMap(def), effects, boundsHalf: boundsHalf)));
    }
}
