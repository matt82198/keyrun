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
        Assert.Equal(Skill.ReactionDelayTicks(37), Skill.ReactionDelayTicks(37));
        Assert.Equal(Skill.ReactionDelayTicks(37), Skill.ReactionDelayTicks(37));
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
