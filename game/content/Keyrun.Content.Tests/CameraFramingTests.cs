using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// CAMERA-FRAMING (Matt 2026-10-20: "it should feel almost exact to wow with less button clutter"; demo verdict KF1:
/// combat animation reads badly because the camera drew the fight about 60 px tall in a 720 px frame). The default
/// follow camera frames the player like WoW's: over the shoulder, the player's body a quarter to a third of the frame
/// tall, mouse-wheel zoom clamped between a near and a far boom. <see cref="CameraFraming"/> holds the rule;
/// game/src/View/CameraRig.cs applies it and game/src/View/CameraFramingSceneTest.ps1 checks the drawn frame.
/// </summary>
public sealed class CameraFramingTests
{
    private static int WardenHeightMm => ActorBinding.DefaultRows.Single(r => r.Key == "warden").TargetHeightMm;

    [Fact]
    public void Framed_player_height_is_the_wardens_drawn_height()
    {
        Assert.Equal(CameraFraming.PlayerHeightMm, WardenHeightMm);
    }

    [Fact]
    public void Default_framing_draws_the_player_a_fifth_to_a_third_of_the_frame_tall()
    {
        var permille = CameraFraming.BodyScreenPermille(
            CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, CameraFraming.VerticalFovDeg,
            CameraFraming.PivotHeightMm, CameraFraming.PlayerHeightMm);
        Assert.InRange(permille, CameraFraming.PlayerMinPermille, CameraFraming.PlayerMaxPermille);
        Assert.Equal(200, CameraFraming.PlayerMinPermille);
        Assert.Equal(350, CameraFraming.PlayerMaxPermille);
    }

    [Fact]
    public void Default_framing_keeps_the_players_feet_above_the_ability_bar()
    {
        // The bar's top edge sits 55 percent of the way down the lower half-frame (CameraRig.HudBottomPermille 550).
        var feet = CameraFraming.BelowAxisPermille(
            CameraFraming.DefaultBoomMm, CameraFraming.DefaultPitchMrad, CameraFraming.VerticalFovDeg, -CameraFraming.PivotHeightMm);
        Assert.InRange(feet, 0, 550);
    }

    [Fact]
    public void Default_boom_is_inside_the_zoom_clamp()
    {
        Assert.InRange(CameraFraming.DefaultBoomMm, CameraFraming.MinZoomMm, CameraFraming.MaxZoomMm);
        Assert.True(CameraFraming.MinZoomMm < CameraFraming.MaxZoomMm);
    }

    [Fact]
    public void Wheel_zoom_moves_one_step_per_notch_and_is_clamped_at_both_ends()
    {
        var b = CameraFraming.DefaultBoomMm;
        Assert.Equal(b - CameraFraming.ZoomStepMm, CameraFraming.Zoom(b, -1));
        Assert.Equal(b + CameraFraming.ZoomStepMm, CameraFraming.Zoom(b, 1));
        Assert.Equal(CameraFraming.MinZoomMm, CameraFraming.Zoom(b, -1000));
        Assert.Equal(CameraFraming.MaxZoomMm, CameraFraming.Zoom(b, 1000));
        Assert.Equal(CameraFraming.MinZoomMm, CameraFraming.Zoom(CameraFraming.MinZoomMm, -1));
        Assert.Equal(CameraFraming.MaxZoomMm, CameraFraming.Zoom(CameraFraming.MaxZoomMm, 1));
    }

    [Fact]
    public void Even_fully_zoomed_out_the_player_stays_readable_and_fully_in_the_player_never_overfills_the_frame()
    {
        var far = CameraFraming.BodyScreenPermille(
            CameraFraming.MaxZoomMm, CameraFraming.DefaultPitchMrad, CameraFraming.VerticalFovDeg,
            CameraFraming.PivotHeightMm, CameraFraming.PlayerHeightMm);
        var near = CameraFraming.BodyScreenPermille(
            CameraFraming.MinZoomMm, CameraFraming.DefaultPitchMrad, CameraFraming.VerticalFovDeg,
            CameraFraming.PivotHeightMm, CameraFraming.PlayerHeightMm);
        Assert.InRange(far, 100, CameraFraming.PlayerMinPermille);
        Assert.InRange(near, CameraFraming.PlayerMaxPermille, 700);
    }

    [Fact]
    public void Screen_height_shrinks_as_the_boom_grows_and_with_a_wider_lens()
    {
        int At(int boom, int fov) => CameraFraming.BodyScreenPermille(boom, CameraFraming.DefaultPitchMrad, fov, 1400, 1750);
        Assert.True(At(4000, 59) > At(6000, 59));
        Assert.True(At(6000, 59) > At(9000, 59));
        Assert.True(At(6000, 59) > At(6000, 70));
    }

    [Fact]
    public void The_pre_framing_camera_drew_the_player_too_small()
    {
        // The camera before CAMERA-FRAMING: 7.5 m boom (and the party fit could push it to 12 m), pitch -0.42 rad,
        // 70 degree vertical fov. Even its nearest default is under a fifth of the frame: KF1's 60 px of 720.
        var old = CameraFraming.BodyScreenPermille(7500, -420, 70, 1400, 1750);
        var oldFar = CameraFraming.BodyScreenPermille(12000, -420, 70, 1400, 1750);
        Assert.InRange(old, 120, 160);
        Assert.InRange(oldFar, 70, 100);
    }

    [Fact]
    public void Integer_sine_and_cosine_match_the_pitch_range()
    {
        Assert.Equal(1_000_000, CameraFraming.CosPpm(0));
        Assert.Equal(0, CameraFraming.SinPpm(0));
        // cos(0.42) = 0.913089, sin(-0.42) = -0.407760, cos(1.2) = 0.362358, sin(1.2) = 0.932039.
        Assert.InRange(CameraFraming.CosPpm(-420), 913_080, 913_100);
        Assert.InRange(CameraFraming.SinPpm(-420), -407_770, -407_750);
        Assert.InRange(CameraFraming.CosPpm(1200), 362_340, 362_380);
        Assert.InRange(CameraFraming.SinPpm(1200), 932_020, 932_060);
    }
}
