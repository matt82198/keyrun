using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// f-70d36828 (KF2 demo verdict; design/BIBLE.md section 9 "The Choir teaches the kick ledger", section 3 interrupts by
/// intended tick): bots once kicked the Choir Cantor's heal 2 ticks after it started, so the first pull never showed the
/// cast bar the Choir teaches. A human M+ player reacts in 0.3-0.6 s and kicks a 4 s heal about a third of the way in.
/// Pinned here as a property over seeds 1-20 of run C's Choir pull (<see cref="KickTimingTests.DriveChoirPull"/>):
/// every heal cast owed a kick is kicked no earlier than a reaction window after it starts and no later than 60 ticks
/// in (the 80-tick cast's last safe stretch), before it resolves, by the ledger's kicker, and none completes.
/// The bounds are stated here independently of the production constants (Ledger.KickOpensTick).
/// </summary>
public sealed class KickReactionWindowTests
{
    /// <summary>The human reaction window: 8 ticks = 0.4 s at 20 Hz, so the cast bar is on screen before the kick.</summary>
    private const int ReactionWindowTicks = 8;

    /// <summary>The latest kick offset into the 80-tick (4 s) heal: 60 ticks leaves a full second of margin.</summary>
    private const int LatestKickTicks = 60;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the per-seed cast and kick ticks).</summary>
    /// <param name="output">The sink.</param>
    public KickReactionWindowTests(ITestOutputHelper output) => _output = output;

    private static List<string> Violations(long seed, IReadOnlyList<KickTimingTests.CastSeen> casts)
    {
        var failures = new List<string>();
        var owed = casts.Where(c => !c.CasterDied).ToList();
        if (owed.Count == 0)
        {
            failures.Add($"seed {seed}: no Choir heal cast owed a kick ({casts.Count} seen); the check would be vacuous");
        }
        foreach (var c in owed)
        {
            if (c.KickTick is not { } kick)
            {
                failures.Add($"seed {seed}: cast {c.Start}..{c.Ends} was never kicked (completed={c.Completed})");
                continue;
            }
            var offset = kick - c.Start;
            if (offset < ReactionWindowTicks)
            {
                failures.Add($"seed {seed}: cast {c.Start}..{c.Ends} kicked {offset} ticks in, before the {ReactionWindowTicks}-tick reaction window");
            }
            if (offset > LatestKickTicks || kick >= c.Ends)
            {
                failures.Add($"seed {seed}: cast {c.Start}..{c.Ends} kicked {offset} ticks in, past {LatestKickTicks} or after it resolved");
            }
            if (c.KickerId != c.LedgerKickerId)
            {
                failures.Add($"seed {seed}: cast {c.Start}..{c.Ends} kicked by {c.KickerId}, the ledger assigned {c.LedgerKickerId}");
            }
        }
        return failures;
    }

    [Fact(DisplayName = "Kick reaction window: key 2 seed 1, the first Choir heal is kicked between 8 and 60 ticks into the cast")]
    public void First_choir_heal_is_kicked_inside_the_window()
    {
        var first = KickTimingTests.DriveChoirPull(1).First(c => !c.CasterDied);
        _output.WriteLine($"first healCast start={first.Start} ends={first.Ends} kick={first.KickTick?.ToString() ?? "none"} kicker={first.KickerId?.ToString() ?? "none"}");
        Assert.NotNull(first.KickTick);
        Assert.InRange(first.KickTick!.Value - first.Start, ReactionWindowTicks, LatestKickTicks);
        Assert.False(first.Completed, "the first Choral Mending resolved");
    }

    [Fact(DisplayName = "Kick reaction window: seeds 1-20, every owed Choir heal is kicked 8-60 ticks in by the ledger's kicker and none completes")]
    public void Every_seed_kicks_every_heal_inside_the_window()
    {
        var failures = new List<string>();
        for (long seed = 1; seed <= 20; seed++)
        {
            var casts = KickTimingTests.DriveChoirPull(seed);
            foreach (var c in casts)
            {
                _output.WriteLine($"seed={seed} start={c.Start} ends={c.Ends} kick={c.KickTick?.ToString() ?? "none"} offset={(c.KickTick - c.Start)?.ToString() ?? "none"} kicker={c.KickerId?.ToString() ?? "none"} ledger={c.LedgerKickerId?.ToString() ?? "none"} completed={c.Completed} casterDied={c.CasterDied}");
            }
            failures.AddRange(Violations(seed, casts));
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
