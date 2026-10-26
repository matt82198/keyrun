using System;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-KICKLOCK-1 (seq 461; evidence: seed 6, hollow-vault). While a guided bot holds the assigned kick for an enemy cast, a
/// bot's own cast must not run past that kick's deadline. Seed 6: the kicker seat 3 started cinder.flare (30 ticks) at tick
/// 1480 while the Choir heal cast 1448..1528 was owed. The flare locked the seat, so the kick landed at 1510, 62 ticks into
/// the heal, past the 60-tick kick window. Constructed here rather than run from the seed: a Choir heal cast owed to a
/// Cinder kicker on the real layout, with the kicker's rotation read from the real kit.
/// </summary>
public sealed class KickHoldsOwnCastTests
{
    /// <summary>The Choir heal cast's id (content choir.healCast).</summary>
    private const string HealCast = "choir.healCast";

    /// <summary>The Choir's seat (an enemy actor id).</summary>
    private const int ChoirId = 100;

    /// <summary>The Cinder kicker's seat (seat 3 in the seed 6 evidence).</summary>
    private const int CinderId = 3;

    /// <summary>The Choir heal cast starts at tick 1448 (seed 6 evidence).</summary>
    private const int CastStart = 1448;

    /// <summary>The Choir heal cast resolves at tick 1528: an 80-tick (4 s) cast.</summary>
    private const int CastEnds = 1528;

    /// <summary>The kick window: the latest kick offset into the cast is 60 ticks (the 80-tick cast's last safe stretch).</summary>
    private const int KickWindowTicks = 60;

    /// <summary>The tick past which an own cast may not complete: the Choir cast start plus the kick window (1508).</summary>
    private const int KickDeadline = CastStart + KickWindowTicks;

    /// <summary>The tick the cinder.flare decision was made in the seed 6 evidence, while the kick was still held.</summary>
    private const int HoldTick = 1480;

    /// <summary>The tick the back half of the heal opens for the kick (CastStart + 40): the kick is owed from here.</summary>
    private const int KickOpenTick = 1488;

    private static readonly Lazy<NavLayout> Nav = new(() => NavLayouts.ToSim(ContentLoader.Load().Layout));

    private static BotView ViewAt(int tick, params ActorState[] actors) =>
        ViewOf(tick, RealKits, null, actors) with { Layout = Nav.Value };

    private static ActorState Choir() => Actor(
        id: ChoirId, kind: ActorKind.Enemy, classId: "choir", role: ActorRole.Healer, x: 5000,
        health: 1000, maxHealth: 1000, castId: HealCast, castStartTick: CastStart, castEndsTick: CastEnds);

    /// <summary>
    /// The Cinder kicker: beside the Choir, full resource, not casting. The rotation's earlier abilities (detonate,
    /// coldSnap, cinderfield) are on cooldown, so the rotation's first ready, affordable ability is cinder.flare, the cast
    /// the seed 6 evidence started while the kick was owed.
    /// </summary>
    private static ActorState Cinder() => Actor(
        id: CinderId, classId: "cinder", role: ActorRole.Damage, x: 5000, resource: 100, maxResource: 100,
        cooldowns: new[] { ("cinder.detonate", 9999), ("cinder.coldSnap", 9999), ("cinder.cinderfield", 9999) });

    private static int CastTicksOf(BotView view, string abilityId)
    {
        Assert.NotNull(view.Kits.Content);
        Assert.True(view.Kits.Content!.Abilities.TryGetValue(abilityId, out var def), $"no content for {abilityId}");
        return def.CastTicks;
    }

    [Fact(DisplayName = "Kick holds own cast: a Cinder kicker holding the owed Choir heal kick does not start a cast that completes after the kick deadline")]
    public void Own_cast_started_while_the_kick_is_owed_completes_before_the_kick_deadline()
    {
        // Precondition 1: at 1480 the kick is still held (it opens at 1488), so the decision below is made while owed.
        var heldView = ViewAt(HoldTick, Choir(), Cinder());
        var heldKicker = heldView.State.Actors[CinderId];
        Assert.Null(RoleShared.LedgerKickCommand(heldView, heldKicker, RoleShared.KitOf(heldView, "cinder")));

        // Precondition 2: at 1488 the kick is open and the Cinder seat is the ledger's named kicker of the Choir heal.
        var openView = ViewAt(KickOpenTick, Choir(), Cinder());
        var kick = RoleShared.LedgerKickCommand(openView, openView.State.Actors[CinderId], RoleShared.KitOf(openView, "cinder"));
        Assert.NotNull(kick);
        Assert.Equal(ChoirId, kick!.TargetId);

        // The rule: every cast the kicker starts at 1480 must complete by the kick deadline (1508).
        var decision = DamageRole.DamageDecide(heldView, heldKicker);
        // Precondition 3: the decision issued at least one cast, so the loop below cannot pass with nothing to check.
        Assert.NotEmpty(decision.OfType<CastCommand>());
        foreach (var cast in decision.OfType<CastCommand>())
        {
            var ends = HoldTick + CastTicksOf(heldView, cast.AbilityId);
            Assert.True(
                ends <= KickDeadline,
                $"cinder started {cast.AbilityId} at tick {HoldTick} while the Choir heal {CastStart}..{CastEnds} was owed; "
                + $"it completes at {ends}, past the kick deadline {KickDeadline}");
        }
    }
}
