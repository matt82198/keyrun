using Keyrun.Bots.Shared;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Port of tests/bots/interrupt.test.ts, case 1 of 3. Cases 2 and 3 ("only the lowest-actorId bot with kick
/// ready casts the interrupt", "the responsible bot falls back to rotation once its interrupt is on
/// cooldown") call roles/damage.ts damageDecide and belong to lane 3B.
/// </summary>
public sealed class InterruptTests
{
    [Fact(DisplayName = "interrupt assignment > isInterruptResponsible picks only the lowest eligible actorId")]
    public void LowestEligibleResponsible()
    {
        Assert.True(Priority.IsInterruptResponsible(5, new[] { 5, 7, 12 }));
        Assert.False(Priority.IsInterruptResponsible(7, new[] { 5, 7, 12 }));
        Assert.False(Priority.IsInterruptResponsible(1, System.Array.Empty<int>()));
    }
}
