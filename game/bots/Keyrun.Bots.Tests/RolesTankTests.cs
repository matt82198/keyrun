using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>Port of tests/bots/tank-bulwark.test.ts (5 cases, real content kit).</summary>
public sealed class RolesTankBulwarkTests
{
    private const int Tick = 10;
    private static readonly Command Bulwark = new CastCommand(Tick, 2, "warden.bulwark");

    private static ActorState Tank(IEnumerable<(string, int)>? cooldowns = null, IEnumerable<EffectInstance>? effects = null,
        string? castId = null, int? castStart = null, int? castEnds = null) =>
        Actor(id: 2, classId: "warden", role: ActorRole.Tank, health: 400, maxHealth: 400, resource: 0, maxResource: 100, targetId: 100,
            cooldowns: cooldowns ?? new[] { ("warden.brace", 999) }, effects: effects, castId: castId, castStartTick: castStart, castEndsTick: castEnds);

    private static ActorState Enemy(int id, int targetId) =>
        Actor(id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, z: 1500, yaw: 512, health: 6720, maxHealth: 6720,
            resource: 0, maxResource: 0, targetId: targetId, threat: new[] { (2, 100L) });

    private static List<Command> Decide(ActorState self, params ActorState[] others) =>
        TankRole.TankDecide(ViewOf(Tick, RealKits, null, new[] { self }.Concat(others).ToArray()), self).ToList();

    private static ActorState[] Pack() => new[] { Enemy(100, 2), Enemy(101, 2), Enemy(102, 2) };

    [Fact(DisplayName = "Warden tank keeps Bulwark up under pack pressure > casts Bulwark, ahead of the rotation, when three enemies are aimed at it and Braced is down")]
    public void CastsBulwarkFirst()
    {
        var cmds = Decide(Tank(), Pack());
        Assert.Contains(Bulwark, cmds);
        Assert.Equal(Bulwark, cmds.OfType<CastCommand>().First());
    }

    [Fact(DisplayName = "Warden tank keeps Bulwark up under pack pressure > does not recast Bulwark while its Braced effect is still up")]
    public void NoRecastWhileBraced() =>
        Assert.DoesNotContain(Bulwark, Decide(Tank(effects: new[] { Effect("braced", 2, Tick + 100) }), Pack()));

    [Fact(DisplayName = "Warden tank keeps Bulwark up under pack pressure > does not cast Bulwark with only two attackers (no pack pressure)")]
    public void NoBulwarkTwoAttackers() =>
        Assert.DoesNotContain(Bulwark, Decide(Tank(), Enemy(100, 2), Enemy(101, 2)));

    [Fact(DisplayName = "Warden tank keeps Bulwark up under pack pressure > does not cast Bulwark while it is on cooldown")]
    public void NoBulwarkOnCooldown() =>
        Assert.DoesNotContain(Bulwark, Decide(Tank(cooldowns: new[] { ("warden.brace", 999), ("warden.bulwark", Tick + 50) }), Pack()));

    [Fact(DisplayName = "Warden tank keeps Bulwark up under pack pressure > never clips its own cast to start Bulwark")]
    public void NeverClipsOwnCast() =>
        Assert.DoesNotContain(Bulwark, Decide(Tank(castId: "warden.bulwark", castStart: Tick - 5, castEnds: Tick + 15), Pack()));
}

/// <summary>Port of tests/bots/tank-engage.test.ts (4 cases, real content kit).</summary>
public sealed class RolesTankEngageTests
{
    private static ActorState Warden(int id = 2, int yaw = 0, long resource = 0, int? targetId = null, IEnumerable<(string, int)>? cooldowns = null) =>
        Actor(id: id, classId: "warden", role: ActorRole.Tank, yaw: yaw, health: 400, maxHealth: 400, resource: resource, maxResource: 100,
            targetId: targetId, cooldowns: cooldowns);

    private static ActorState Ally() =>
        Actor(id: 3, classId: "cinder", role: ActorRole.Damage, health: 250, maxHealth: 250, maxResource: 100, z: 4000);

    private static ActorState Enemy(long z, int targetId, IEnumerable<(int, long)>? threat = null) =>
        Actor(id: 100, kind: ActorKind.Enemy, classId: "choir.melee", role: ActorRole.None, z: z, health: 400, maxHealth: 400, maxResource: 100,
            targetId: targetId, threat: threat);

    private static IReadOnlyList<Command> Decide(ActorState tank, params ActorState[] others) =>
        TankRole.TankDecide(ViewOf(10, RealKits, null, new[] { tank }.Concat(others).ToArray()), tank);

    [Fact(DisplayName = "Warden tank engage > taunts a loose enemy (aimed at another player) within taunt range")]
    public void TauntsLooseEnemy()
    {
        var tank = Warden();
        Assert.Contains(new CastCommand(10, 2, "warden.taunt", 100), Decide(tank, Ally(), Enemy(2000, 3)));
    }

