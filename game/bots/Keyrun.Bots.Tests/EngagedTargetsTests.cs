using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// PRESPAWN-AGGRO x the scripted Warden (game/src/Host/PlayerInput.cs CastTargeting and ScriptedWarden): an idle
/// (not yet engaged) prespawned enemy is never a hostile target. Targeting it would aim casts at a pack that has not
/// pulled, and fail them out of range, or pull it by targeting. The idle rule matches the holder lock's (111995a,
/// Holder.LockHolders: <c>Kind == Enemy &amp;&amp; Alive &amp;&amp; !Idle</c>).
/// </summary>
public sealed class EngagedTargetsTests
{
    private const int Me = 1;

    private static ActorState Enemy(int id, long x, bool idle)
    {
        var a = Actors.Create(id, ActorKind.Enemy, "test.brute", ActorRole.None, new Vec3(x, 0, 0), 100, 0);
        a.Idle = idle;
        return a;
    }

    private static (SimState State, ActorState Self) World(params ActorState[] enemies)
    {
        var state = SimState.Create(1);
        var self = Actors.Create(Me, ActorKind.Player, "warden", ActorRole.Tank, new Vec3(0, 0, 0), 400, 100);
        state.Actors[Me] = self;
        foreach (var e in enemies)
        {
            state.Actors[e.Id] = e;
        }
        return (state, self);
    }

    [Fact(DisplayName = "Engaged targets: an engaged pack at 6000 mm is picked over an idle pack at 3000 mm")]
    public void Picks_the_engaged_pack_over_a_nearer_idle_one()
    {
        var (state, self) = World(Enemy(101, 3000, idle: true), Enemy(201, 6000, idle: false));
        Assert.Equal(201, EngagedTargets.Nearest(state, self)?.Id);
        Assert.Equal(201, EngagedTargets.HostileTarget(state, self));
    }

    [Fact(DisplayName = "Engaged targets: with only idle packs nothing is picked (no pull by targeting)")]
    public void Picks_nothing_when_every_pack_is_idle()
    {
        var (state, self) = World(Enemy(101, 3000, idle: true), Enemy(201, 6000, idle: true));
        Assert.Null(EngagedTargets.Nearest(state, self));
        Assert.Null(EngagedTargets.HostileTarget(state, self));
    }

    [Fact(DisplayName = "Engaged targets: a current target is kept only while it is a living engaged enemy")]
    public void Keeps_the_current_target_only_while_it_is_engaged()
    {
        var (state, self) = World(Enemy(101, 3000, idle: true), Enemy(201, 6000, idle: false), Enemy(202, 9000, idle: false));
        self.TargetId = 202;
        Assert.Equal(202, EngagedTargets.HostileTarget(state, self));

        self.TargetId = 101;
        Assert.Equal(201, EngagedTargets.HostileTarget(state, self));

        state.Actors[202].Alive = false;
        self.TargetId = 202;
        Assert.Equal(201, EngagedTargets.HostileTarget(state, self));
    }

    [Fact(DisplayName = "Engaged targets: ties on distance go to the lowest id; players are never hostile to a player")]
    public void Ties_go_to_the_lowest_id()
    {
        var (state, self) = World(Enemy(202, 4000, idle: false), Enemy(201, -4000, idle: false));
        var ally = Actors.Create(2, ActorKind.Player, "cinder", ActorRole.Damage, new Vec3(100, 0, 0), 250, 100);
        state.Actors[2] = ally;
        Assert.Equal(201, EngagedTargets.Nearest(state, self)?.Id);
    }
}
