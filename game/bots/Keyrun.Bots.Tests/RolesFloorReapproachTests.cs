using System;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>Port of tests/bots/floor-reapproach.test.ts (4 cases, no content registry).</summary>
public sealed class RolesFloorReapproachTests
{
    /// <summary>Mirrors roles/healer.ts HEAL_RANGE_MM (the TS test keeps its own copy too).</summary>
    private const long HealRangeMm = 30 * 1000;

    private static BotView View(int tick, BotTelegraph[] telegraphs, params ActorState[] actors) =>
        ViewOf(tick, KitRegistry.Empty, null, telegraphs, actors);

    [Fact(DisplayName = "melee re-approach > flees the floor patch instead of chasing its target while still standing in it")]
    public void FleesBeforeChasing()
    {
        var target = Actor(id: 100, kind: ActorKind.Enemy, classId: "warden");
        var patch = Circle(Floor.FloorPatchPrefix + "fire", 0, 2, 3);
        var withTarget = Actor(id: 1, classId: "warden", z: 2, targetId: 100);
        var view = View(10, new[] { patch }, withTarget, target);

        var commands = DamageRole.DamageDecide(view, withTarget);
        var escape = Floor.FloorEscapeCommand(view, withTarget);
        Assert.NotNull(escape);
        Assert.Equal(new Command[] { escape! }, commands);
    }

    [Fact(DisplayName = "melee re-approach > re-approaches its target once no longer clearing any floor patch")]
    public void ReapproachesTarget()
    {
        var target = Actor(id: 100, kind: ActorKind.Enemy, classId: "warden");
        var self = Actor(id: 1, classId: "warden", z: 10000, targetId: 100);
        var move = BotTestKit.FirstMove(DamageRole.DamageDecide(View(10, Array.Empty<BotTelegraph>(), self, target), self));
        Assert.NotNull(move);
        Assert.True(move!.Dir.Z < 0);
    }

    [Fact(DisplayName = "melee re-approach > a ranged class does not chase its target (only tank/warden are treated as melee)")]
    public void RangedDoesNotChase()
    {
        var target = Actor(id: 100, kind: ActorKind.Enemy, classId: "warden");
        var self = Actor(id: 1, classId: "cinder", z: 10000, targetId: 100);
        Assert.Empty(DamageRole.DamageDecide(View(10, Array.Empty<BotTelegraph>(), self, target), self));
    }

    [Fact(DisplayName = "healer keeps range while escaping a floor patch > ends up within heal range of the party after clearing a patch centred on it")]
    public void HealerKeepsRange()
    {
        const long stepMm = 1000;
        const int maxTicks = 40;
        var patch = Circle(Floor.FloorPatchPrefix + "fire", -29000, 0, 2000);
        var inflated = patch with { Danger = true, Radius = 2500 };
        var pos = new PointMm(-29000, 0);
        var ticks = 0;
        for (; ticks < maxTicks; ticks++)
        {
            if (!Positioning.IsInAnyDanger(pos, new[] { inflated }))
            {
                break;
            }
            var healer = Actor(id: 1, classId: "chanter", role: ActorRole.Healer, x: pos.X, z: pos.Z);
            var ally = Actor(id: 2, classId: "warden", role: ActorRole.Damage);
            var move = BotTestKit.FirstMove(HealerRole.HealerDecide(View(ticks, new[] { patch }, healer, ally), healer));
            Assert.NotNull(move);
            pos = BotTestKit.ApplyMove(pos, move, stepMm);
        }

        Assert.True(ticks < maxTicks);
        var distToAlly = Math.Sqrt(((double)pos.X * pos.X) + ((double)pos.Z * pos.Z));
        Assert.True(distToAlly <= HealRangeMm + 2000, $"ended {distToAlly} mm from the ally");
    }
}
