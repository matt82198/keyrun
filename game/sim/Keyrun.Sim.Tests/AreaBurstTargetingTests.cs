using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// WILDFIRE-NEVER-ENDS: a hostile circle tagged "aoe" (Cinder Wildfire, the Widening Flare facet: "Flare becomes a
/// small area instead of single target") bursts at the caster's target, so it lands from the spell's own range.
/// Centred on the caster, the 1.5 m Wildfire burst hit nothing from 25 m: no damage, no Kindled, and the all-Cinder bot
/// runs never ended (game/bots/Keyrun.Bots.Tests/WildfireRunEndsTests.cs). Untagged and friendly circles keep the
/// caster-centred TS rule (targeting.ts:130).
/// </summary>
public sealed class AreaBurstTargetingTests
{
    private static readonly EffectOp Dmg = new DamageOp(110, "fire");

    /// <summary>Flare under Wildfire: 25 m range, a 1.5 m circle, tagged aoe.</summary>
    private static AbilityDef Wildfire(params string[] tags) =>
        Ability(new AbilityShape(ShapeKind.Circle, Radius: 1500), new[] { Dmg }, id: "cinder.flare") with
        {
            Range = 25000,
            Tags = tags,
        };

    /// <summary>A Cinder 20 m from its target, which has a second enemy 1 m beside it and one 5 m away.</summary>
    private static (SimState State, ActorState Caster) Scene(int? targetId = 2)
    {
        var caster = Actor(1, ActorKind.Player, 0, 0, classId: "cinder");
        var target = Actor(2, ActorKind.Enemy, 0, 20000);
        var beside = Actor(3, ActorKind.Enemy, 1000, 20000);
        var far = Actor(4, ActorKind.Enemy, 5000, 20000);
        caster.TargetId = targetId;
        return (State(caster, target, beside, far), caster);
    }

    [Fact(DisplayName = "area burst > a hostile aoe circle bursts at the target from range, hitting the target and its neighbour")]
    public void Hostile_aoe_circle_bursts_at_the_target()
    {
        var (state, caster) = Scene();
        var hit = Targeting.ResolveTargets(state, caster, Wildfire("setup:kindled", "aoe"), Affinity.Hostile);
        Assert.Equal(new[] { 2, 3 }, hit.Select(a => a.Id));
    }

    [Fact(DisplayName = "area burst > an untagged hostile circle (Cinderfield) still centres on the caster")]
    public void Untagged_hostile_circle_centres_on_the_caster()
    {
        var (state, caster) = Scene();
        Assert.Empty(Targeting.ResolveTargets(state, caster, Wildfire("setup:kindled"), Affinity.Hostile));
    }

    [Fact(DisplayName = "area burst > a friendly aoe circle (Group Song) still centres on the caster")]
    public void Friendly_aoe_circle_centres_on_the_caster()
    {
        var (state, caster) = Scene();
        var hit = Targeting.ResolveTargets(state, caster, Wildfire("aoe"), Affinity.Friendly);
        Assert.Equal(new[] { 1 }, hit.Select(a => a.Id));
    }

    [Fact(DisplayName = "area burst > with no target, or a target beyond range, the burst falls back to the caster")]
    public void No_valid_target_falls_back_to_the_caster()
    {
        var (noTarget, c1) = Scene(targetId: null);
        Assert.Empty(Targeting.ResolveTargets(noTarget, c1, Wildfire("aoe"), Affinity.Hostile));

        var (state, c2) = Scene();
        var shortRange = Wildfire("aoe") with { Range = 19999 };
        Assert.Empty(Targeting.ResolveTargets(state, c2, shortRange, Affinity.Hostile));
        Assert.Equal(new[] { 2, 3 }, Targeting.ResolveTargets(state, c2, shortRange with { Range = 20000 }, Affinity.Hostile).Select(a => a.Id));
    }
}
