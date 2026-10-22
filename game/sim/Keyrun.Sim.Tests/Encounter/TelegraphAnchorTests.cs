using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// TELEGRAPH-ANCHOR (finding f-ebb50027; BIBLE section 3 "shape plus colour, drawn ahead of resolution", BIBLE 9.3
/// Searing Hex "circle under the target"): a target-anchored script telegraph is drawn, and resolved, at the caster's
/// current target, in integer mm; on a tick the caster has no living target it is not drawn at all, because a shape
/// at the caster would be a lie about where the cast lands.
/// </summary>
public sealed class TelegraphAnchorTests
{
    private static readonly Vec3 TargetPos = new(3000, 0, 4000);

    private static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> HexScripts(bool anchorTarget) => EncounterTestKit.Scripts(1,
        ScriptStep.Loop(100, "ash.searingHex") with { Telegraph = new TelegraphSpec("circle", 40) { Radius = 1200, AnchorTarget = anchorTarget } });

    private static SimState StateAt(int tick, int? targetId, bool targetAlive = true)
    {
        var caster = EncounterTestKit.Actor(1, ActorKind.Enemy, new Vec3(-500, 0, 250));
        caster.TargetId = targetId;
        return EncounterTestKit.State(tick, caster, EncounterTestKit.Actor(2, ActorKind.Player, TargetPos, alive: targetAlive));
    }

    private static Vec3 OriginOf(SimEvent e)
    {
        var o = (JsObject)e.Get("origin")!;
        return new Vec3(((JsInt)o.Get("x")!).Value, ((JsInt)o.Get("y")!).Value, ((JsInt)o.Get("z")!).Value);
    }

    [Fact]
    public void A_target_anchored_telegraph_is_drawn_at_the_casters_current_target()
    {
        var r = Scripts.StepEnemyScripts(StateAt(60, targetId: 2), HexScripts(anchorTarget: true));
        var tg = Assert.Single(r.TelegraphEvents);
        Assert.Equal(TargetPos, OriginOf(tg));
        Assert.Equal(100, ((JsInt)tg.Get("resolvesTick")!).Value);
        Assert.Single(r.TelegraphCastStarts);
        // The registry entry is what both the hit resolution (Telegraphs.Resolve) and the view read: TelegraphView.Spawn
        // places the floor shape at Telegraph.Origin, so the drawn shape follows the event's anchored origin.
        Assert.Equal(TargetPos, Telegraphs.Register(tg, 1).Origin);
    }

    [Fact]
    public void A_caster_anchored_telegraph_stays_at_the_caster()
    {
        var r = Scripts.StepEnemyScripts(StateAt(60, targetId: 2), HexScripts(anchorTarget: false));
        Assert.Equal(new Vec3(-500, 0, 250), OriginOf(Assert.Single(r.TelegraphEvents)));
    }

    [Fact]
    public void No_target_means_no_telegraph_never_one_at_the_caster()
    {
        var r = Scripts.StepEnemyScripts(StateAt(60, targetId: null), HexScripts(anchorTarget: true));
        Assert.Empty(r.TelegraphEvents);
        Assert.Empty(r.TelegraphCastStarts);
    }

    [Fact]
    public void A_dead_target_means_no_telegraph()
    {
        var r = Scripts.StepEnemyScripts(StateAt(60, targetId: 2, targetAlive: false), HexScripts(anchorTarget: true));
        Assert.Empty(r.TelegraphEvents);
        Assert.Empty(r.TelegraphCastStarts);
    }

    [Fact]
    public void The_json_anchor_parses_target_and_caster_and_rejects_anything_else()
    {
        static TelegraphSpec Parse(string anchor) => EncounterJson.ParseScript(JsValue.Parse(
            "[{\"at\":\"loop\",\"every\":100,\"cast\":\"x\",\"telegraph\":{\"shape\":\"circle\",\"radius\":1200,\"leadTicks\":40" + anchor + "}}]"))[0].Telegraph!;
        Assert.True(Parse(",\"anchor\":\"target\"").AnchorTarget);
        Assert.False(Parse(",\"anchor\":\"caster\"").AnchorTarget);
        Assert.False(Parse("").AnchorTarget);
        Assert.Throws<FormatException>(() => Parse(",\"anchor\":\"ground\""));
    }
}