    [Fact(DisplayName = "Warden tank engage > walks toward its target when it is beyond melee reach")]
    public void WalksToTarget()
    {
        var tank = Warden(cooldowns: new[] { ("warden.taunt", 999) });
        var move = BotTestKit.FirstMove(Decide(tank, Ally(), Enemy(9000, 3)));
        Assert.NotNull(move);
        Assert.True(move!.Dir.Z > 0);
    }

    [Fact(DisplayName = "Warden tank engage > in reach of a held enemy: faces it and casts a rotation ability at it (never Vigil)")]
    public void FacesAndCasts()
    {
        var tank = Warden(yaw: 768, resource: 60, targetId: 100);
        var cmds = Decide(tank, Ally(), Enemy(2000, 2, new[] { (2, 10L) }));
        Assert.Equal(256, BotTestKit.FirstMove(cmds)?.Yaw);
        var cast = cmds.OfType<CastCommand>().FirstOrDefault();
        Assert.NotNull(cast);
        Assert.Contains(cast!.AbilityId, new[] { "warden.strike", "warden.guardBreak", "warden.bulwark", "warden.reckoning" });
        Assert.Equal(100, cast.TargetId);
    }

    [Fact(DisplayName = "Warden tank engage > holdsThreat: needs an entry at least as high as every other one")]
    public void HoldsThreat()
    {
        Assert.False(TankEngage.HoldsThreat(Warden(id: 1), 2));
        Assert.True(TankEngage.HoldsThreat(Actor(threat: new[] { (2, 0L) }), 2));
        Assert.False(TankEngage.HoldsThreat(Actor(threat: new[] { (2, 10L), (3, 50L) }), 2));
    }
}

/// <summary>Port of tests/bots/tank-facing.test.ts (4 cases, no content registry; scripted defensive "shield-wall").</summary>
public sealed class RolesTankFacingTests
{
    private static readonly ClassKitView FacingKit = ClassKit(defensive: "shield-wall");

    private static ActorState Tank(long z = 0, long health = 100) => Actor(id: 1, role: ActorRole.Tank, z: z, health: health);

    private static IReadOnlyList<Command> Decide(ActorState tank, params ActorState[] others) =>
        TankRole.TankDecide(ViewOf(10, KitRegistry.Empty, FacingKit, new[] { tank }.Concat(others).ToArray()), tank);

    [Fact(DisplayName = "tank state machine > acquires the enemy with the highest threat deficit")]
    public void AcquiresHighestDeficit()
    {
        var tank = Tank(z: -5);
        var offTarget = Actor(id: 10, kind: ActorKind.Enemy, role: ActorRole.Tank, threat: new[] { (1, 10L), (3, 50L) });
        var onTarget = Actor(id: 11, kind: ActorKind.Enemy, role: ActorRole.Tank, threat: new[] { (1, 100L) });
        Assert.Contains(new TargetCommand(10, 1, 10), Decide(tank, offTarget, onTarget));
    }

    [Fact(DisplayName = "tank state machine > turns to face away from the party centroid")]
    public void FacesAwayFromParty()
    {
        var tank = Tank(z: -5);
        var ally = Actor(id: 2, role: ActorRole.Damage);
        var move = BotTestKit.FirstMove(Decide(tank, ally));
        Assert.NotNull(move);
        Assert.Equal(768, move!.Yaw);
    }

    [Fact(DisplayName = "tank state machine > pops its defensive under the 35% health floor")]
    public void DefensiveUnderFloor() => Assert.Contains("shield-wall", CastIds(Decide(Tank(health: 30))));

    [Fact(DisplayName = "tank state machine > does not pop its defensive above the health floor")]
    public void NoDefensiveAboveFloor() => Assert.Empty(CastIds(Decide(Tank(health: 80))));
}

/// <summary>Port of tests/bots/tank-momentum.test.ts (5 cases, real content kit).</summary>
public sealed class RolesTankMomentumTests
{
    private const int Tick = 1000;

    private static (string, int)[] BraceOnCd(params (string, int)[] more) => new[] { ("warden.brace", Tick + 245) }.Concat(more).ToArray();

    private static ActorState Tank(IEnumerable<EffectInstance>? effects = null, (string, int)[]? cooldowns = null) =>
        Actor(id: 2, classId: "warden", role: ActorRole.Tank, health: 310, maxHealth: 400, resource: 100, maxResource: 100, targetId: 100,
            cooldowns: cooldowns ?? BraceOnCd(), effects: effects);

    private static ActorState Enemy(int id) =>
        Actor(id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, health: 6720, maxHealth: 6720, resource: 0,
            maxResource: 100, z: 2000, targetId: 2, threat: new[] { (2, 50L) });

