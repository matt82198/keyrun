using System;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// CAMERA-HEADROOM (KF1 demo receipts demo-012-t048s and demo-025-t050s): at the Hollow Warden's melee reach the 4.7 m boss
/// filled the frame and its head was cut off at the top edge at the default boom and pitch, while the Choir and the Pair
/// framed fine. <see cref="CameraHeadroom"/> holds the rule; game/src/View/CameraRig.cs applies it.
/// </summary>
public sealed class CameraHeadroomTests
{
    private const int Fov = CameraFraming.VerticalFovDeg;
    private const int Pivot = CameraFraming.PivotHeightMm;
    private const int Headroom = 850;
    private const int MaxBoom = CameraFraming.MaxZoomMm;
    private const int Ceiling = -50;
    private const int MeleeReachMm = 2000;

    private static int TopOf(string key)
    {
        ActorBindingRow row = ActorBinding.Default.For(key);
        return Math.Max(row.TargetHeightMm, row.StandingHeightMm);
    }

    // The top's tangent above the view axis, computed in doubles from the camera geometry (camera at pivot - B f, f =
    // (0, sin p, -cos p), target top at (0, y, -d) from the pivot): independent of the integer rule under test.
    private static double TopTangent(int boomMm, int pitchMrad, int topMm, int distanceMm)
    {
        var p = pitchMrad / 1000.0;
        var y = (double)(topMm - Pivot);
        var d = (double)distanceMm;
        var b = (double)boomMm;
        var height = y * Math.Cos(p) - d * Math.Sin(p);
        var depth = y * Math.Sin(p) + d * Math.Cos(p) + b;
        return height / depth;
    }

    private static double HeadroomTangent() => Math.Tan(Fov / 2.0 * Math.PI / 180.0) * Headroom / 1000.0;

    [Fact]
    public void Boss_at_melee_reach_pulls_the_camera_back_so_its_top_is_in_frame()
    {
        int top = TopOf("hollowWarden.boss");
        Assert.Equal(4687, top);

        var fit = CameraHeadroom.Adjust(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, Fov, Pivot, top,
            MeleeReachMm, Headroom, MaxBoom, Ceiling);

        Assert.True(fit.Fits);
        Assert.Equal(CameraFraming.DefaultPitchMrad, fit.PitchMrad);
        Assert.InRange(fit.BoomMm, 6500, 7200);
        Assert.True(TopTangent(fit.BoomMm, fit.PitchMrad, top, MeleeReachMm) <= HeadroomTangent() + 1e-9);
        // The rule's own check at the old boom is the receipt: that boom leaves the top out of frame.
        Assert.True(TopTangent(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, top, MeleeReachMm) > HeadroomTangent());
    }

    [Fact]
    public void Choir_at_melee_reach_is_unchanged()
    {
        int top = TopOf("choir.melee");
        Assert.Equal(1800, top); // Skeleton_Warrior 2590 mm native at 695 permille stands 1800 mm

        var fit = CameraHeadroom.Adjust(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, Fov, Pivot, top,
            MeleeReachMm, Headroom, MaxBoom, Ceiling);

        Assert.Equal(new HeadroomFit(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, true), fit);
    }

    [Fact]
    public void Boss_far_away_keeps_the_follow_distance()
    {
        var fit = CameraHeadroom.Adjust(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, Fov, Pivot,
            TopOf("hollowWarden.boss"), 20_000, Headroom, MaxBoom, Ceiling);

        Assert.Equal(new HeadroomFit(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, true), fit);
    }

    [Fact]
    public void Needed_boom_falls_as_the_boss_moves_away_so_the_camera_returns_smoothly()
    {
        int top = TopOf("hollowWarden.boss");
        int previous = int.MaxValue;
        foreach (var distance in new[] { 500, 1000, 2000, 4000, 8000, 16_000 })
        {
            var fit = CameraHeadroom.Adjust(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, Fov, Pivot, top,
                distance, Headroom, MaxBoom, Ceiling);
            Assert.True(fit.Fits);
            Assert.True(fit.BoomMm <= previous, $"boom grew from {previous} to {fit.BoomMm} at {distance} mm");
            previous = fit.BoomMm;
        }
    }

