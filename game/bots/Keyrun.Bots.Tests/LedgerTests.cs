using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.BotTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>Port of tests/bots/ledger.test.ts (10 cases).</summary>
public sealed class LedgerTests
{
    private static ActorState Actor(int id, ActorKind kind = ActorKind.Player, string? castId = null, int? kickReady = null) =>
        MakeActor(id: id, kind: kind, classId: "warden", castId: castId, cooldowns: kickReady is null ? null : new[] { ("kick", kickReady.Value) });

    [Fact(DisplayName = "selectPriorityCastTarget > with no priority list, ties on lowest actorId")]
    public void NoPriorityListTiesLowest()
    {
        var casting = new[] { new CastingTarget(101, "a"), new CastingTarget(100, "b") };
        Assert.Equal(100, Ledger.SelectPriorityCastTarget(casting));
    }

    [Fact(DisplayName = "selectPriorityCastTarget > a listed ability outranks an unlisted one regardless of actorId")]
    public void ListedOutranksUnlisted()
    {
        var casting = new[] { new CastingTarget(100, "trivial-swing"), new CastingTarget(200, "must-kick-heal") };
        Assert.Equal(200, Ledger.SelectPriorityCastTarget(casting, new[] { "must-kick-heal" }));
    }

    [Fact(DisplayName = "selectPriorityCastTarget > null when nothing is casting")]
    public void NullWhenNothingCasting()
    {
        Assert.Null(Ledger.SelectPriorityCastTarget(System.Array.Empty<CastingTarget>()));
    }

    [Fact(DisplayName = "selectKicker > the lowest of the eligible actorIds")]
    public void LowestEligible()
    {
        Assert.Equal(2, Ledger.SelectKicker(new[] { 7, 2, 5 }));
    }

    [Fact(DisplayName = "selectKicker > null when no one is eligible")]
    public void NullWhenNoneEligible()
    {
        Assert.Null(Ledger.SelectKicker(System.Array.Empty<int>()));
    }

    [Fact(DisplayName = "resolveInterrupt > with two ready interrupts, the lowest actorId is assigned the kick")]
    public void TwoReadyLowestAssigned()
    {
        var enemy = Actor(100, ActorKind.Enemy, castId: "big-cast");
        var assignment = Ledger.ResolveInterrupt(Table(enemy, Actor(3), Actor(7)), "kick", new[] { 3, 7 }, 10);
        Assert.Equal(new InterruptAssignment(100, 3), assignment);
    }

    [Fact(DisplayName = "resolveInterrupt > skips a not-ready candidate: the other eligible actor is assigned instead")]
    public void SkipsNotReady()
    {
        var enemy = Actor(100, ActorKind.Enemy, castId: "big-cast");
        var assignment = Ledger.ResolveInterrupt(Table(enemy, Actor(3, kickReady: 999), Actor(7)), "kick", new[] { 3, 7 }, 10);
        Assert.Equal(new InterruptAssignment(100, 7), assignment);
    }

    [Fact(DisplayName = "resolveInterrupt > null when nobody eligible is ready")]
    public void NullWhenNobodyReady()
    {
        var enemy = Actor(100, ActorKind.Enemy, castId: "big-cast");
        Assert.Null(Ledger.ResolveInterrupt(Table(enemy, Actor(3, kickReady: 999)), "kick", new[] { 3 }, 10));
    }

    [Fact(DisplayName = "resolveInterrupt > null when nothing is casting even if kickers are ready")]
    public void NullWhenNothingCastingEvenIfReady()
    {
        Assert.Null(Ledger.ResolveInterrupt(Table(Actor(3)), "kick", new[] { 3 }, 10));
    }

    [Fact(DisplayName = "resolveInterrupt > honours a priority-cast list: the must-kick cast is targeted even from a higher-actorId enemy")]
    public void HonoursPriorityList()
    {
        var trivial = Actor(100, ActorKind.Enemy, castId: "trivial-swing");
        var mustKick = Actor(200, ActorKind.Enemy, castId: "choir.healCast");
        var assignment = Ledger.ResolveInterrupt(Table(trivial, mustKick, Actor(3)), "kick", new[] { 3 }, 10, new[] { "choir.healCast" });
        Assert.Equal(new InterruptAssignment(200, 3), assignment);
    }
}
