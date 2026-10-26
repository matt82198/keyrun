using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-TARGET-1 (c), healer side (f-db5db445): <see cref="HealerIdle"/> centres a shaped AoE payoff (the Chanter's Dissonance,
/// a radius circle tagged "aoe" that bursts at its target) with <c>AoeGate.ClusterTarget</c>, which counts engaged enemies
/// only and never applies the idle-pack veto that <c>AoeGate.Filter</c> applies for the damage role. So a healer AoE can be
/// centred on an engaged enemy whose shape holds an idle pack member. WoW Mythic+: no seat AoEs into an unpulled pack.
/// The radius is read from content (content/data/abilities/chanter.json, chanter.dissonance shape.radius), not guessed.
/// </summary>
public sealed class HealerAoeIdlePackTests
{
    private const int Tank = 1;
    private const int Chanter = 2;
    private const int EnemyA = 10;
    private const int EnemyB = 11;
    private const int IdlePack = 12;
    private const int EnemyC = 13;
    private const int Tick = 10;
    private const string Dissonance = "chanter.dissonance";

    /// <summary>Radius of Dissonance's shape in mm, read from the real content registry.</summary>
    private static long DissonanceRadius() => ContentLoader.Load().Abilities[Dissonance].Shape.Radius ?? 0;

    private static ActorState Foe(int id, long x, long z, bool idle = false)
    {
        var foe = Actor(id: id, kind: ActorKind.Enemy, classId: "choir.melee", role: ActorRole.None, x: x, z: z, health: 300, maxHealth: 300,
            targetId: idle ? null : Tank, alive: true);
        foe.Idle = idle;
        return foe;
    }

    private static ActorState Ally() =>
        Actor(id: Tank, classId: "warden", role: ActorRole.Tank, x: -9000, z: 0, health: 400, maxHealth: 400);

    private static ActorState Healer(int? targetId) =>
        Actor(id: Chanter, classId: "chanter", role: ActorRole.Healer, health: 250, maxHealth: 250, resource: 1, maxResource: 1,
            targetId: targetId);

    private static int? DissonanceTarget(IEnumerable<Command> cmds) =>
        cmds.OfType<CastCommand>().Where(c => c.AbilityId == Dissonance).Select(c => c.TargetId).FirstOrDefault();

    private static double Distance(ActorState a, ActorState b)
    {
        var dx = (double)(a.Pos.X - b.Pos.X);
        var dz = (double)(a.Pos.Z - b.Pos.Z);
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    [Fact(DisplayName = "Healer AoE idle-pack veto (f-db5db445): with two engaged enemies, Dissonance is never centred on the one whose shape holds an idle pack member")]
    public void Dissonance_is_not_centred_on_an_enemy_whose_shape_holds_an_idle_pack_member()
    {
        var radius = DissonanceRadius();
        // EnemyA is the healer's current target. The idle member sits 2000 mm from EnemyA (inside the radius), so EnemyA's
        // shape clips it. EnemyB sits 4000 mm west of EnemyA, so its shape holds only itself (the idle member is 6000 mm away).
        // Both engaged enemies hold one engaged enemy in shape, so the current target wins the tie and gets the press.
        var enemyA = Foe(EnemyA, x: 0, z: 0);
        var enemyB = Foe(EnemyB, x: -4000, z: 0);
        var idle = Foe(IdlePack, x: 2000, z: 0, idle: true);
        var healer = Healer(targetId: EnemyA);
        var view = ViewOf(Tick, RealKits, null, Ally(), healer, enemyA, enemyB, idle);

        Assert.True(2000 < radius, $"premise: the idle member must be inside Dissonance's {radius} mm shape from EnemyA");
        var cmds = HealerRole.HealerDecide(view, healer);
        var chosen = DissonanceTarget(cmds);
        var casts = string.Join(", ", CastIds(cmds));
        var chosenIsClipping = chosen is { } c && Distance(view.State.Actors[IdlePack], view.State.Actors[c]) <= radius;
        var distance = chosen is { } t && view.State.Actors.TryGetValue(t, out var centre)
            ? Distance(view.State.Actors[IdlePack], centre).ToString("F0")
            : "n/a";
        Assert.True(chosen == EnemyB && !chosenIsClipping,
            $"Dissonance centred on engaged enemy {chosen?.ToString() ?? "none"}, whose {radius} mm shape holds idle pack member {IdlePack} " +
            $"at {distance} mm from the shape centre; the clear engaged enemy {EnemyB} was available. Casts were [{casts}].");
    }

    [Fact(DisplayName = "Healer AoE idle-pack guard: with no idle pack in reach, the bigger cluster wins the Dissonance centre")]
    public void Dissonance_centres_on_the_bigger_cluster_when_no_idle_pack_is_in_reach()
    {
        // EnemyA (the target) stands alone. EnemyB and EnemyC stand 500 mm apart, 4000 mm east of EnemyA: B's shape holds
        // two engaged enemies, A's holds one. No idle enemy exists, so the cluster pick is unchanged.
        var enemyA = Foe(EnemyA, x: 0, z: 0);
        var enemyB = Foe(EnemyB, x: 4000, z: 0);
        var enemyC = Foe(EnemyC, x: 4500, z: 0);
        var healer = Healer(targetId: EnemyA);
        var view = ViewOf(Tick, RealKits, null, Ally(), healer, enemyA, enemyB, enemyC);

        var cmds = HealerRole.HealerDecide(view, healer);
        Assert.Equal(
            new Command[] { new TargetCommand(Tick, Chanter, EnemyB), new CastCommand(Tick, Chanter, Dissonance, EnemyB) },
            cmds);
    }

    [Fact(DisplayName = "Healer AoE idle-pack guard: when every engaged candidate clips an idle pack member, the healer withholds Dissonance rather than pulling")]
    public void Dissonance_is_withheld_when_every_engaged_candidate_clips_an_idle_pack_member()
    {
        // EnemyA (the target) at 0 and EnemyB at -4000 mm; the idle member sits at -2000 mm, 2000 mm from each of them, so
        // both candidate shapes (radius 2500) hold it. Every Dissonance centre would pull the idle pack.
        var enemyA = Foe(EnemyA, x: 0, z: 0);
        var enemyB = Foe(EnemyB, x: -4000, z: 0);
        var idle = Foe(IdlePack, x: -2000, z: 0, idle: true);
        var healer = Healer(targetId: EnemyA);
        var view = ViewOf(Tick, RealKits, null, Ally(), healer, enemyA, enemyB, idle);

        var cmds = HealerRole.HealerDecide(view, healer);
        var casts = CastIds(cmds);
        Assert.False(casts.Contains(Dissonance),
            $"Dissonance was cast although every engaged candidate ({EnemyA}, {EnemyB}) clips idle pack member {IdlePack}; " +
            $"the healer cast [{string.Join(", ", casts)}] instead.");
    }
}
