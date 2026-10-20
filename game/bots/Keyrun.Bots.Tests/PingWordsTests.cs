using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.BotTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// PING-FEEDBACK (design/BIBLE.md s10 "the game turns it into the party's language ("kick", "stack here",
/// "Window!")"): the word a human G-ping shows, read from the same context PingsPolicy's bots react to.
/// </summary>
public sealed class PingWordsTests
{
    private const int Now = 100;
    private const int Me = 1;
    private const int Foe = 100;

    private static PingEvent TargetPing(int targetId) => new(Now, Me, PingKind.Target) { TargetId = targetId };

    private static PingEvent SpotPing() => new(Now, Me, PingKind.Spot) { Point = new PointMm(0, 0) };

    private static ActorState Warden(params EffectInstance[] effects) =>
        MakeActor(id: Me, classId: "warden", role: ActorRole.Tank, effects: effects);

    private static ActorState Enemy(string? castId = null, bool alive = true, params EffectInstance[] effects) =>
        MakeActor(id: Foe, kind: ActorKind.Enemy, classId: "warden", alive: alive, castId: castId, effects: effects);

    [Fact(DisplayName = "a target ping on a casting enemy says kick (the bots' interrupt reaction)")]
    public void CastingEnemyIsKick()
    {
        var actors = Table(Warden(), Enemy(castId: "boltCast"));
        Assert.Equal("kick", PingWords.Say(actors, Now, TargetPing(Foe)));
    }

    [Fact(DisplayName = "a target ping on an enemy carrying a live Window says Window! (the bots' payoff reaction)")]
    public void WindowCarrierIsWindow()
    {
        var actors = Table(Warden(), Enemy(effects: Effect(Pings.WindowEffectPrefix + "sundered", Me, Now + 50)));
        Assert.Equal("Window!", PingWords.Say(actors, Now, TargetPing(Foe)));
    }

    [Fact(DisplayName = "kick outranks Window! on a casting Window carrier (PingsPolicy: interrupt before payoff)")]
    public void KickOutranksWindow()
    {
        var actors = Table(Warden(), Enemy(castId: "boltCast", effects: Effect(Pings.WindowEffectPrefix + "sundered", Me, Now + 50)));
        Assert.Equal("kick", PingWords.Say(actors, Now, TargetPing(Foe)));
    }

    [Fact(DisplayName = "an expired Window is no Window: the ping is a plain focus call")]
    public void ExpiredWindowIsFocus()
    {
        var actors = Table(Warden(), Enemy(effects: Effect(Pings.WindowEffectPrefix + "sundered", Me, Now)));
        Assert.Equal("focus", PingWords.Say(actors, Now, TargetPing(Foe)));
    }

    [Fact(DisplayName = "a target ping on a stack-marked player says stack here")]
    public void StackMarkedTargetIsStackHere()
    {
        var actors = Table(Warden(), MakeActor(id: 2, classId: "chanter", effects: new[] { Effect(PingWords.StackEffectId, Foe, Now + 40) }), Enemy());
        Assert.Equal("stack here", PingWords.Say(actors, Now, TargetPing(2)));
    }

    [Fact(DisplayName = "a spot ping while a stack mark is live says stack here")]
    public void SpotDuringStackMarkIsStackHere()
    {
        var actors = Table(Warden(Effect(PingWords.StackEffectId, Foe, Now + 40)), Enemy());
        Assert.Equal("stack here", PingWords.Say(actors, Now, SpotPing()));
    }

    [Fact(DisplayName = "a spot ping while a STACK telegraph is pending (before the mark lands) says stack here")]
    public void SpotDuringStackTelegraphIsStackHere()
    {
        var actors = Table(Warden(), Enemy());
        Assert.Equal("stack here", PingWords.Say(actors, Now, SpotPing(), stackPending: true));
    }

    [Fact(DisplayName = "a spot ping with no call live is a plain here marker")]
    public void PlainSpotIsHere()
    {
        var actors = Table(Warden(Effect(PingWords.StackEffectId, Foe, Now)), Enemy());
        Assert.Equal("here", PingWords.Say(actors, Now, SpotPing()));
    }

    [Fact(DisplayName = "a target ping on an idle enemy is a plain focus marker")]
    public void IdleEnemyIsFocus()
    {
        Assert.Equal("focus", PingWords.Say(Table(Warden(), Enemy()), Now, TargetPing(Foe)));
    }

    [Fact(DisplayName = "a dead caster is no kick")]
    public void DeadCasterIsNotKick()
    {
        Assert.Equal("focus", PingWords.Say(Table(Warden(), Enemy(castId: "boltCast", alive: false)), Now, TargetPing(Foe)));
    }

    [Fact(DisplayName = "the word shows for 2 s (PingsPolicy.PingMoveTicks, the bots' own spot-ping reaction)")]
    public void ShowsTwoSeconds() => Assert.Equal(2 * Tick.TickHz, PingWords.ShowTicks);
}
