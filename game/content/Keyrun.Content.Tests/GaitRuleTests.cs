using System;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// KF1 (Matt 2026-10-19, "combat animation reads badly"), gait: the run and walk clips played at speed 1 whatever the
/// sim speed, so every body slid across the floor 1.3x to 2.6x faster than its feet stepped (ice skating). BIBLE s0
/// test 1 needs a fight that reads by feel, and a pack that skates in reads as fake before the first blow lands.
/// <see cref="GaitRule.PlaybackPermille"/> plays the loop at the rate that keeps the planted foot still on the
/// floor: sim speed over the clip's planted-foot ground speed at the model's scale.
/// </summary>
public sealed class GaitRuleTests
{
    // Sim speeds, mm/s at 20 Hz: the player base 180 mm/tick and the melee chasers' 207 mm/tick
    // (content/data/TUNING.md "speedMmPerTick 207 = provisional 1.15x the 180 mm/tick player base").
    private const int BaseMmps = 180 * 20;
    private const int ChaserMmps = 207 * 20;

    // The world speed the planted foot travels at when the clip plays at playbackPermille.
    private static long FootWorldMmps(string clip, int scalePermille, int playbackPermille) =>
        (long)GaitRule.GroundMmps(clip)!.Value * scalePermille / 1000 * playbackPermille / 1000;

    [Fact]
    public void Every_bound_move_clip_has_a_planted_foot_speed()
    {
        foreach (var row in ActorBinding.DefaultRows)
        {
            Assert.True(GaitRule.GroundMmps(row.Clips.Move) is > 0,
                $"{row.Key}: move clip {row.Clips.Move} has no planted-foot speed, so its feet slide");
        }
    }

    [Theory]
    [InlineData("warden", BaseMmps)]
    [InlineData("cinder", BaseMmps)]
    [InlineData("chanter", BaseMmps)]
    [InlineData("choir.melee", ChaserMmps)]
    [InlineData("choir.lieutenant", BaseMmps)]
    [InlineData("pair.melee", ChaserMmps)]
    [InlineData("seed.elite", BaseMmps)]
    [InlineData("hollowWarden.twinA", BaseMmps)]
    [InlineData("hollowWarden.boss", BaseMmps)]
    public void Every_bound_body_plants_its_feet_at_its_sim_speed(string key, int speedMmps)
    {
        var row = ActorBinding.Default.For(key);
        var playback = GaitRule.PlaybackPermille(row.Clips.Move, speedMmps, row.ScalePermille);
        var foot = FootWorldMmps(row.Clips.Move, row.ScalePermille, playback);
        Assert.True(Math.Abs(foot - speedMmps) * 100 <= speedMmps,
            $"{key}: body moves {speedMmps} mm/s but its planted foot sweeps {foot} mm/s at playback {playback} permille");
    }

    [Fact]
    public void The_party_warden_runs_half_again_faster_than_the_clip_at_walk_speed()
    {
        // Running_A plants at 3370 native mm/s; the Knight is drawn at 0.709, so 2389 mm/s at speed 1 against 3600.
        Assert.Equal(1506, GaitRule.PlaybackPermille("Running_A", BaseMmps, 709));
    }

    [Fact]
    public void A_slower_body_steps_slower_and_a_bigger_body_steps_slower_at_the_same_speed()
    {
        var full = GaitRule.PlaybackPermille("Running_A", BaseMmps, 709);
        var half = GaitRule.PlaybackPermille("Running_A", BaseMmps / 2, 709);
        var bigger = GaitRule.PlaybackPermille("Running_A", BaseMmps, 709 * 2);
        Assert.True(half < full, $"half speed {half} should step slower than full {full}");
        Assert.True(bigger < full, $"a model twice the size {bigger} should take fewer steps than {full}");
    }

    [Fact]
    public void A_render_spike_is_clamped_so_the_legs_never_blur()
    {
        Assert.Equal(GaitRule.MaxPermille, GaitRule.PlaybackPermille("Running_A", 100_000, 709));
        Assert.Equal(GaitRule.MinPermille, GaitRule.PlaybackPermille("Running_A", 401, 1900));
    }

    [Theory]
    [InlineData("Idle", 3600, 709)] // not a gait clip: nothing to plant
    [InlineData("Running_A", 0, 709)] // not moving
    [InlineData("Running_A", 3600, 0)] // a degenerate scale
    public void Anything_without_a_stride_to_match_plays_at_its_own_rate(string clip, int speedMmps, int scale)
    {
        Assert.Equal(GaitRule.UnitPermille, GaitRule.PlaybackPermille(clip, speedMmps, scale));
    }
}
