using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>Port of tests/bots/class-specific-roles.test.ts (6 cases, real content kits).</summary>
public sealed class RolesClassSpecificTests
{
    private static ActorState Chanter(int id = 1, long resource = 0, ActorRole role = ActorRole.Healer, long health = 100) =>
        Actor(id: id, classId: "chanter", role: role, resource: resource, maxResource: 1, health: health);

    private static BotView View(params ActorState[] actors) => ViewOf(10, RealKits, null, actors);

    [Fact(DisplayName = "Chanter healer: verse swap on party missing-health threshold > swaps to Mend (Refrain) when not already in Mend and the party is badly hurt")]
    public void SwapsToMend()
    {
        var chanter = Chanter(id: 1, resource: 0);
        var hurt = Chanter(id: 2, role: ActorRole.Damage, health: 10);
        Assert.Equal(new Command[] { new CastCommand(10, 1, "chanter.refrain") }, HealerRole.HealerDecide(View(chanter, hurt), chanter));
    }

    [Fact(DisplayName = "Chanter healer: verse swap on party missing-health threshold > does not verse-swap when already in Mend (heals through the shape-aware heal-triage path instead)")]
    public void NoSwapInMend()
    {
        var chanter = Chanter(id: 1, resource: 1);
        var hurt = Chanter(id: 2, role: ActorRole.Damage, health: 10);
        Assert.Equal(new Command[] { new CastCommand(10, 1, "chanter.chorus") }, HealerRole.HealerDecide(View(chanter, hurt), chanter));
    }

    [Fact(DisplayName = "Chanter healer: verse swap on party missing-health threshold > does not swap when the party is healthy")]
    public void NoSwapHealthy()
    {
        var chanter = Chanter(id: 1, resource: 0);
        var healthy = Chanter(id: 2, role: ActorRole.Damage, health: 95);
        Assert.DoesNotContain("chanter.refrain", CastIds(HealerRole.HealerDecide(View(chanter, healthy), chanter)));
    }

    private static ActorState Warden(long health, long resource) =>
        Actor(id: 1, classId: "warden", role: ActorRole.Tank, health: health, resource: resource, maxResource: 1);

    [Fact(DisplayName = "Warden tank: Vigil under a higher floor than Brace > pops Vigil (self-sustain) between the Vigil and Brace floors, when affordable")]
    public void VigilBetweenFloors()
    {
        var warden = Warden(55, 60);
        var ids = CastIds(TankRole.TankDecide(View(warden), warden));
        Assert.Contains("warden.vigil", ids);
        Assert.DoesNotContain("warden.brace", ids);
    }

    [Fact(DisplayName = "Warden tank: Vigil under a higher floor than Brace > pops Brace instead, under Brace's lower floor")]
    public void BraceUnderFloor()
    {
        var warden = Warden(30, 60);
        var ids = CastIds(TankRole.TankDecide(View(warden), warden));
        Assert.Contains("warden.brace", ids);
        Assert.DoesNotContain("warden.vigil", ids);
    }

    [Fact(DisplayName = "Warden tank: Vigil under a higher floor than Brace > does not pop Vigil without enough Momentum to afford it")]
    public void VigilUnaffordable()
    {
        var warden = Warden(55, 10);
        Assert.Empty(CastIds(TankRole.TankDecide(View(warden), warden)));
    }
}
