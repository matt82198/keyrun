using System;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// ANIMS-READABLE-2 (Matt 2026-10-21 "theres still no ability animations", f-a5ffb07b; KF1 f-fa28e36e). Measured on the
/// kf-main-0e748ca1 capture log (main 0e748ca1, after #544): every ability start logged an ANIM line, but
/// (1) a release a cast had wound up showed only 250 ms (contact to contact + tail) before the next chained cast's pose
/// took it back, under the 300 ms a swing needs to read at 20 Hz; (2) a second ability resolved on the same step drew
/// nothing (cinder.coldSnap 27 resolves, 13 clip starts; cinder.detonate 20 and 12) because the view read only the first
/// <c>cast_complete</c>. <see cref="ReleaseHold"/> holds every release for at least <see cref="ReleaseHold.MinReleaseMs"/>,
/// stretches a clip with less than that left, and lists every ability one step resolved so the view can play them in turn.
/// </summary>
public sealed class ReleaseHoldTests
{
    [Theory]
    [InlineData("Spellcast_Shoot", 300)]
    [InlineData("1H_Melee_Attack_Chop", 700)]
    [InlineData("2H_Melee_Attack_Slice", 300)]
    public void A_wound_up_release_holds_at_least_the_minimum(string clip, int contactMs)
    {
        // A cast that wound the swing up continues it from contact (AbilityAnim.ResolveStartMs woundUp: true).
        var start = AbilityAnim.ResolveStartMs(clip, woundUp: true);
        Assert.Equal(contactMs, start);
        Assert.True(ReleaseHold.InRelease(clip, start, start + ReleaseHold.MinReleaseMs - 1));
        Assert.False(ReleaseHold.InRelease(clip, start, start + ReleaseHold.MinReleaseMs));
        Assert.True(ReleaseHold.EndsMs(clip, start) - start >= ReleaseHold.MinReleaseMs);
    }

    [Theory]
    [InlineData("Spellcast_Shoot")]
    [InlineData("Unarmed_Melee_Attack_Punch_A")]
    [InlineData("Dodge_Forward")]
    [InlineData("Blocking")]
    public void An_instant_release_keeps_its_longer_window(string clip)
    {
        var start = AbilityAnim.ResolveStartMs(clip, woundUp: false);
        var ends = ReleaseHold.EndsMs(clip, start);
        Assert.Equal(Math.Max(AnimRule.ReleaseEndsMs(clip), start + ReleaseHold.MinReleaseMs), ends);
        Assert.InRange(ends - start, ReleaseHold.MinReleaseMs, 500); // never longer than a third of the shortest bar
    }

    [Theory]
    [InlineData(200, 933, 1000)] // Spellcast_Shoot from 200 ms: 733 ms left, plays at its own rate
    [InlineData(0, 400, 1000)] // Dodge_Forward: 400 ms, enough
    [InlineData(800, 1000, 666)] // 200 ms left: slowed to 2/3 so it lasts 300 ms
    [InlineData(1000, 1000, 1000)] // nothing left to stretch
    public void A_short_tail_is_stretched_to_the_minimum(int startMs, int lengthMs, int want)
    {
        Assert.Equal(want, ReleaseHold.SpeedPermille(startMs, lengthMs));
        var left = lengthMs - startMs;
        if (left > 0)
        {
            Assert.True(left * 1000 / ReleaseHold.SpeedPermille(startMs, lengthMs) >= ReleaseHold.MinReleaseMs - 1);
        }
    }

    [Fact]
    public void Every_ability_one_step_resolved_is_listed_in_order()
    {
        // Tick 112 of kf-main-0e748ca1: Flare and Cold Snap both resolve for actor 3 on one step.
        var events = new (string Type, int? ActorId, string? AbilityId)[]
        {
            ("damage", null, null),
            ("cast_complete", 3, "cinder.flare"),
            ("cast_complete", 1, "warden.strike"),
            ("cast_complete", 3, "cinder.coldSnap"),
        };
        Assert.Equal(new[] { "cinder.flare", "cinder.coldSnap" }, ReleaseHold.Resolved(3, events));
        Assert.Equal(new[] { "warden.strike" }, ReleaseHold.Resolved(1, events));
        Assert.Empty(ReleaseHold.Resolved(2, events));
    }

    [Fact]
    public void A_release_in_its_window_holds_off_the_next_wind_up()
    {
        Assert.True(ReleaseHold.HoldsOffWindup(AnimClip.Act, inRelease: true));
        Assert.False(ReleaseHold.HoldsOffWindup(AnimClip.Act, inRelease: false));
        Assert.False(ReleaseHold.HoldsOffWindup(AnimClip.Hit, inRelease: true));
        Assert.False(ReleaseHold.HoldsOffWindup(AnimClip.None, inRelease: true));
    }

    [Fact]
    public void A_queued_release_starts_once_the_current_one_leaves_its_window()
    {
        Assert.False(ReleaseHold.NextQueuedNow(queued: 1, AnimClip.Act, inRelease: true));
        Assert.True(ReleaseHold.NextQueuedNow(queued: 1, AnimClip.Act, inRelease: false));
        Assert.True(ReleaseHold.NextQueuedNow(queued: 1, AnimClip.None, inRelease: false));
        Assert.False(ReleaseHold.NextQueuedNow(queued: 0, AnimClip.None, inRelease: false));
    }

    [Fact]
    public void Every_table_release_is_a_clip_with_a_window()
    {
        foreach (var row in AbilityClipTable.Rows)
        {
            foreach (var woundUp in new[] { false, true })
            {
                var start = AbilityAnim.ResolveStartMs(row.Release, woundUp);
                Assert.True(ReleaseHold.EndsMs(row.Release, start) - start >= ReleaseHold.MinReleaseMs, row.AbilityId);
            }
        }
        Assert.True(AbilityClipTable.Rows.Count(r => AnimRule.ContactMs(r.Release) is null) > 0, "vacuity: no clip without contact");
    }
}
