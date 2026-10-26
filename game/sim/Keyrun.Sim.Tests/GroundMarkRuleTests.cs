using Keyrun.Host;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Which kinds of ground marks the 3D view draws (Matt 2026-10-26: "too much ground clutter, players will learn their
/// aggro range and stuff, they dont need circles around them"). Per-actor Window rings at the feet are dropped: the
/// buff state already shows on the party frames. Hazards to step out of, cast telegraphs, the Pair marks and the boss
/// Lock rings stay. RED until <see cref="GroundMarkRule"/> and <see cref="GroundMarkKind"/> exist in Keyrun.Host.
/// </summary>
public sealed class GroundMarkRuleTests
{
    [Theory]
    [InlineData(GroundMarkKind.ActorWindow)]
    [InlineData(GroundMarkKind.FloorWindow)]
    public void PersistentWindowRingsAreNotDrawn(GroundMarkKind kind)
    {
        Assert.False(GroundMarkRule.Draws(kind));
    }

    [Theory]
    [InlineData(GroundMarkKind.Telegraph)]
    [InlineData(GroundMarkKind.SpreadMark)]
    [InlineData(GroundMarkKind.StackMark)]
    [InlineData(GroundMarkKind.SteamDisc)]
    [InlineData(GroundMarkKind.LockRing)]
    [InlineData(GroundMarkKind.EnrageRing)]
    public void HazardsCastsAndMechanicMarksAreStillDrawn(GroundMarkKind kind)
    {
        Assert.True(GroundMarkRule.Draws(kind));
    }
}
