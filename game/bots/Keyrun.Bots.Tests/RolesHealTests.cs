using System.Collections.Generic;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Port of tests/bots/heal-pick.test.ts cases 2 to 7 (case 1, the heal classification, is KitsTests, lane 3A). Real
/// content kits.
/// </summary>
public sealed class RolesHealPickTests
{
    private static ActorState Healer(long health = 250, IEnumerable<(string, int)>? cooldowns = null, string? castId = null, int? castStart = null, int? castEnds = null) =>
        Actor(id: 3, classId: "chanter", role: ActorRole.Healer, health: health, maxHealth: 250, resource: 1, maxResource: 1,
            cooldowns: cooldowns, castId: castId, castStartTick: castStart, castEndsTick: castEnds);

    private static ActorState Tank(long z) =>
        Actor(id: 2, classId: "warden", role: ActorRole.Tank, health: 100, maxHealth: 400, resource: 1, maxResource: 1, z: z);

    private static BotView View(params ActorState[] actors) => ViewOf(10, RealKits, null, actors);

    [Fact(DisplayName = "Chanter shape-aware heals > heals a hurt tank 10m away with the targeted heal, never the self-shaped Refrain")]
    public void TargetedHealAtRange()
    {
        var healer = Healer(cooldowns: new[] { ("chanter.hymn", 999) });
        var tank = Tank(10000);
        Assert.Equal(new Command[] { new CastCommand(10, 3, "chanter.cadence", 2) }, HealerRole.HealerDecide(View(healer, tank), healer));
    }

    [Fact(DisplayName = "Chanter shape-aware heals > heals itself with the self heal")]
    public void SelfHeal()
    {
        var healer = Healer(health: 50);
        Assert.Equal(new Command[] { new CastCommand(10, 3, "chanter.refrain") }, HealerRole.HealerDecide(View(healer), healer));
    }

    [Fact(DisplayName = "Chanter shape-aware heals > uses the group heal for an ally inside its radius")]
    public void GroupHeal()
    {
        var healer = Healer();
        var tank = Tank(3000);
        Assert.Equal(new Command[] { new CastCommand(10, 3, "chanter.chorus") }, HealerRole.HealerDecide(View(healer, tank), healer));
    }

    [Fact(DisplayName = "Chanter shape-aware heals > casts Hymn (the second group heal) when Chorus is down and the ally stands in Hymn's radius")]
    public void HymnInRadius()
    {
        var healer = Healer(cooldowns: new[] { ("chanter.chorus", 999) });
        var tank = Tank(1500);
        Assert.Equal(new Command[] { new CastCommand(10, 3, "chanter.hymn") }, HealerRole.HealerDecide(View(healer, tank), healer));
    }

    [Fact(DisplayName = "Chanter shape-aware heals > walks toward a hurt ally outside Hymn's radius while Hymn is ready")]
    public void WalksIntoHymn()
    {
        var healer = Healer(cooldowns: new[] { ("chanter.chorus", 999) });
        var tank = Tank(10000);
        var cmds = HealerRole.HealerDecide(View(healer, tank), healer);
        Assert.DoesNotContain(cmds, c => c is CastCommand);
        var move = BotTestKit.FirstMove(cmds);
        Assert.True((move?.Dir.Z ?? 0) > 0);
    }

    [Fact(DisplayName = "Chanter shape-aware heals > never walks out of its own cast for Hymn")]
    public void NeverWalksOutOfCast()
    {
        var healer = Healer(cooldowns: new[] { ("chanter.chorus", 999) }, castId: "chanter.cadence", castStart: 0, castEnds: 20);
        var tank = Tank(10000);
        Assert.DoesNotContain(HealerRole.HealerDecide(View(healer, tank), healer), c => c is MoveCommand);
    }
}

/// <summary>Port of tests/bots/healer-triage.test.ts (3 cases, no content registry; the scripted kit heals with "flash-heal").</summary>
public sealed class RolesHealerTriageTests
{
    private static readonly ClassKitView TriageKit = ClassKit(heal: "flash-heal");

    private static ActorState Ally(int id, ActorRole role, long health, IEnumerable<EffectInstance>? effects = null) =>
        Actor(id: id, classId: "priest", role: role, health: health, x: 1, effects: effects);

    private static BotView View(params ActorState[] actors) => ViewOf(10, KitRegistry.Empty, TriageKit, actors);

    [Fact(DisplayName = "healer triage > picks the lowest-health ally under 70% health over a higher one")]
    public void LowestUnderThreshold()
    {
        var healer = Actor(id: 1, classId: "priest", role: ActorRole.Healer);
        var commands = HealerRole.HealerDecide(View(healer, Ally(2, ActorRole.Tank, 40), Ally(3, ActorRole.Damage, 65)), healer);
        var cast = Assert.IsType<CastCommand>(commands[0]);
        Assert.Equal(("flash-heal", (int?)2), (cast.AbilityId, cast.TargetId));
    }

    [Fact(DisplayName = "healer triage > does not heal allies at or above the 70% threshold")]
    public void NoHealAboveThreshold()
    {
        var healer = Actor(id: 1, classId: "priest", role: ActorRole.Healer);
        Assert.Empty(HealerRole.HealerDecide(View(healer, Ally(3, ActorRole.Damage, 90)), healer));
    }

    [Fact(DisplayName = "healer triage > dispels a debuff carrier before healing a lower-health ally")]
    public void DispelFirst()
    {
        var healer = Actor(id: 1, classId: "priest", role: ActorRole.Healer);
        var dps = Ally(3, ActorRole.Damage, 90, new[] { Effect("dispellable:poison", 100, 50) });
        var commands = HealerRole.HealerDecide(View(healer, Ally(2, ActorRole.Tank, 40), dps), healer);
        var cast = Assert.IsType<CastCommand>(commands[0]);
        Assert.Equal(("dispel", (int?)3), (cast.AbilityId, cast.TargetId));
    }
}

/// <summary>Port of tests/bots/interrupt.test.ts cases 2 and 3 (case 1 is InterruptTests, lane 3A).</summary>
public sealed class RolesInterruptTests
{
    private static readonly ClassKitView KickKit = ClassKit(rotation: new[] { "filler" }, interrupt: "kick");

    [Fact(DisplayName = "interrupt assignment > only the lowest-actorId bot with kick ready casts the interrupt")]
    public void LowestKicks()
    {
        var enemy = Actor(id: 100, kind: ActorKind.Enemy, castId: "big-cast");
        var low = Actor(id: 2);
        var high = Actor(id: 3);
        var view = ViewOf(10, KitRegistry.Empty, KickKit, enemy, low, high);

        var lowCast = Assert.IsType<CastCommand>(DamageRole.DamageDecide(view, low)[0]);
        Assert.Equal(("kick", (int?)100), (lowCast.AbilityId, lowCast.TargetId));
        Assert.DoesNotContain("kick", CastIds(DamageRole.DamageDecide(view, high)));
    }

    [Fact(DisplayName = "interrupt assignment > the responsible bot falls back to rotation once its interrupt is on cooldown")]
    public void FallsBackToRotation()
    {
        var enemy = Actor(id: 100, kind: ActorKind.Enemy, castId: "big-cast");
        var low = Actor(id: 2, cooldowns: new[] { ("kick", 999) });
        var cast = Assert.IsType<CastCommand>(DamageRole.DamageDecide(ViewOf(10, KitRegistry.Empty, KickKit, enemy, low), low)[0]);
        Assert.Equal("filler", cast.AbilityId);
    }
}
