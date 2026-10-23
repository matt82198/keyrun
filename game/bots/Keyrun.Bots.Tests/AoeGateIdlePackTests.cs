using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-TARGET-1 (a), WoW Mythic+ "do not AoE next to an unpulled pack": a bot never casts an ability whose area or target
/// would damage an idle (unengaged) enemy. Trace hollow-vault k10 s7: seat 3 damaged an idle cantor-vigil member 2.5 m away
/// and pulled seven enemies into the Choir fight. The DPS seat here is Cinder; Detonate (cinder.detonate, circle radius
/// 1500 mm, damage, cooldown 60) is the real kit's shaped AoE. It carries the "aoe" area-burst tag, so its shape is centred
/// on the seat's TARGET (game/sim/Keyrun.Sim/Combat/Targeting.cs:179), not on the caster.
/// </summary>
public sealed class AoeGateIdlePackTests
{
    private const int Me = 1;
    private const int Engaged1 = 10;
    private const int Engaged2 = 11;
    private const int IdlePack = 12;
    private const string Detonate = "cinder.detonate";

    /// <summary>
    /// The Cinder seat with every other rotation button on cooldown, so the only AoE candidate is Detonate.
    /// </summary>
    private static ActorState Cinder() =>
        Actor(id: Me, classId: "cinder", role: ActorRole.Damage, x: 0, z: 0, resource: 100, maxResource: 100,
            cooldowns: new[]
            {
                ("cinder.emberBolt", 999),
                ("cinder.flare", 999),
                ("cinder.coldSnap", 999),
            });

    private static ActorState Foe(int id, long x, long z, bool idle, int? targetId = null)
    {
        var foe = Actor(id: id, kind: ActorKind.Enemy, classId: "choir.melee", role: ActorRole.None, x: x, z: z,
            health: 300, maxHealth: 300, targetId: targetId);
        foe.Idle = idle;
        return foe;
    }

    /// <summary>
    /// Engaged1 (the target, x=1000 mm) and Engaged2 (x=800 mm, 200 mm from the target) inside Detonate's 1500 mm radius
    /// centred on the target, plus one idle pack member at (1000, <paramref name="idleZ"/>) mm, i.e. <paramref name="idleZ"/>
    /// mm from the target centre.
    /// </summary>
    private static BotView View(long idleZ)
    {
        var self = Cinder();
        self.TargetId = Engaged1;
        return ViewOf(10, RealKits, null,
            self,
            Foe(Engaged1, x: 1000, z: 0, idle: false, targetId: Me),
            Foe(Engaged2, x: 800, z: 0, idle: false, targetId: Me),
            Foe(IdlePack, x: 1000, z: idleZ, idle: true));
    }

    [Fact(DisplayName = "AoE idle-pack guard: two engaged enemies and an idle pack member inside Detonate's radius, the bot does not cast Detonate")]
    public void Does_not_cast_aoe_that_would_hit_an_idle_pack_inside_the_radius()
    {
        var view = View(idleZ: 1200);
        var cmds = DamageRole.DamageDecide(view, view.State.Actors[Me]);
        var casts = CastIds(cmds);
        Assert.False(casts.Contains(Detonate),
            $"Detonate (radius 1500 mm, centred on the target) was cast with an idle pack member 1200 mm from the target centre inside its shape; casts were [{string.Join(", ", casts)}]");
    }

    [Fact(DisplayName = "AoE idle-pack guard: the same two engaged enemies with the idle pack member outside the radius, the bot casts Detonate")]
    public void Casts_aoe_when_the_idle_pack_is_outside_the_radius()
    {
        var view = View(idleZ: 2500);
        var cmds = DamageRole.DamageDecide(view, view.State.Actors[Me]);
        var casts = CastIds(cmds);
        Assert.True(casts.Contains(Detonate),
            $"Detonate was not cast with two engaged enemies in its shape and the idle pack 2500 mm from the target centre, outside it; casts were [{string.Join(", ", casts)}]");
    }
}