    private static List<string> Casts(ActorState tank, int enemies)
    {
        var actors = new List<ActorState> { tank };
        for (var i = 0; i < enemies; i++)
        {
            actors.Add(Enemy(100 + i));
        }
        return CastIds(TankRole.TankDecide(ViewOf(Tick, RealKits, null, actors.ToArray()), tank));
    }

    [Fact(DisplayName = "Warden tank momentum under pack pressure > pressured, 100 momentum, Brace about to expire -> casts Vigil, not Reckoning")]
    public void VigilAsBraceExpires()
    {
        var ids = Casts(Tank(effects: new[] { Effect("braceSelf", 2, Tick + 5) }), 5);
        Assert.Contains("warden.vigil", ids);
        Assert.DoesNotContain("warden.reckoning", ids);
    }

    [Fact(DisplayName = "Warden tank momentum under pack pressure > pressured, 100 momentum, Brace down -> casts Vigil, not Reckoning")]
    public void VigilWhenBraceDown()
    {
        var ids = Casts(Tank(), 3);
        Assert.Contains("warden.vigil", ids);
        Assert.DoesNotContain("warden.reckoning", ids);
    }

    [Fact(DisplayName = "Warden tank momentum under pack pressure > pressured with Brace fresh -> holds momentum: neither Reckoning nor Vigil yet")]
    public void HoldsMomentumBraceFresh()
    {
        var ids = Casts(Tank(effects: new[] { Effect("braceSelf", 2, Tick + 50) }), 5);
        Assert.DoesNotContain("warden.reckoning", ids);
        Assert.DoesNotContain("warden.vigil", ids);
        Assert.NotEmpty(ids);
    }

    [Fact(DisplayName = "Warden tank momentum under pack pressure > pressured with Vigil on cooldown -> still holds momentum (no Reckoning)")]
    public void HoldsMomentumVigilCooling()
    {
        var ids = Casts(Tank(cooldowns: BraceOnCd(("warden.vigil", Tick + 60)), effects: new[] { Effect("bracedSelf", 2, Tick + 40) }), 5);
        Assert.DoesNotContain("warden.reckoning", ids);
    }

    [Fact(DisplayName = "Warden tank momentum under pack pressure > unpressured (one attacker), 100 momentum, 310/400 HP -> Reckoning stays the finisher")]
    public void ReckoningUnpressured()
    {
        var ids = Casts(Tank(), 1);
        Assert.Contains("warden.reckoning", ids);
        Assert.DoesNotContain("warden.vigil", ids);
    }
}

/// <summary>Port of tests/bots/tank-pressure.test.ts (4 cases, real content kit).</summary>
public sealed class RolesTankPressureTests
{
    private static readonly Command Brace = new CastCommand(10, 2, "warden.brace");

    private static ActorState Tank(long health = 400) =>
        Actor(id: 2, classId: "warden", role: ActorRole.Tank, yaw: 256, health: health, maxHealth: 400, maxResource: 100, targetId: 100);

    private static ActorState Enemy(int id, int targetId) =>
        Actor(id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, yaw: 256, health: 400, maxHealth: 400, maxResource: 100,
            z: 2000, targetId: targetId, threat: new[] { (2, 0L) });

    private static IReadOnlyList<Command> Decide(ActorState tank, params ActorState[] others) =>
        TankRole.TankDecide(ViewOf(10, RealKits, null, new[] { tank }.Concat(others).ToArray()), tank);

    [Fact(DisplayName = "Warden tank pack pressure > braces at full health once three enemies are aimed at it")]
    public void BracesUnderPack() => Assert.Contains(Brace, Decide(Tank(), Enemy(100, 2), Enemy(101, 2), Enemy(102, 2)));

    [Fact(DisplayName = "Warden tank pack pressure > does not brace at full health with only two attackers")]
    public void NoBraceTwoAttackers() => Assert.DoesNotContain(Brace, Decide(Tank(), Enemy(100, 2), Enemy(101, 2)));

    [Fact(DisplayName = "Warden tank pack pressure > counts only enemies aimed at the tank (three enemies, one on the tank -> no brace)")]
    public void CountsOnlyAimedAtTank()
    {
        var healer = Actor(id: 3, classId: "chanter", role: ActorRole.Healer, yaw: 256, health: 400, maxHealth: 400, maxResource: 100, z: -4000);
        Assert.DoesNotContain(Brace, Decide(Tank(), healer, Enemy(100, 2), Enemy(101, 3), Enemy(102, 3)));
    }

    [Fact(DisplayName = "Warden tank pack pressure > still braces under the 35% health floor with a single attacker")]
    public void BracesUnderFloor() => Assert.Contains(Brace, Decide(Tank(health: 100), Enemy(100, 2)));
}
