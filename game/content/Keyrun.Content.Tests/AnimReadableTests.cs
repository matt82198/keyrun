using System;
using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// KF1-READABLE (finding f-fa28e36e, Matt 2026-10-19 "combat animation reads badly"). Two defects measured from the
/// 2026-10-20 run log (godot.log, 372 <c>cast_complete</c> events against 301 swing one-shots):
/// <list type="number">
/// <item>A caster that chains casts never shows a release: the bot starts the next Flare on the very step the last one
/// completes, so the sim actor still has a <c>CastId</c> and the cast loop beat the swing. 59 of 71 Flares drew no
/// release at all; the mage looped Spellcasting for the whole fight. A cast's own <c>cast_complete</c>
/// (<see cref="AnimFacts.Released"/>) now plays the release over the next cast's loop for
/// <see cref="AnimRule.ReleaseEndsMs"/>, then the cast pose resumes for the rest of the cast bar.</item>
/// <item>A frame that runs two or more sim steps (35 + 10 of 371 event batches) handed the animator only the last
/// step's events and before-state, so a swing, flinch or kick resolved on an earlier step of that frame never drew
/// (6 enemy melee swings lost). <see cref="StepBatch{TState,TEvent}"/> keeps every step of the frame.</item>
/// </list>
/// The engine proof line is <see cref="AnimRule.ViewLine"/>.
/// </summary>
public sealed class AnimReadableTests
{
    private static AnimFacts Facts(
        bool casting = false,
        bool acted = false,
        bool released = false,
        bool inRelease = false,
        AnimClip playing = AnimClip.None,
        bool newTick = true,
        bool hurt = false) =>
        new(true, newTick, false, casting, acted, hurt, false, playing, released, inRelease);

    [Fact]
    public void A_release_beats_the_cast_it_chains_into()
    {
        // Flare at tick 176: cast_complete for actor 3 and, on the same step, the next Flare's CastId.
        var events = new (string Type, int? ActorId)[] { ("cast_complete", 3) };
        var released = AnimRule.Resolved(3, events);
        var d = AnimRule.Decide(Facts(casting: true, acted: true, released: released));
        Assert.Equal(new AnimDecision(AnimClip.Act, true, true), d);
    }

    [Fact]
    public void The_release_holds_over_the_next_cast_until_its_window_ends()
    {
        var holding = AnimRule.Decide(Facts(casting: true, playing: AnimClip.Act, inRelease: true, newTick: false));
        Assert.Equal(new AnimDecision(AnimClip.Act, true, false), holding);
        var resumed = AnimRule.Decide(Facts(casting: true, playing: AnimClip.Act, inRelease: false, newTick: false));
        Assert.Equal(new AnimDecision(AnimClip.Cast, false, false), resumed);
    }

    [Fact]
    public void A_cast_start_still_loops_the_cast_pose()
    {
        // The GCD restarts when a cast begins (Acted), but nothing resolved: the cast pose holds for the cast bar.
        Assert.Equal(new AnimDecision(AnimClip.Cast, false, false), AnimRule.Decide(Facts(casting: true, acted: true)));
        // A release only counts on the frame its step lands.
        Assert.Equal(new AnimDecision(AnimClip.Cast, false, false),
            AnimRule.Decide(Facts(casting: true, released: true, newTick: false)));
        // A hit while casting does not cut the cast pose (the flash shows it).
        Assert.Equal(new AnimDecision(AnimClip.Cast, false, false), AnimRule.Decide(Facts(casting: true, hurt: true)));
    }

    [Theory]
    [InlineData("Spellcast_Shoot", 300)]
    [InlineData("Spellcast_Raise", 600)]
    [InlineData("1H_Melee_Attack_Chop", 700)]
    public void Release_window_runs_past_contact_by_the_tail(string clip, int contactMs)
    {
        var end = AnimRule.ReleaseEndsMs(clip);
        Assert.Equal(contactMs + AnimRule.ReleaseTailMs, end);
        Assert.True(AnimRule.InRelease(clip, AnimRule.SwingStartMs(clip)));
        Assert.True(AnimRule.InRelease(clip, contactMs));
        Assert.True(AnimRule.InRelease(clip, end - 1));
        Assert.False(AnimRule.InRelease(clip, end));
    }

    [Fact]
    public void Hit_presentation_reads_short_against_a_cast_bar()
    {
        // The shortest chained cast is Flare, 30 ticks (1500 ms). R-ANIM-2 (f): the readability intent (an instant never
        // reads like a cast) is carried by the hit presentation delay, not by the window: under (b) the clip plays from
        // frame 0 for its full length, so the window from start to release end is no longer bounded. The delay must be
        // in 1..300 and under one third of the shortest bar (1500 / 3 = 500).
        foreach (var row in ActorBinding.DefaultRows)
        {
            var clip = row.Clips.Act;
            Assert.Equal(0, AnimRule.SwingStartMs(clip));
            var delay = AnimRule.HitPresentationDelayMs(clip);
            Assert.InRange(delay, 1, 300);
            Assert.True(delay * 3 < 1500, $"{clip}: hit presentation delay {delay} ms is not under a third of a 1500 ms bar");
        }
    }

    [Fact]
    public void Two_steps_in_one_frame_keep_both_steps_events_and_the_frame_start_state()
    {
        var batch = new StepBatch<string, string>("s0");
        batch.Step(frame: 1, before: "s0", events: new[] { "a1" });
        batch.Step(frame: 1, before: "s1", events: new[] { "b1", "b2" });
        Assert.Equal("s0", batch.Start);
        Assert.Equal(new[] { "a1", "b1", "b2" }, batch.Events);

        batch.Step(frame: 2, before: "s2", events: new[] { "c1" });
        Assert.Equal("s2", batch.Start);
        Assert.Equal(new[] { "c1" }, batch.Events);

        batch.Reset("r0");
        Assert.Equal("r0", batch.Start);
        Assert.Empty(batch.Events);
        batch.Step(frame: 2, before: "r0", events: new[] { "d1" }); // same frame number after a reset still starts fresh
        Assert.Equal(new[] { "d1" }, batch.Events);
    }

    [Fact]
    public void A_swing_on_the_first_of_two_steps_still_draws()
    {
        var batch = new StepBatch<int, (string Type, int? ActorId)>(0);
        batch.Step(frame: 7, before: 0, events: new (string, int?)[] { ("cast_complete", 12) });
        batch.Step(frame: 7, before: 1, events: Array.Empty<(string, int?)>());
        var acted = AnimRule.Resolved(12, batch.Events);
        Assert.Equal(new AnimDecision(AnimClip.Act, true, true), AnimRule.Decide(Facts(acted: acted, released: acted)));
    }

    [Fact]
    public void View_line_is_the_engine_proof_format()
    {
        Assert.Equal("VIEW anim actor=3 clip=Spellcast_Shoot tick=176", AnimRule.ViewLine(3, "Spellcast_Shoot", 176));
    }
}
