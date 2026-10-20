using System.Collections.Generic;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>Port of tests/bots/skill-dial.test.ts (describe "skill dial", 6 cases).</summary>
public sealed class SkillDialTests
{
    [Fact(DisplayName = "skill dial > maps a higher dial to a strictly shorter (or equal) reaction delay")]
    public void HigherDialShorterDelay()
    {
        var worst = Skill.ReactionDelayTicks(0);
        var mid = Skill.ReactionDelayTicks(50);
        var best = Skill.ReactionDelayTicks(100);
        Assert.True(worst > mid);
        Assert.True(mid > best);
        Assert.True(best >= 1);
    }

    [Fact(DisplayName = "skill dial > is a pure function of the dial: same dial always yields the same delay")]
    public void PureFunctionOfDial()
    {
        // Pinned by hand from skill.ts reactionDelayTicks: Math.round(10 - (9 * clamp(dial, 0, 100)) / 100),
        // with JS Math.round (ties toward +infinity).
        //   dial   0: 10 - 0    = 10    -> 10
        //   dial  37: 10 - 3.33 = 6.67  -> 7
        //   dial  50: 10 - 4.5  = 5.5   -> 6 (the tie rounds up, not to even)
        //   dial 100: 10 - 9    = 1     -> 1
        // Each dial is asked twice so a stateful implementation (a counter, a drifting cache) cannot pass.
        foreach (var (dial, ticks) in new[] { (0.0, 10), (37.0, 7), (50.0, 6), (100.0, 1) })
        {
            Assert.Equal(ticks, Skill.ReactionDelayTicks(dial));
            Assert.Equal(ticks, Skill.ReactionDelayTicks(dial));
        }

        // The formula is non-increasing in the dial: a more skilled bot never waits longer.
        for (var dial = 0; dial < 100; dial++)
        {
            Assert.True(
                Skill.ReactionDelayTicks(dial + 1) <= Skill.ReactionDelayTicks(dial),
                $"reaction delay rose from dial {dial} to {dial + 1}");
        }
    }

    [Fact(DisplayName = "skill dial > clamps out-of-range dials")]
    public void ClampsOutOfRange()
    {
        Assert.Equal(Skill.ReactionDelayTicks(0), Skill.ReactionDelayTicks(-50));
        Assert.Equal(Skill.ReactionDelayTicks(100), Skill.ReactionDelayTicks(500));
    }

    [Fact(DisplayName = "skill dial > mistake chance is 0 at dial 100 and positive at dial 0")]
    public void MistakeChanceBounds()
    {
        Assert.Equal(0.0, Skill.MistakeChance(100));
        Assert.True(Skill.MistakeChance(0) > 0);
    }

    [Fact(DisplayName = "skill dial > stepPrng is deterministic: same seed and state always produce the same next value")]
    public void StepPrngDeterministic()
    {
        var seed = Skill.SeedPrng(42);
        var a = Skill.StepPrng(seed);
        var b = Skill.StepPrng(seed);
        Assert.Equal(a.Value, b.Value);
        Assert.Equal(a.Next, b.Next);
    }

    [Fact(DisplayName = "skill dial > rollMistake is reproducible: replaying the same seed reproduces the same mistake sequence")]
    public void RollMistakeReproducible()
    {
        var runA = Replay(Skill.SeedPrng(7), 20, 15);
        var runB = Replay(Skill.SeedPrng(7), 20, 15);
        Assert.Equal(runA, runB);
    }

    private static List<bool> Replay(BotPrngState seed, int dial, int steps)
    {
        var prng = seed;
        var output = new List<bool>();
        for (var i = 0; i < steps; i++)
        {
            var roll = Skill.RollMistake(dial, prng);
            output.Add(roll.Mistake);
            prng = roll.Next;
        }
        return output;
    }
}
