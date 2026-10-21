using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// <see cref="FloorCloud.Id"/> promises a unique id. One Payoff actor can close two Steam combos in one tick (a cast
/// completing at the tick plus an instant Payoff in that tick's commands, Rules.Make), and the id used to be only
/// effectId:sourceId:tick, so the two clouds shared it and their <see cref="FloorTelegraphs.DeriveCloudTelegraphs"/>
/// ids collided. These tests pin: same-tick clouds get distinct ids, the first keeps the old id (no hash moves), and
/// every id still ends in ":&lt;opening tick&gt;".
/// </summary>
public sealed class CloudIdUniquenessTests
{
    private static readonly EffectDef Blinded = new("blinded", 1, "magic", new EffectModifiers());
    private static readonly IReadOnlyDictionary<string, EffectDef> ComboDefs = Defs(Blinded);

    private static SimState OpenThreeSameTickClouds()
    {
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: 0, z: 0);
        var a = Make(9, ActorKind.Enemy, "choir", x: 2000, z: 0);
        var b = Make(10, ActorKind.Enemy, "choir", x: 9000, z: 0);
        var c = Make(11, ActorKind.Enemy, "choir", x: -9000, z: 0);
        var state = At(50, chanter, a, b, c);
        foreach (var carrier in new[] { a, b, c })
        {
            Combos.ApplyComboEffects(state, chanter, carrier, new EffectOp[] { new CloudOp("blinded", 80, 2500) }, ComboDefs, _ => { });
        }
        return state;
    }

    [Fact]
    public void Two_clouds_opened_by_one_source_in_one_tick_get_distinct_ids()
    {
        var state = OpenThreeSameTickClouds();

        Assert.Equal(3, state.FloorClouds.Count);
        Assert.Equal(3, state.FloorClouds.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Cloud_telegraphs_for_same_tick_clouds_are_all_present_with_distinct_ids()
    {
        var state = OpenThreeSameTickClouds();

        var telegraphs = FloorTelegraphs.DeriveCloudTelegraphs(state);

        Assert.Equal(3, telegraphs.Count);
        Assert.Equal(3, telegraphs.Select(t => t.Id).Distinct().Count());
        Assert.Equal(new long[] { 2000, 9000, -9000 }, telegraphs.Select(t => t.CenterX).ToArray());
    }

    [Fact]
    public void The_first_cloud_keeps_the_original_id_and_every_id_ends_in_the_opening_tick()
    {
        var state = OpenThreeSameTickClouds();

        Assert.Equal("blinded:2:50", state.FloorClouds[0].Id);
        Assert.All(state.FloorClouds, c => Assert.EndsWith(":50", c.Id));
        Assert.All(state.FloorClouds, c => Assert.StartsWith("blinded:2", c.Id));
    }

    [Fact]
    public void Same_source_clouds_on_different_ticks_keep_the_plain_id()
    {
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: 0, z: 0);
        var carrier = Make(9, ActorKind.Enemy, "choir", x: 2000, z: 0);
        var state = At(50, chanter, carrier);
        Combos.ApplyComboEffects(state, chanter, carrier, new EffectOp[] { new CloudOp("blinded", 80, 2500) }, ComboDefs, _ => { });
        state.Tick = 51;
        Combos.ApplyComboEffects(state, chanter, carrier, new EffectOp[] { new CloudOp("blinded", 80, 2500) }, ComboDefs, _ => { });

        Assert.Equal(new[] { "blinded:2:50", "blinded:2:51" }, state.FloorClouds.Select(c => c.Id).ToArray());
    }
}