    [Fact]
    public void Pitch_turns_up_only_when_the_largest_boom_cannot_frame_the_top()
    {
        int top = TopOf("hollowWarden.boss");
        const int capBoom = 6000;

        var fit = CameraHeadroom.Adjust(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, Fov, Pivot, top,
            MeleeReachMm, Headroom, capBoom, Ceiling);

        Assert.True(fit.Fits);
        Assert.True(fit.PitchMrad > CameraFraming.DefaultPitchMrad, "the pitch must turn up (flatter)");
        Assert.True(fit.PitchMrad <= Ceiling);
        Assert.Equal(0, (fit.PitchMrad - CameraFraming.DefaultPitchMrad) % CameraHeadroom.PitchStepMrad);
        Assert.True(fit.BoomMm <= capBoom);
        Assert.True(TopTangent(fit.BoomMm, fit.PitchMrad, top, MeleeReachMm) <= HeadroomTangent() + 1e-9);
    }

    [Fact]
    public void A_top_no_boom_or_pitch_can_frame_reports_unsolved_at_the_limits()
    {
        var fit = CameraHeadroom.Adjust(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, Fov, Pivot,
            TopOf("hollowWarden.boss"), MeleeReachMm, Headroom, CameraFraming.DefaultBoomMm, Ceiling);

        Assert.False(fit.Fits);
        Assert.Equal(CameraFraming.DefaultBoomMm, fit.BoomMm);
        Assert.Equal(Ceiling, fit.PitchMrad);
    }

    [Fact]
    public void A_top_below_the_pivot_axis_is_unchanged()
    {
        var fit = CameraHeadroom.Adjust(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, Fov, Pivot,
            Pivot - 200, MeleeReachMm, Headroom, MaxBoom, Ceiling);

        Assert.Equal(new HeadroomFit(CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, true), fit);
    }

    [Fact]
    public void Non_positive_boom_or_headroom_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => CameraHeadroom.Adjust(0, -300, Fov, Pivot, 4687, 2000, Headroom, MaxBoom, Ceiling));
        Assert.Throws<ArgumentException>(() => CameraHeadroom.Adjust(5000, -300, Fov, Pivot, 4687, 2000, 0, MaxBoom, Ceiling));
        Assert.Throws<ArgumentException>(() => CameraHeadroom.Adjust(5000, -300, Fov, Pivot, 4687, 2000, 1001, MaxBoom, Ceiling));
    }

    // CAMERA-HEADROOM body choice (the Hollow Warden is framed while it stands in the Warden's reach, even when the Warden
    // targets a Sundered Twin). The pivot is the origin; the camera is at (0, camZ) looking down +Z.
    private const int Reach = 3000;

    [Fact]
    public void Boss_beside_the_twin_in_cone_and_reach_drives_the_headroom_not_the_twin()
    {
        var bodies = new[]
        {
            new HeadroomBody(0, 1000, TopOf("hollowWarden.twinB"), Hostile: true, Engaged: true),
            new HeadroomBody(800, 1000, TopOf("hollowWarden.boss"), Hostile: true, Engaged: true),
        };

        Assert.Equal(1, CameraHeadroomBody.Select(bodies, 0, -CameraFraming.DefaultBoomMm, 0, 1000, 0, 0, Reach));
    }

    [Fact]
    public void Boss_behind_the_camera_is_not_framed_so_the_twin_is()
    {
        var bodies = new[]
        {
            new HeadroomBody(0, 1000, TopOf("hollowWarden.twinB"), Hostile: true, Engaged: true),
            new HeadroomBody(0, -2500, TopOf("hollowWarden.boss"), Hostile: true, Engaged: true),
        };

        Assert.Equal(0, CameraHeadroomBody.Select(bodies, 0, -2000, 0, 1000, 0, 0, Reach));
    }

    [Fact]
    public void An_idle_boss_is_not_engaged_so_the_twin_is_framed()
    {
        var bodies = new[]
        {
            new HeadroomBody(0, 1000, TopOf("hollowWarden.twinB"), Hostile: true, Engaged: true),
            new HeadroomBody(800, 1000, TopOf("hollowWarden.boss"), Hostile: true, Engaged: false),
        };

        Assert.Equal(0, CameraHeadroomBody.Select(bodies, 0, -CameraFraming.DefaultBoomMm, 0, 1000, 0, 0, Reach));
    }

    [Fact]
    public void No_engaged_hostile_in_cone_and_reach_selects_nothing()
    {
        var bodies = new[]
        {
            new HeadroomBody(0, 6000, TopOf("hollowWarden.boss"), Hostile: true, Engaged: true),
            new HeadroomBody(0, 1000, TopOf("hollowWarden.boss"), Hostile: false, Engaged: true),
        };

        Assert.Null(CameraHeadroomBody.Select(bodies, 0, -CameraFraming.DefaultBoomMm, 0, 1000, 0, 0, Reach));
    }
}
