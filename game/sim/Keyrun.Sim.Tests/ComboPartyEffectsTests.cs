using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE section 4 "The six Combos": Anthem ("Party gains 15% haste for 6 s.") and Steam ("Burst; a cloud that blinds
/// enemies inside (they miss) for 4 s."). The ops are C#-only (game/content/data/combo-riders.json), so no oracle run
/// carries them. Whether a blinded enemy misses is a separate rule (lane BIBLE-BLIND-MISS); these tests cover who gets
/// the effect and for how long.
/// </summary>
public sealed class ComboPartyEffectsTests
{
    private static readonly EffectDef Hasted = new("hasted", 1, null, new EffectModifiers(HasteBp: 1500));
    private static readonly EffectDef Blinded = new("blinded", 1, "magic", new EffectModifiers());
    private static readonly IReadOnlyDictionary<string, EffectDef> ComboDefs = Defs(Hasted, Blinded);

    private static ActorState Warden() => Make(1, ActorKind.Player, "warden", ActorRole.Tank, x: 0, z: 0);
    private static ActorState Chanter() => Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: -6000, z: 0);
    private static ActorState Cinder() => Make(3, ActorKind.Player, "cinder", ActorRole.Damage, x: 6000, z: 6000);

    private static bool Has(ActorState a, string effectId) => a.Effects.Any(e => e.EffectId == effectId);

    private static EffectInstance Only(ActorState a, string effectId) => Assert.Single(a.Effects, e => e.EffectId == effectId);

    [Fact]
    public void Anthem_party_effect_hastes_every_living_party_member_for_six_seconds_and_no_enemy()
    {
        var warden = Warden();
        var chanter = Chanter();
        var cinder = Cinder();
        var carrier = Make(9, ActorKind.Enemy, "choir", x: 1000, z: 0);
        var state = At(40, warden, chanter, cinder, carrier);

        Combos.ApplyComboEffects(state, warden, carrier, new EffectOp[] { new PartyEffectOp("hasted", 120, 1) }, ComboDefs, _ => { });

        foreach (var member in new[] { warden, chanter, cinder })
        {
            var inst = Only(member, "hasted");
            Assert.Equal(160, inst.EndsTick);
            Assert.Equal(warden.Id, inst.SourceId);
        }
        Assert.False(Has(carrier, "hasted"));
    }

    [Fact]
    public void Anthem_party_effect_skips_a_dead_party_member()
    {
        var warden = Warden();
        var chanter = Chanter();
        chanter.Alive = false;
        var carrier = Make(9, ActorKind.Enemy, "choir", x: 1000, z: 0);
        var state = At(40, warden, chanter, carrier);

        Combos.ApplyComboEffects(state, warden, carrier, new EffectOp[] { new PartyEffectOp("hasted", 120, 1) }, ComboDefs, _ => { });

        Assert.True(Has(warden, "hasted"));
        Assert.False(Has(chanter, "hasted"));
    }

    [Fact]
    public void Steam_cloud_opens_on_the_carrier_and_blinds_each_enemy_inside_it_but_not_outside_or_allies()
    {
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: 1000, z: 1000);
        var carrier = Make(9, ActorKind.Enemy, "choir", x: 2000, z: 0);
        var near = Make(10, ActorKind.Enemy, "choir", x: 4000, z: 2000);
        var far = Make(11, ActorKind.Enemy, "choir", x: 9000, z: 0);
        var state = At(50, chanter, carrier, near, far);

        Combos.ApplyComboEffects(state, chanter, carrier, new EffectOp[] { new CloudOp("blinded", 80, 3000) }, ComboDefs, _ => { });

        var cloud = Assert.Single(state.FloorClouds);
        Assert.Equal(new Vec3(2000, 0, 0), cloud.Pos);
        Assert.Equal(3000, cloud.RadiusMm);
        Assert.Equal(130, cloud.EndsTick);
        Assert.Equal("blinded", cloud.EffectId);
        Assert.Equal(chanter.Id, cloud.SourceId);
        Assert.Equal(130, Only(carrier, "blinded").EndsTick);
        Assert.Equal(130, Only(near, "blinded").EndsTick);
        Assert.False(Has(far, "blinded"));
        Assert.False(Has(chanter, "blinded"));
    }

    [Fact]
    public void Steam_cloud_blinds_an_enemy_that_walks_in_while_it_lasts_and_is_gone_when_it_ends()
    {
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: -5000, z: 0);
        var carrier = Make(9, ActorKind.Enemy, "choir", x: 0, z: 0);
        var walker = Make(10, ActorKind.Enemy, "choir", x: 8000, z: 0);
        var state = At(50, chanter, carrier, walker);
        var rules = Rules.Make(Config(AbilityMap(), ComboDefs));
        Combos.ApplyComboEffects(state, chanter, carrier, new EffectOp[] { new CloudOp("blinded", 80, 3000) }, ComboDefs, _ => { });
        Assert.False(Has(walker, "blinded"));

        Tick(state, rules, Cmds(), _ => { });
        Assert.False(Has(walker, "blinded"));

        walker.Pos = new Vec3(1500, 0, 0);
        Tick(state, rules, Cmds(), _ => { });
        Assert.Equal(130, Only(walker, "blinded").EndsTick);

        while (state.Tick <= 130)
        {
            Tick(state, rules, Cmds(), _ => { });
        }
        Assert.Empty(state.FloorClouds);
        Assert.False(Has(walker, "blinded"));

        var late = Make(11, ActorKind.Enemy, "choir", x: 0, z: 0);
        state.Actors[late.Id] = late;
        Tick(state, rules, Cmds(), _ => { });
        Assert.False(Has(late, "blinded"));
    }

    [Fact]
    public void Cloud_telegraphs_are_derived_separately_from_the_floor_window_telegraphs_bots_read()
    {
        var chanter = Make(2, ActorKind.Player, "chanter", x: 0, z: 0);
        var carrier = Make(9, ActorKind.Enemy, "choir", x: 2000, z: -1000);
        var state = At(50, chanter, carrier);
        Combos.ApplyComboEffects(state, chanter, carrier, new EffectOp[] { new CloudOp("blinded", 80, 3000) }, ComboDefs, _ => { });

        var t = Assert.Single(FloorTelegraphs.DeriveCloudTelegraphs(state));
        Assert.StartsWith(FloorTelegraphs.CloudPrefix, t.Id);
        Assert.Equal(2000, t.CenterX);
        Assert.Equal(-1000, t.CenterZ);
        Assert.Equal(3000, t.Radius);
        Assert.Equal(130, t.ResolvesTick);
        Assert.Empty(FloorTelegraphs.DeriveFloorTelegraphs(state));
    }

    [Fact]
    public void A_cloud_survives_a_state_clone_and_an_actor_merge_and_the_clone_is_independent()
    {
        var chanter = Make(2, ActorKind.Player, "chanter", x: 0, z: 0);
        var carrier = Make(9, ActorKind.Enemy, "choir", x: 2000, z: 0);
        var state = At(50, chanter, carrier);
        Combos.ApplyComboEffects(state, chanter, carrier, new EffectOp[] { new CloudOp("blinded", 80, 3000) }, ComboDefs, _ => { });

        var copy = state.Clone();
        copy.FloorClouds[0].EndsTick = 999;
        Assert.Equal(130, state.FloorClouds[0].EndsTick);

        var merged = RunWiring.MergeActors(state, new[] { Make(12, ActorKind.Enemy, "choir", x: 9000, z: 0) });
        Assert.Equal(130, Assert.Single(merged.FloorClouds).EndsTick);
    }
}
