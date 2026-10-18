using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>Port of tests/bots/damage-behavior.test.ts (8 cases, real content kits).</summary>
public sealed class RolesDamageBehaviorTests
{
    private static ActorState Make(int id = 1, ActorKind kind = ActorKind.Player, string classId = "cinder", long resource = 0, long x = 0,
        int? targetId = null, IEnumerable<(string, int)>? cooldowns = null, IEnumerable<EffectInstance>? effects = null) =>
        Actor(id: id, kind: kind, classId: classId, role: ActorRole.Damage, resource: resource, maxResource: 100, x: x, targetId: targetId,
            cooldowns: cooldowns, effects: effects);

    private static BotView View(params ActorState[] actors) => ViewOf(10, RealKits, null, actors);

    [Fact(DisplayName = "Payoff play: only closes another class's open Window > a Cinder bot targets and casts its Payoff on a Window a Warden opened")]
    public void PayoffOnWardenWindow()
    {
        var warden = Make(id: 2, classId: "warden");
        var enemy = Make(id: 100, kind: ActorKind.Enemy, effects: new[] { Effect(Pings.WindowEffectPrefix + "sundered", 2, 200) });
        var cinder = Make(id: 5, classId: "cinder", resource: 50);
        Assert.Equal(
            new Command[] { new TargetCommand(10, 5, 100), new CastCommand(10, 5, "cinder.coldSnap", 100) },
            DamageRole.DamageDecide(View(warden, enemy, cinder), cinder));
    }

    [Fact(DisplayName = "Payoff play: only closes another class's open Window > The Lock: a Cinder bot will not treat a Window another Cinder opened as closable")]
    public void LockBlocksOwnClassWindow()
    {
        var otherCinder = Make(id: 2, classId: "cinder");
        var enemy = Make(id: 100, kind: ActorKind.Enemy, effects: new[] { Effect(Pings.WindowEffectPrefix + "kindled", 2, 200) });
        var cinder = Make(id: 5, classId: "cinder", resource: 0);
        Assert.DoesNotContain(DamageRole.DamageDecide(View(otherCinder, enemy, cinder), cinder), c => c is TargetCommand);
    }

    [Fact(DisplayName = "Setup play: only opens a Window when a Payoff-capable ally is close enough > a Warden bot Setups when a Cinder ally is within range and no Window is open")]
    public void SetupWithAllyInRange()
    {
        var cinderAlly = Make(id: 5, classId: "cinder", x: 10);
        var enemy = Make(id: 100, kind: ActorKind.Enemy);
        var warden = Make(id: 2, classId: "warden", resource: 50);
        Assert.Equal(
            new Command[] { new TargetCommand(10, 2, 100), new CastCommand(10, 2, "warden.strike", 100) },
            DamageRole.DamageDecide(View(cinderAlly, enemy, warden), warden));
    }

    [Fact(DisplayName = "Setup play: only opens a Window when a Payoff-capable ally is close enough > does not Setup when no Payoff-capable ally is in range (falls back to its own rotation)")]
    public void NoSetupAllyFar()
    {
        var farCinderAlly = Make(id: 5, classId: "cinder", x: 40000);
        var enemy = Make(id: 100, kind: ActorKind.Enemy);
        var warden = Make(id: 2, classId: "warden", resource: 50);
        var ids = CastIds(DamageRole.DamageDecide(View(farCinderAlly, enemy, warden), warden));
        Assert.DoesNotContain("warden.strike", ids);
        Assert.DoesNotContain("warden.bulwark", ids);
    }

    private static EffectiveKit HeatKit() => new()
    {
        Rotation = new[] { "builder", "dump" },
        InterruptAbilityId = null,
        DefensiveAbilityId = null,
        HealAbilityId = null,
        SelfSustainAbilityId = null,
        StanceSwapAbilityId = null,
        DispelAbilityId = null,
        SetupAbilityIds = Array.Empty<string>(),
        PayoffAbilityIds = new[] { "dump" },
        WindowIdBySetupAbility = new Dictionary<string, string>(),
        CostByAbility = new Dictionary<string, long> { ["builder"] = 0, ["dump"] = 0 },
        ResourceGainByAbility = new Dictionary<string, long> { ["builder"] = 30, ["dump"] = -50 },
        ResourceDumpAbilityIds = new[] { "dump" },
        ResourceBuilderAbilityIds = new[] { "builder" },
        LoopKind = Keyrun.Content.LoopKind.HeatOverheat,
        Resource = "heat",
        ResourceMax = 100,
        Heals = HealKit.None,
        TauntAbilityId = null,
    };

    [Fact(DisplayName = "Cinder heat-overheat resource-awareness > pickResourceAwareRotation: never lets a builder overflow the resource when a ready dump exists")]
    public void DumpBeforeOverflow()
    {
        var self = Make(resource: 90);
        Assert.Equal("dump", DamageRole.PickResourceAwareRotation(HeatKit(), self, 10));
    }

    [Fact(DisplayName = "Cinder heat-overheat resource-awareness > pickResourceAwareRotation: accepts the builder (and the coming Overheat) when no dump is ready")]
    public void BuilderWhenNoDump()
    {
        var self = Make(resource: 90, cooldowns: new[] { ("dump", 999) });
        Assert.Equal("builder", DamageRole.PickResourceAwareRotation(HeatKit(), self, 10));
    }

    [Fact(DisplayName = "Cinder heat-overheat resource-awareness > integration: a Cinder bot with Cold Snap ready never picks a heat-builder")]
    public void ColdSnapReady()
    {
        var cinder = Make(id: 5, resource: 90, targetId: 100, cooldowns: new[] { ("cinder.detonate", 999) });
        var enemy = Make(id: 100, kind: ActorKind.Enemy);
        Assert.Equal(new Command[] { new CastCommand(10, 5, "cinder.coldSnap", 100) }, DamageRole.DamageDecide(View(cinder, enemy), cinder));
    }

    [Fact(DisplayName = "Cinder heat-overheat resource-awareness > integration: with no dump or finisher ready, a Cinder bot still builds Heat (push-your-luck is intentional)")]
    public void StillBuildsHeat()
    {
        var cinder = Make(id: 5, resource: 90, targetId: 100,
            cooldowns: new[] { ("cinder.detonate", 999), ("cinder.coldSnap", 999), ("cinder.cinderfield", 999) });
        var enemy = Make(id: 100, kind: ActorKind.Enemy);
        Assert.Equal(new Command[] { new CastCommand(10, 5, "cinder.flare", 100) }, DamageRole.DamageDecide(View(cinder, enemy), cinder));
    }
}
