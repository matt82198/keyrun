using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The strike cue rule (<see cref="StrikeCueRule"/>, KF1): every melee ability that lands damage on a tick emits one
/// <see cref="StrikeCue"/> on that tick, carrying the attacker's actor id, facing and the cue duration. Instants and
/// casts count alike (both emit cast_complete); an ability that is not tagged melee, damage on another tick or from
/// another actor, and a melee resolve with no damage emit nothing.
/// </summary>
public sealed class StrikeCueTests
{
    private const int Warden = 1;
    private const int Yaw = 256;

    private static IReadOnlySet<string> Melee => new HashSet<string>(new[] { "warden.strike", "choir.meleeSwing", "pair.meleeSwing" });

    private static IReadOnlyList<StrikeCue> Run(params StrikeEvent[] events) =>
        StrikeCueRule.Cues(events, Melee, _ => Yaw);

    [Fact]
    public void Strike_landing_damage_emits_one_cue_on_the_damage_tick()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 42, Warden, "warden.strike"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 42, Warden));

        var cue = Assert.Single(cues);
        Assert.Equal(Warden, cue.ActorId);
        Assert.Equal(42, cue.Tick);
        Assert.Equal(Yaw, cue.Yaw);
        Assert.Equal(StrikeCueRule.DurationMs, cue.DurationMs);
        Assert.Equal("warden.strike", cue.AbilityId);
    }

    [Theory]
    [InlineData("warden.strike")]
    [InlineData("choir.meleeSwing")]
    [InlineData("pair.meleeSwing")]
    public void Every_melee_ability_landing_damage_emits_a_cue(string abilityId)
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 7, Warden, abilityId),
            new StrikeEvent(StrikeCueRule.DamageEvent, 7, Warden));

        Assert.Equal(abilityId, Assert.Single(cues).AbilityId);
    }

    [Fact]
    public void Melee_set_is_the_abilities_tagged_melee()
    {
        var ids = StrikeCueRule.MeleeIds(new (string, IReadOnlyList<string>?)[]
        {
            ("warden.strike", new[] { "setup:sundered" }),
            ("choir.meleeSwing", new[] { StrikeCueRule.MeleeTag }),
            ("cinder.flare", null),
        });

        Assert.Equal(new[] { "choir.meleeSwing" }, ids.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Ability_not_tagged_melee_emits_nothing()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 9, Warden, "cinder.flare"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 9, Warden));

        Assert.Empty(cues);
    }

    [Fact]
    public void Damage_on_another_tick_emits_nothing()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 10, Warden, "warden.strike"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 11, Warden));

        Assert.Empty(cues);
    }

    [Fact]
    public void Damage_from_another_actor_emits_nothing()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 12, Warden, "warden.strike"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 12, 5));

        Assert.Empty(cues);
    }

    [Fact]
    public void Melee_resolve_with_no_damage_emits_nothing()
    {
        var cues = Run(new StrikeEvent(StrikeCueRule.ResolveEvent, 13, Warden, "warden.strike"));

        Assert.Empty(cues);
    }

    [Fact]
    public void Cleave_hitting_three_targets_on_one_tick_is_one_cue()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 14, Warden, "warden.strike"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 14, Warden),
            new StrikeEvent(StrikeCueRule.DamageEvent, 14, Warden),
            new StrikeEvent(StrikeCueRule.DamageEvent, 14, Warden));

        Assert.Single(cues);
    }

    [Fact]
    public void Facing_is_read_from_the_attacker_on_the_step()
    {
        var cues = StrikeCueRule.Cues(
            new[]
            {
                new StrikeEvent(StrikeCueRule.ResolveEvent, 15, Warden, "warden.strike"),
                new StrikeEvent(StrikeCueRule.DamageEvent, 15, Warden),
            },
            Melee,
            id => id == Warden ? 768 : 0);

        Assert.Equal(768, Assert.Single(cues).Yaw);
    }

    // f-e20614cc (KF1 "combat animation reads badly"): the core dungeon loop defers to WoW, whose melee swing animation
    // runs about 0.5 to 1.0 s. The old 200 ms cue showed in 1 of about 30 combat frames and read as a floor line.
    [Fact]
    public void Duration_is_the_wow_swing_window_500_ms()
    {
        Assert.Equal(500, StrikeCueRule.DurationMs);
    }

    [Fact]
    public void Cue_carries_the_target_it_struck()
    {
        var cues = Run(
            new StrikeEvent(StrikeCueRule.ResolveEvent, 16, Warden, "warden.strike"),
            new StrikeEvent(StrikeCueRule.DamageEvent, 16, Warden, null, 9));

        Assert.Equal(9, Assert.Single(cues).TargetId);
    }

    [Fact]
    public void Cue_height_stays_in_the_hand_band_for_the_whole_swing()
    {
        for (var ms = 0; ms < StrikeCueRule.DurationMs; ms++)
        {
            var pose = StrikeCueRule.Pose(0, 0, 2000, 0, ms);
            Assert.InRange(pose.HeightMm, 1000, 1200);
        }
    }

    [Theory]
    [InlineData(1000, 2000, 3000, 2000, 2000, 0)]
    [InlineData(1000, 2000, -500, 2500, -1500, 500)]
    [InlineData(0, 0, 0, -1800, 0, -1800)]
    public void Cue_aims_from_the_caster_to_the_target(int cx, int cz, int tx, int tz, int ax, int az)
    {
        var pose = StrikeCueRule.Pose(cx, cz, tx, tz, 0);

        Assert.True(pose.HasAim);
        Assert.Equal(ax, pose.AimXMm);
        Assert.Equal(az, pose.AimZMm);
    }

    [Fact]
    public void Cue_with_no_separate_target_falls_back_to_facing()
    {
        Assert.False(StrikeCueRule.Pose(500, 500, 500, 500, 0).HasAim);
    }

    [Fact]
    public void Cue_sweeps_across_the_target_and_drops_over_the_window()
    {
        var first = StrikeCueRule.Pose(0, 0, 2000, 0, 0);
        var last = StrikeCueRule.Pose(0, 0, 2000, 0, StrikeCueRule.DurationMs - 1);
        var mid = StrikeCueRule.Pose(0, 0, 2000, 0, StrikeCueRule.DurationMs / 2);

        Assert.True(first.SweepYaw < 0, $"swing starts to one side, got {first.SweepYaw}");
        Assert.True(last.SweepYaw > 0, $"swing ends to the other side, got {last.SweepYaw}");
        Assert.InRange(mid.SweepYaw, -8, 8);
        Assert.True(last.SweepYaw - first.SweepYaw >= 256, $"arc spans at least a quarter turn, got {last.SweepYaw - first.SweepYaw}");
        Assert.True(first.HeightMm > last.HeightMm, $"swing comes down, {first.HeightMm} -> {last.HeightMm}");
        var prev = first.SweepYaw;
        for (var ms = 1; ms < StrikeCueRule.DurationMs; ms++)
        {
            var yaw = StrikeCueRule.Pose(0, 0, 2000, 0, ms).SweepYaw;
            Assert.True(yaw >= prev, $"sweep goes one way at {ms} ms");
            prev = yaw;
        }
    }

    [Fact]
    public void Cue_shows_on_30_of_60_frames_in_one_second()
    {
        var shown = 0;
        for (var frame = 0; frame < 60; frame++)
        {
            if (StrikeCueRule.Pose(0, 0, 2000, 0, frame * 1000 / 60).Visible)
            {
                shown++;
            }
        }

        Assert.Equal(30, shown);
    }

    [Fact]
    public void Cue_is_hidden_outside_its_window()
    {
        Assert.False(StrikeCueRule.Pose(0, 0, 2000, 0, -1).Visible);
        Assert.False(StrikeCueRule.Pose(0, 0, 2000, 0, StrikeCueRule.DurationMs).Visible);
    }
}
