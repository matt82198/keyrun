using System;
using System.Reflection;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// R-ANIM-2 (b) (serves f-905bf874 and f-a5ffb07b; slice f-e8a691c0): the attack clip starts at frame 0 on the resolve
/// tick, and the hit presentation (victim spark, body flash, floating damage) is delayed to the clip's contact frame,
/// capped at 300 ms. Strike's release clip is Slice_Horizontal (contact 200 ms) so the lag stays under 300 ms. The sim
/// damage tick is unchanged; the delay is presentation only. Red on this tree: <see cref="AnimRule.SwingStartMs"/>
/// starts the release 100 ms before contact (600 ms into Chop), the hit presentation has no delay seam, and
/// <see cref="AbilityClipTable"/> still releases Chop for warden.strike.
/// </summary>
public sealed class AnimAnticipationTests
{
    private const string AbilityId = "warden.strike";
    private const string CurrentRelease = "1H_Melee_Attack_Chop";
    private const int HitPresentationCapMs = 300;

    [Fact]
    public void Instant_release_clip_starts_at_frame_zero_on_the_resolve_tick()
    {
        // The rule's decision on the resolve tick: a swing one-shot restarted (AnimRule.Decide, Act, restart).
        var decision = AnimRule.Decide(new AnimFacts(
            Alive: true, NewTick: true, Moving: false, Casting: false, Acted: true,
            Hurt: false, Staggered: false, Playing: AnimClip.None));
        Assert.Equal(AnimClip.Act, decision.Clip);
        Assert.True(decision.Restart);

        string release = AbilityClipTable.ReleaseClip(AbilityId, CurrentRelease);
        int startMs = AnimRule.SwingStartMs(release);

        Assert.True(
            startMs == 0,
            $"{AbilityId}: release clip '{release}' is started at clip ms {startMs} on the resolve tick (tick 0), " +
            $"expected 0 ms (frame 0 on the resolve tick; contact {AnimRule.ContactMs(release)} ms, " +
            $"SwingStartMs = contact minus {AnimRule.SwingLeadMs} ms)");
    }

    [Fact]
    public void Hit_presentation_is_delayed_to_the_contact_frame_capped_300ms()
    {
        string release = AbilityClipTable.ReleaseClip(AbilityId, CurrentRelease);
        int contact = AnimRule.ContactMs(release) ?? -1;
        Assert.True(contact >= 0, $"{release}: no contact frame in the rule table");
        int expected = Math.Min(contact, HitPresentationCapMs);

        // The seam the rule needs: AnimRule.HitPresentationDelayMs(clip). Looked up by name so this fact fails at
        // runtime (with the seam named) instead of breaking the build of the whole test project.
        MethodInfo? seam = typeof(AnimRule).GetMethod(
            "HitPresentationDelayMs", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
        if (seam is null)
        {
            Assert.Fail(
                $"{AbilityId}: AnimRule has no HitPresentationDelayMs(string clip) seam; the hit presentation " +
                $"(spark, flash, floating damage) has no contact delay. Expected {expected} ms " +
                $"(min(contact {contact} ms of '{release}', {HitPresentationCapMs} ms)).");
            return;
        }

        int actual = (int)seam.Invoke(null, new object[] { release })!;
        Assert.True(
            actual == expected,
            $"{AbilityId}: hit presentation delay for '{release}' is {actual} ms, expected {expected} ms " +
            $"(min(contact {contact} ms, {HitPresentationCapMs} ms)).");
    }

    [Fact]
    public void Strike_release_clip_is_Slice_Horizontal()
    {
        string release = AbilityClipTable.ReleaseClip(AbilityId, CurrentRelease);
        Assert.True(
            release == "1H_Melee_Attack_Slice_Horizontal",
            $"{AbilityId}: release clip is '{release}', expected '1H_Melee_Attack_Slice_Horizontal' " +
            $"(contact {AnimRule.ContactMs("1H_Melee_Attack_Slice_Horizontal")} ms keeps the hit lag under " +
            $"{HitPresentationCapMs} ms)");
    }
}
