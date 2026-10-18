using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.BotTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>Port of tests/bots/windows.test.ts (10 cases).</summary>
public sealed class WindowsTests
{
    private const string W = Pings.WindowEffectPrefix;

    private static ActorState Player(int id, string classId) => MakeActor(id: id, classId: classId);

    private static ActorState Enemy(int id, bool alive = true, params EffectInstance[] effects) =>
        MakeActor(id: id, kind: ActorKind.Enemy, classId: "warden", alive: alive, effects: effects);

    [Fact(DisplayName = "canClose (The Lock) > any class can close a Window with an unknown source")]
    public void UnknownSourceClosable() => Assert.True(BotWindows.CanClose(null, "cinder"));

    [Fact(DisplayName = "canClose (The Lock) > a class cannot close its own Window")]
    public void OwnWindowNotClosable() => Assert.False(BotWindows.CanClose("warden", "warden"));

    [Fact(DisplayName = "canClose (The Lock) > a different class can close it")]
    public void OtherClassClosable() => Assert.True(BotWindows.CanClose("warden", "cinder"));

    [Fact(DisplayName = "openWindowsOn > finds a live Window effect and derives setupClass from the caster's (sourceId's) classId")]
    public void FindsLiveWindow()
    {
        var wins = BotWindows.OpenWindowsOn(Table(Player(2, "warden"), Enemy(100, true, Effect(W + "sundered", 2, 200))), 10);
        Assert.Equal(new[] { new OpenWindow(100, "sundered", "warden", 200) }, wins);
    }

    [Fact(DisplayName = "openWindowsOn > ignores an expired Window (endsTick at or before the current tick)")]
    public void IgnoresExpired()
    {
        Assert.Empty(BotWindows.OpenWindowsOn(Table(Player(2, "warden"), Enemy(100, true, Effect(W + "sundered", 2, 50))), 50));
    }

    [Fact(DisplayName = "openWindowsOn > ignores non-Window effects and dead enemies")]
    public void IgnoresNonWindowAndDead()
    {
        var alive = Enemy(100, true, Effect("burning", 2, 200));
        var dead = Enemy(101, false, Effect(W + "kindled", 2, 200));
        Assert.Empty(BotWindows.OpenWindowsOn(Table(Player(2, "warden"), alive, dead), 10));
    }

    [Fact(DisplayName = "openWindowsOn > setupClass is null when the source actor is missing (e.g. despawned)")]
    public void NullSetupClassWhenSourceMissing()
    {
        var wins = BotWindows.OpenWindowsOn(Table(Enemy(100, true, Effect(W + "sundered", 999, 200))), 10);
        Assert.Null(wins[0].SetupClass);
    }

    [Fact(DisplayName = "findClosableWindow > excludes a Window this payoffClass opened, even if it's the only one, and picks the closable one otherwise")]
    public void ExcludesOwnPicksOther()
    {
        var actors = Table(
            Player(2, "warden"),
            Player(3, "cinder"),
            Enemy(100, true, Effect(W + "kindled", 3, 200)),
            Enemy(101, true, Effect(W + "sundered", 2, 300)));
        var forCinder = BotWindows.FindClosableWindow(actors, 10, "cinder");
        Assert.Equal(101, forCinder?.TargetId);
        Assert.Equal("warden", forCinder?.SetupClass);
    }

    [Fact(DisplayName = "findClosableWindow > returns null when every open Window belongs to the querying class")]
    public void NullWhenAllOwn()
    {
        Assert.Null(BotWindows.FindClosableWindow(Table(Player(3, "cinder"), Enemy(100, true, Effect(W + "kindled", 3, 200))), 10, "cinder"));
    }

    [Fact(DisplayName = "findClosableWindow > prefers the earliest-expiring closable Window")]
    public void PrefersEarliestExpiring()
    {
        var actors = Table(Player(2, "warden"), Enemy(100, true, Effect(W + "sundered", 2, 120)), Enemy(101, true, Effect(W + "sundered", 2, 300)));
        Assert.Equal(100, BotWindows.FindClosableWindow(actors, 10, "cinder")?.TargetId);
    }
}
