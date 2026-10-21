using System;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// CARELESS-SEAT-ARGS (PR #308 run C): a user argument the arena does not read must stop the launch, never vanish.
/// Run C launched with <c>--careless-seat 3@300:pair</c> on a Keyrun.dll built before the flag existed; that build's
/// ArenaOptions.Parse ignored the unknown flag, so the run played a normal party and logged no BOTS careless line and no
/// error. ArenaOptions.Parse hands every argument it does not know to <see cref="ForeignUserArgs.Skip"/>: the flags other
/// readers own (Main's <c>--smoke</c> and <c>--view-size</c>, Screen's <c>--progression-dir</c>) are stepped over,
/// anything else throws and SimDriver prints <c>ARENA error</c> and exits 1.
/// </summary>
public sealed class ForeignUserArgsTests
{
    private static int SkipAt(string[] args, int at)
    {
        var i = at;
        ForeignUserArgs.Skip(args, ref i);
        return i;
    }

    [Fact]
    public void A_flag_the_arena_build_does_not_know_stops_the_launch()
    {
        // The stale-build case: run C's flag reaching a Parse whose switch has no --careless-seat case.
        var args = new[] { "--autorun", "--ticks", "7000", "--careless-seat", "3@300:pair" };
        var ex = Assert.Throws<ArgumentException>(() => SkipAt(args, 3));
        Assert.Contains("'--careless-seat'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_misspelled_flag_stops_the_launch()
    {
        var ex = Assert.Throws<ArgumentException>(() => SkipAt(new[] { "--carless-seat", "3@300:pair" }, 0));
        Assert.Contains("'--carless-seat'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stray_value_stops_the_launch()
    {
        // "--careless-seat 3@300 pair" split by a shell: the arena reads 3@300 (and fails it) or meets "pair" here.
        var ex = Assert.Throws<ArgumentException>(() => SkipAt(new[] { "--autorun", "pair" }, 1));
        Assert.Contains("'pair'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_and_Screen_flags_are_stepped_over_with_their_values()
    {
        Assert.Equal(0, SkipAt(new[] { "--smoke" }, 0));
        Assert.Equal(2, SkipAt(new[] { "--autorun", "--view-size", "1280x720", "--solo" }, 1));
        Assert.Equal(1, SkipAt(new[] { "--progression-dir", "C:/Temp/keyrun-save" }, 0));
    }

    [Fact]
    public void A_value_flag_never_swallows_the_next_flag()
    {
        // ProgressionSavePlace reads "--progression-dir --solo" as no directory; --solo must still reach the arena.
        Assert.Equal(0, SkipAt(new[] { "--progression-dir", "--solo" }, 0));
        Assert.Equal(0, SkipAt(new[] { "--view-size" }, 0));
    }
}
