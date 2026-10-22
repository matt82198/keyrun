using System;
using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// CAMERA-VERTICAL (Matt 2026-10-21: "run the playthroughs vertical camera"; capture kf2-kick-1f8fed7f frame 007: the
/// low chase camera let the party hide the telegraph circle and left the back of the room unreadable). The run argument
/// <c>--camera chase|vertical</c> (default chase, unchanged) and the in-play V toggle pick the rig's preset;
/// <see cref="CameraMode"/> holds the rule, game/src/View/CameraRig.cs applies it, and SimDriver logs
/// <c>CAMERA mode=M pitch=P dist=D</c> at run start.
/// </summary>
public sealed class CameraModeTests
{
    [Fact]
    public void Camera_vertical_argument_sets_the_rig_pitch_and_distance()
    {
        var mode = CameraMode.FromArgs(new[] { "--autorun", "--ticks", "7000", "--camera", "vertical", "--key", "2" });
        Assert.Equal(CameraModeKind.Vertical, mode);
        var view = CameraMode.For(mode);
        Assert.Equal(-1200, view.PitchMrad);
        Assert.Equal(16500, view.BoomMm);
        Assert.Equal("CAMERA mode=vertical pitch=-1200 dist=16500", CameraMode.ProofLine(mode, view.PitchMrad, view.BoomMm));
    }

    [Fact]
    public void Vertical_pitch_is_65_to_75_degrees_down()
    {
        // 1134 mrad is 65 degrees, 1309 mrad is 75 degrees.
        Assert.InRange(-CameraMode.For(CameraModeKind.Vertical).PitchMrad, 1134, 1309);
    }

    [Fact]
    public void No_camera_argument_keeps_the_chase_camera()
    {
        var mode = CameraMode.FromArgs(new[] { "--autorun", "--ticks", "7000" });
        Assert.Equal(CameraModeKind.Chase, mode);
        var view = CameraMode.For(mode);
        Assert.Equal(CameraFraming.DefaultPitchMrad, view.PitchMrad);
        Assert.Equal(CameraFraming.DefaultBoomMm, view.BoomMm);
        Assert.Equal("CAMERA mode=chase pitch=-300 dist=5000", CameraMode.ProofLine(mode, view.PitchMrad, view.BoomMm));
        Assert.Equal(CameraModeKind.Chase, CameraMode.Parse("chase"));
    }

    [Theory]
    [InlineData("Vertical")]
    [InlineData("top")]
    [InlineData("")]
    public void An_unknown_mode_stops_the_launch(string raw)
    {
        Assert.Throws<ArgumentException>(() => CameraMode.Parse(raw));
        Assert.Throws<ArgumentException>(() => CameraMode.FromArgs(new[] { "--camera", raw }));
    }

    [Fact]
    public void A_camera_flag_without_a_value_stops_the_launch()
    {
        Assert.Throws<ArgumentException>(() => CameraMode.FromArgs(new[] { "--autorun", "--camera" }));
    }

    [Fact]
    public void The_v_toggle_flips_between_the_two_modes()
    {
        Assert.Equal(CameraModeKind.Vertical, CameraMode.Toggle(CameraModeKind.Chase));
        Assert.Equal(CameraModeKind.Chase, CameraMode.Toggle(CameraModeKind.Vertical));
        Assert.Equal("V", CameraMode.ToggleKeyLabel);
    }

    [Fact]
    public void Vertical_skips_the_chase_only_rules_and_chase_keeps_them()
    {
        // Above the 1.6 m walls the full-height wall clamp and the boss headroom pull-back would only flatten the view.
        Assert.True(CameraMode.ChaseRules(CameraModeKind.Chase));
        Assert.False(CameraMode.ChaseRules(CameraModeKind.Vertical));
    }

    [Fact]
    public void Wheel_zoom_reaches_the_vertical_distance_only_in_vertical()
    {
        Assert.Equal(16500, CameraMode.Zoom(CameraModeKind.Vertical, 16500, 4));
        Assert.Equal(16000, CameraMode.Zoom(CameraModeKind.Vertical, 16500, -1));
        Assert.Equal(CameraFraming.MaxZoomMm, CameraMode.Zoom(CameraModeKind.Chase, CameraFraming.MaxZoomMm, 4));
        Assert.Equal(CameraFraming.MinZoomMm, CameraMode.Zoom(CameraModeKind.Vertical, 3000, -10));
    }

    [Fact]
    public void Vertical_frames_the_party_the_pull_and_its_telegraph_at_1280x720()
    {
        // The rig's frustum at 1280x720 (CameraRig.Tangents): vertical fov 59 degrees, the HUD top band 850 and the
        // ability bar band 550 of the half-frames, a 0.5 m pad. The Warden's feet at the origin, the pivot 1.4 m up,
        // looking along -z. The party within 2.5 m behind and beside, a pull 6 m ahead under a 4 m telegraph circle.
        var view = CameraMode.For(CameraModeKind.Vertical);
        var tanV = CameraFraming.TanHalfFovPpm(CameraFraming.VerticalFovDeg);
        var tanH = (int)(tanV * 1280 / 720 / 1000);
        var tanUp = (int)(tanV * 850 / 1_000_000);
        var tanDown = (int)(tanV * 550 / 1_000_000);
        var sin = CameraFraming.SinPpm(view.PitchMrad);
        var cos = CameraFraming.CosPpm(view.PitchMrad);
        var forward = (0, sin / 1000, -cos / 1000);
        var up = (0, cos / 1000, sin / 1000);
        var points = new List<(int X, int Y, int Z)>();
        foreach (var (x, z) in new[] { (0, 0), (-2500, 2000), (2500, 2000), (0, 2500) })
        {
            points.Add((x, 0, z));
            points.Add((x, 2000, z));
        }
        foreach (var (x, z) in new[] { (0, -10000), (-4000, -6000), (4000, -6000), (0, -2000) })
        {
            points.Add((x, 0, z));
        }
        points.Add((0, 4687, -6000)); // the boss's top at the pull's centre

        var fit = CameraBoom.FitParty(
            (0, CameraFraming.PivotHeightMm, 0), forward, up, points, tanH, tanUp, tanDown, 500, view.BoomMm, view.BoomMm, 0, 0);
        Assert.True(fit.Solved, $"point {fit.Worst} is out of the vertical frame");

        var chase = CameraMode.For(CameraModeKind.Chase);
        var chaseFit = CameraBoom.FitParty(
            (0, CameraFraming.PivotHeightMm, 0),
            (0, CameraFraming.SinPpm(chase.PitchMrad) / 1000, -CameraFraming.CosPpm(chase.PitchMrad) / 1000),
            (0, CameraFraming.CosPpm(chase.PitchMrad) / 1000, CameraFraming.SinPpm(chase.PitchMrad) / 1000),
            points, tanH, tanUp, tanDown, 500, chase.BoomMm, chase.BoomMm, 0, 0);
        Assert.False(chaseFit.Solved);
    }
}
