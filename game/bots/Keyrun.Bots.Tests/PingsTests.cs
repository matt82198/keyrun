using System;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.BotTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>Port of tests/bots/pings.test.ts (16 cases).</summary>
public sealed class PingsTests
{
    private const string W = Pings.WindowEffectPrefix;
    private static readonly string Bolt = PingsPolicy.PayoffAbilityPrefix + "bolt";

    private static PingEvent TargetPing(int tick, int actorId, int targetId) => new(tick, actorId, PingKind.Target) { TargetId = targetId };

    private static PingEvent SpotPing(int tick, int actorId, long x, long z) => new(tick, actorId, PingKind.Spot) { Point = new PointMm(x, z) };

    private static ActorState Enemy(int id, string? castId = null, params EffectInstance[] effects) =>
        MakeActor(id: id, kind: ActorKind.Enemy, castId: castId, effects: effects);

    // describe("isPingLive / latestLivePing")

    [Fact(DisplayName = "isPingLive / latestLivePing > a ping is live from its own tick through the window, not before or after")]
    public void LiveWindow()
    {
        var ping = SpotPing(100, 1, 0, 0);
        Assert.False(Pings.IsPingLive(ping, 99, 60));
        Assert.True(Pings.IsPingLive(ping, 100, 60));
        Assert.True(Pings.IsPingLive(ping, 160, 60));
        Assert.False(Pings.IsPingLive(ping, 161, 60));
    }

    [Fact(DisplayName = "isPingLive / latestLivePing > latestLivePing prefers the newer tick, then the lower actorId")]
    public void LatestPrefersNewerThenLower()
    {
        var older = TargetPing(10, 1, 5);
        var newer = TargetPing(20, 2, 5);
        var sameTickHigh = TargetPing(20, 9, 5);
        var sameTickLow = TargetPing(20, 3, 5);
        Assert.Same(newer, Pings.LatestLivePing(new[] { older, newer }, 20));
        Assert.Same(sameTickLow, Pings.LatestLivePing(new[] { sameTickHigh, sameTickLow }, 20));
    }

    [Fact(DisplayName = "isPingLive / latestLivePing > filters by kind and drops stale pings")]
    public void FiltersKindAndStale()
    {
        var spot = SpotPing(0, 1, 0, 0);
        var target = TargetPing(0, 1, 5);
        Assert.Same(target, Pings.LatestLivePing(new[] { spot, target }, 0, 60, PingKind.Target));
        Assert.Null(Pings.LatestLivePing(new[] { spot }, 1000, 60, PingKind.Spot));
    }

    // describe("interruptPingCommands")

    [Fact(DisplayName = "interruptPingCommands > the responsible damage bot kicks the specific enemy named by a target ping")]
    public void ResponsibleKicksPinged()
    {
        var low = MakeActor(id: 2, role: ActorRole.Damage);
        var view = View(MakeState(10, Enemy(100, "big-cast"), Enemy(101, "also-casting"), low), Kit("kick"));
        var commands = PingsPolicy.InterruptPingCommands(view, low, new[] { TargetPing(10, 2, 100) });
        Assert.Equal(new Command[] { new CastCommand(10, 2, "kick", 100) }, commands);
    }

    [Fact(DisplayName = "interruptPingCommands > a higher-actorId bot yields to the lowest-actorId bot with kick ready")]
    public void HigherYieldsToLower()
    {
        var low = MakeActor(id: 2, role: ActorRole.Damage);
        var high = MakeActor(id: 3, role: ActorRole.Damage);
        var view = View(MakeState(10, Enemy(100, "big-cast"), low, high), Kit("kick"));
        var ping = new[] { TargetPing(10, 2, 100) };
        Assert.Empty(PingsPolicy.InterruptPingCommands(view, high, ping));
        var cast = Assert.IsType<CastCommand>(PingsPolicy.InterruptPingCommands(view, low, ping)[0]);
        Assert.Equal("kick", cast.AbilityId);
        Assert.Equal(100, cast.TargetId);
    }

    [Fact(DisplayName = "interruptPingCommands > does nothing once the interrupt is on cooldown")]
    public void NothingOnCooldown()
    {
        var low = MakeActor(id: 2, role: ActorRole.Damage, cooldowns: new[] { ("kick", 999) });
        var view = View(MakeState(10, Enemy(100, "big-cast"), low), Kit("kick"));
        Assert.Empty(PingsPolicy.InterruptPingCommands(view, low, new[] { TargetPing(10, 2, 100) }));
    }

    [Fact(DisplayName = "interruptPingCommands > does nothing when the pinged target is not actually casting")]
    public void NothingWhenNotCasting()
    {
        var low = MakeActor(id: 2, role: ActorRole.Damage);
        var view = View(MakeState(10, Enemy(100, null), low), Kit("kick"));
        Assert.Empty(PingsPolicy.InterruptPingCommands(view, low, new[] { TargetPing(10, 2, 100) }));
    }

    // describe("windowPingPayoffCommands")

