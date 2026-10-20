using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The drawable shape of a Steam blind cloud (BIBLE section 4 Steam: "a cloud that blinds enemies inside (they miss)
/// for 4 s"; game/content/data/combo-riders.json steam: <c>cloud</c>, 80 ticks, 2500 mm). The arena view draws each
/// <see cref="FloorTelegraphs.DeriveCloudShapes"/> entry: its centre and radius come straight from the sim's
/// <see cref="FloorCloud"/>, and its lit share of life runs from 1000 permille when it opens to 0 when it ends, after
/// which no shape is derived at all.
/// </summary>
public sealed class CloudShapeTests
{
    private static readonly EffectDef Blinded = new("blinded", 1, "magic", new EffectModifiers());
    private static readonly IReadOnlyDictionary<string, EffectDef> ComboDefs = Defs(Blinded);

    // The steam rider's cloud op exactly as game/content/data/combo-riders.json carries it.
    private static CloudOp SteamCloud() => new("blinded", 80, 2500);

    private static SimState OpenSteamCloudAt(int tick, out ActorState carrier)
    {
        var chanter = Make(2, ActorKind.Player, "chanter", ActorRole.Healer, x: -5000, z: 0);
        carrier = Make(9, ActorKind.Enemy, "choir", x: 3000, z: -1500);
        var state = At(tick, chanter, carrier);
        Combos.ApplyComboEffects(state, chanter, carrier, new EffectOp[] { SteamCloud() }, ComboDefs, _ => { });
        return state;
    }

    [Fact]
    public void An_open_steam_cloud_yields_one_shape_at_the_carrier_with_the_rider_radius_and_lifetime()
    {
        var state = OpenSteamCloudAt(50, out var carrier);

        var shape = Assert.Single(FloorTelegraphs.DeriveCloudShapes(state));

        Assert.Equal(FloorTelegraphs.CloudPrefix + Assert.Single(state.FloorClouds).Id, shape.Id);
        Assert.Equal("blinded", shape.EffectId);
        Assert.Equal(carrier.Pos.X, shape.CenterX);
        Assert.Equal(carrier.Pos.Z, shape.CenterZ);
        Assert.Equal(2500, shape.RadiusMm);
        Assert.Equal(50, shape.OpenedTick);
        Assert.Equal(130, shape.EndsTick);
        Assert.Equal(80, shape.LifeTicks);
    }

    [Fact]
    public void The_lit_share_of_life_runs_from_full_at_open_to_none_at_the_end()
    {
        var shape = Assert.Single(FloorTelegraphs.DeriveCloudShapes(OpenSteamCloudAt(50, out _)));

        Assert.Equal(1000, shape.LeftPermille(50));
        Assert.Equal(500, shape.LeftPermille(90));
        Assert.Equal(250, shape.LeftPermille(110));
        Assert.Equal(0, shape.LeftPermille(130));
        Assert.Equal(0, shape.LeftPermille(200));
        Assert.Equal(1000, shape.LeftPermille(10));
    }

    [Fact]
    public void The_shape_is_drawn_every_step_the_cloud_lasts_and_none_once_it_ends()
    {
        var state = OpenSteamCloudAt(50, out _);
        var rules = Rules.Make(Config(AbilityMap(), ComboDefs));
        var drawnSteps = 0;

        while (state.Tick < 130)
        {
            var shape = Assert.Single(FloorTelegraphs.DeriveCloudShapes(state));
            Assert.Equal(2500, shape.RadiusMm);
            Assert.Equal(130 - state.Tick, shape.TicksLeft(state.Tick));
            drawnSteps++;
            Tick(state, rules, Cmds(), _ => { });
        }

        Assert.Equal(80, drawnSteps);
        Assert.Empty(FloorTelegraphs.DeriveCloudShapes(state));
        Tick(state, rules, Cmds(), _ => { });
        Assert.Empty(FloorTelegraphs.DeriveCloudShapes(state));
    }

    [Fact]
    public void A_cloud_whose_end_tick_has_come_but_is_not_yet_swept_draws_nothing()
    {
        var state = OpenSteamCloudAt(50, out _);
        state.Tick = 130;

        Assert.Single(state.FloorClouds);
        Assert.Empty(FloorTelegraphs.DeriveCloudShapes(state));
    }

    [Fact]
    public void A_cloud_whose_id_carries_no_opening_tick_is_drawn_full_from_the_tick_it_is_read()
    {
        var state = At(60);
        state.FloorClouds.Add(new FloorCloud { Id = "odd", EffectId = "blinded", Pos = new Vec3(1, 0, 2), RadiusMm = 2500, EndsTick = 100 });

        var shape = Assert.Single(FloorTelegraphs.DeriveCloudShapes(state));

        Assert.Equal(60, shape.OpenedTick);
        Assert.Equal(1000, shape.LeftPermille(60));
        Assert.Equal(1, shape.CenterX);
        Assert.Equal(2, shape.CenterZ);
    }

    [Fact]
    public void Clouds_are_derived_in_opening_order_and_do_not_reach_the_floor_telegraphs_bots_read()
    {
        var state = OpenSteamCloudAt(50, out _);
        state.Tick = 70;
        var second = Make(12, ActorKind.Enemy, "choir", x: -2000, z: 4000);
        state.Actors[second.Id] = second;
        Combos.ApplyComboEffects(state, state.Actors[2], second, new EffectOp[] { SteamCloud() }, ComboDefs, _ => { });

        var shapes = FloorTelegraphs.DeriveCloudShapes(state);

        Assert.Equal(2, shapes.Count);
        Assert.Equal(50, shapes[0].OpenedTick);
        Assert.Equal(70, shapes[1].OpenedTick);
        Assert.Equal(-2000, shapes[1].CenterX);
        Assert.Equal(4000, shapes[1].CenterZ);
        Assert.Empty(FloorTelegraphs.DeriveFloorTelegraphs(state));
    }
}
