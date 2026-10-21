using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Bots press inside the spell queue window (PR #370, <see cref="SpellQueue"/>): mid-cast, a bot is not locked out once
/// its cast is in its last <see cref="SpellQueue.WindowTicks"/> ticks, so it presses an off-GCD ability (a kick, a
/// defensive) that the sim holds and fires on the tick the cast ends. The kick ledger counts such a kicker when that
/// landing tick falls inside the enemy's cast, and picks the kicker whose kick lands first, ties to the lowest id
/// (BIBLE s3: by intended tick, fixed tiebreak). TS twin: tests/bots/spell-queue.test.ts.
/// </summary>
public sealed class SpellQueueBotsTests
{
    private const int Now = 100;

    [Fact]
    public void A_bot_in_the_last_window_ticks_of_its_cast_is_ready_for_an_off_gcd_press()
    {
        var kit = RealContent.Kits.GetClassKit("cinder")!;
        var effective = EffectiveKit.FromClassKit(kit);
        var self = Casting(3, Now + SpellQueue.WindowTicks);

        Assert.False(Readiness.IsCastLocked(self, Now));
        Assert.True(Readiness.AbilityReady(self, "cinder.quench", Now, effective));
        self.CastEndsTick = Now + SpellQueue.WindowTicks + 1;
        Assert.True(Readiness.IsCastLocked(self, Now));
        Assert.False(Readiness.AbilityReady(self, "cinder.quench", Now, effective));
    }

    [Fact]
    public void The_ledger_assigns_a_kicker_whose_cast_ends_inside_the_window_and_before_the_enemy_cast()
    {
        var cinder = Casting(3, Now + 4);
        var cantor = Cantor(Now + 75);
        var view = BotTestKit.View(BotTestKit.MakeState(Now, cinder, cantor)) with { Kits = RealContent.Kits };

        var kick = RoleShared.LedgerKickCommand(view, cinder, RoleShared.KitOf(view, "cinder"));

        Assert.NotNull(kick);
        Assert.Equal("cinder.quench", kick!.AbilityId);
        Assert.Equal(cantor.Id, kick.TargetId);
    }

    [Fact]
    public void The_ledger_never_assigns_a_kick_that_would_land_once_the_enemy_cast_is_over()
    {
        var cinder = Casting(3, Now + 4);
        var cantor = Cantor(Now + 4); // the enemy cast completes before commands on the kicker's landing tick
        var view = BotTestKit.View(BotTestKit.MakeState(Now, cinder, cantor)) with { Kits = RealContent.Kits };

        Assert.Null(RoleShared.LedgerKickCommand(view, cinder, RoleShared.KitOf(view, "cinder")));
    }

    [Fact]
    public void The_ledger_picks_the_kick_that_lands_first_then_the_lowest_id()
    {
        var queued = Casting(3, Now + 4);
        var free = BotTestKit.MakeActor(id: 4, classId: "cinder");
        var cantor = Cantor(Now + 75);
        var actors = BotTestKit.MakeState(Now, queued, free, cantor).Actors;

        var first = Ledger.ResolveInterrupt(actors, "cinder.quench", new[] { 3, 4 }, Now, null, interruptOffGcd: true);
        Assert.Equal(4, first?.KickerId); // lands now, before id 3's queued kick

        queued.CastId = null;
        queued.CastEndsTick = null;
        var tie = Ledger.ResolveInterrupt(actors, "cinder.quench", new[] { 4, 3 }, Now, null, interruptOffGcd: true);
        Assert.Equal(3, tie?.KickerId);
    }

    private static ActorState Casting(int id, int endsTick)
    {
        var self = BotTestKit.MakeActor(id: id, classId: "cinder", castId: RealContent.Kits.GetClassKit("cinder")!.Rotation[0]);
        self.CastStartTick = Now - 10;
        self.CastEndsTick = endsTick;
        return self;
    }

    private static ActorState Cantor(int endsTick)
    {
        var cantor = BotTestKit.MakeActor(id: 9, kind: ActorKind.Enemy, classId: "choir.cantor", role: ActorRole.None, castId: "choir.healCast");
        cantor.CastStartTick = Now - 5;
        cantor.CastEndsTick = endsTick;
        return cantor;
    }
}
