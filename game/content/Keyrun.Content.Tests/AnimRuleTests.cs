using System;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The actor animation rule (<see cref="AnimRule"/>, COMBAT-ANIM, KF1): every swing, cast and hit reads on an actor in
/// motion (BIBLE s3 "cast bars are the enemy's language"). A cast loop wins over everything but death; a resolved
/// ability (the sim's <c>cast_complete</c> for that actor, instants included) restarts the swing one-shot whether the
/// actor moves or not; a stagger or a health drop restarts the hit one-shot (a hit never cuts a swing short); a one-shot
/// is held to its end, moving or not, then the actor returns to run or idle.
/// </summary>
public sealed class AnimRuleTests
{
    private static AnimFacts Facts(
        bool moving = false,
        bool casting = false,
        bool acted = false,
        bool hurt = false,
        bool staggered = false,
        AnimClip playing = AnimClip.None,
        bool newTick = true,
        bool alive = true) =>
        new(alive, newTick, moving, casting, acted, hurt, staggered, playing);

    public static TheoryData<string, AnimFacts, AnimClip, bool, bool> Table => new()
    {
        // name, facts, clip, one-shot, restart
        { "swing while moving", Facts(moving: true, acted: true), AnimClip.Act, true, true },
        { "swing standing", Facts(acted: true), AnimClip.Act, true, true },
        { "hit while moving", Facts(moving: true, hurt: true), AnimClip.Hit, true, true },
        { "hit standing", Facts(hurt: true), AnimClip.Hit, true, true },
        { "cast loop wins over swing", Facts(casting: true, acted: true), AnimClip.Cast, false, false },
        { "cast loop wins over hit", Facts(moving: true, casting: true, hurt: true), AnimClip.Cast, false, false },
        { "swing beats hit on the same tick", Facts(acted: true, hurt: true), AnimClip.Act, true, true },
        { "a hit never cuts a swing short", Facts(moving: true, hurt: true, playing: AnimClip.Act), AnimClip.Act, true, false },
        { "a new swing restarts over a hit", Facts(acted: true, playing: AnimClip.Hit), AnimClip.Act, true, true },
        { "stagger restarts the hit", Facts(staggered: true, playing: AnimClip.Hit), AnimClip.Hit, true, true },
        { "one-shot held while moving", Facts(moving: true, playing: AnimClip.Act, newTick: false), AnimClip.Act, true, false },
        { "one-shot ended returns to run", Facts(moving: true), AnimClip.Move, false, false },
        { "one-shot ended returns to idle", Facts(), AnimClip.Idle, false, false },
        { "facts only fire on a new tick", Facts(acted: true, hurt: true, newTick: false), AnimClip.Idle, false, false },
        { "death wins over all", Facts(alive: false, casting: true, acted: true), AnimClip.Death, true, false },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Rule_table(string name, AnimFacts facts, AnimClip clip, bool oneShot, bool restart)
    {
        AnimDecision d = AnimRule.Decide(facts);
        Assert.True(d == new AnimDecision(clip, oneShot, restart), $"{name}: got {d}");
    }

    [Fact]
    public void Enemy_swing_signal_is_its_own_cast_complete()
    {
        // choir.meleeSwing is castTicks 0, gcdTicks 0: it never moves GcdReadyTick, but it does emit cast_complete.
        var events = new (string Type, int? ActorId)[]
        {
            ("damage", null),
            ("cast_complete", 7),
            ("cast_stopped", 9),
        };
        Assert.True(AnimRule.Resolved(7, events));
        Assert.False(AnimRule.Resolved(9, events)); // a stopped cast is not a swing
        Assert.False(AnimRule.Resolved(3, events));
        Assert.False(AnimRule.Resolved(7, Array.Empty<(string, int?)>()));
    }

    [Fact]
    public void Enemy_swing_on_its_signal_plays_the_act_one_shot_while_chasing()
    {
        var events = new (string Type, int? ActorId)[] { ("cast_complete", 12) };
        var acted = AnimRule.Resolved(12, events);
        Assert.Equal(new AnimDecision(AnimClip.Act, true, true), AnimRule.Decide(Facts(moving: true, acted: acted)));
    }
}