    [Fact(DisplayName = "windowPingPayoffCommands > a payoff-capable bot targets and casts its payoff on a pinged Window carrier")]
    public void PayoffTargetsAndCasts()
    {
        var self = MakeActor(id: 2, role: ActorRole.Damage, targetId: null);
        var view = View(MakeState(10, Enemy(100, null, Effect(W + "shatter", 2, 200)), self), Kit(null, "filler", Bolt));
        var commands = PingsPolicy.WindowPingPayoffCommands(view, self, new[] { TargetPing(10, 3, 100) });
        Assert.Equal(new Command[] { new TargetCommand(10, 2, 100), new CastCommand(10, 2, Bolt, 100) }, commands);
    }

    [Fact(DisplayName = "windowPingPayoffCommands > skips the target command when already targeting the pinged carrier")]
    public void SkipsTargetWhenAlreadyTargeting()
    {
        var self = MakeActor(id: 2, role: ActorRole.Damage, targetId: 100);
        var view = View(MakeState(10, Enemy(100, null, Effect(W + "shatter", 2, 200)), self), Kit(null, Bolt));
        Assert.Equal(new Command[] { new CastCommand(10, 2, Bolt, 100) }, PingsPolicy.WindowPingPayoffCommands(view, self, new[] { TargetPing(10, 3, 100) }));
    }

    [Fact(DisplayName = "windowPingPayoffCommands > does nothing for a class with no payoff ability in its rotation")]
    public void NothingWithoutPayoff()
    {
        var self = MakeActor(id: 2, role: ActorRole.Damage);
        var view = View(MakeState(10, Enemy(100, null, Effect(W + "shatter", 2, 200)), self), Kit(null, "filler"));
        Assert.Empty(PingsPolicy.WindowPingPayoffCommands(view, self, new[] { TargetPing(10, 3, 100) }));
    }

    [Fact(DisplayName = "windowPingPayoffCommands > does nothing when the pinged target carries no Window effect")]
    public void NothingWithoutWindow()
    {
        var self = MakeActor(id: 2, role: ActorRole.Damage);
        var view = View(MakeState(10, Enemy(100, null), self), Kit(null, Bolt));
        Assert.Empty(PingsPolicy.WindowPingPayoffCommands(view, self, new[] { TargetPing(10, 3, 100) }));
    }

    // describe("spotPingMoveCommands")

    [Fact(DisplayName = "spotPingMoveCommands > the tank does not move for a spot ping")]
    public void TankHolds()
    {
        var tank = MakeActor(id: 1, role: ActorRole.Tank);
        var view = View(MakeState(10, tank));
        Assert.Empty(PingsPolicy.SpotPingMoveCommands(view, tank, new[] { SpotPing(10, 2, 10, 0) }));
    }

    [Fact(DisplayName = "spotPingMoveCommands > moves a healer/damage bot to a spot ping within PING_MOVE_TICKS, simulated tick by tick")]
    public void ArrivesWithinMoveTicks()
    {
        const long stepMm = 1500;
        var self = MakeActor(id: 2, role: ActorRole.Healer);
        var spot = new PointMm(10000, 0);
        var ticks = 0;
        for (; ticks < PingsPolicy.PingMoveTicks; ticks++)
        {
            var ping = new PingEvent(0, 3, PingKind.Spot) { Point = spot };
            var view = View(MakeState(ticks, self));
            var move = FirstMove(PingsPolicy.SpotPingMoveCommands(view, self, new[] { ping }));
            if (move is null)
            {
                break;
            }
            SetPos(self, ApplyMove(PointMm.Of(self.Pos), move, stepMm));
        }
        Assert.True(ticks < PingsPolicy.PingMoveTicks);
        var dx = (double)(spot.X - self.Pos.X);
        var dz = (double)(spot.Z - self.Pos.Z);
        Assert.True(Math.Sqrt((dx * dx) + (dz * dz)) <= 500);
    }

    [Fact(DisplayName = "spotPingMoveCommands > stops producing move commands once the ping goes stale")]
    public void StopsWhenStale()
    {
        var self = MakeActor(id: 2, role: ActorRole.Damage);
        var view = View(MakeState(PingsPolicy.PingMoveTicks + 1, self));
        Assert.Empty(PingsPolicy.SpotPingMoveCommands(view, self, new[] { SpotPing(0, 3, 10, 0) }));
    }

    // describe("pingReactionCommands priority")

    [Fact(DisplayName = "pingReactionCommands priority > prefers the interrupt reaction over a Window payoff and a spot move")]
    public void InterruptFirst()
    {
        var self = MakeActor(id: 2, role: ActorRole.Damage);
        var view = View(MakeState(10, Enemy(100, "big-cast", Effect(W + "shatter", 2, 200)), self), Kit("kick", Bolt));
        var commands = PingsPolicy.PingReactionCommands(view, self, new[] { TargetPing(10, 3, 100), SpotPing(10, 3, 50, 50) });
        Assert.Equal(new Command[] { new CastCommand(10, 2, "kick", 100) }, commands);
    }

    [Fact(DisplayName = "pingReactionCommands priority > falls back to a spot move when no interrupt or payoff reaction applies")]
    public void FallsBackToSpotMove()
    {
        var self = MakeActor(id: 2, role: ActorRole.Damage);
        var view = View(MakeState(10, self));
        var commands = PingsPolicy.PingReactionCommands(view, self, new[] { SpotPing(10, 3, 5000, 0) });
        Assert.Single(commands);
        Assert.Equal("move", commands.Single().Kind);
    }
}
