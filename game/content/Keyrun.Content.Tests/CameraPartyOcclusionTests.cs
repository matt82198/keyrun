using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// R-CAMERA-OCC-1 (ruling seq 273): the camera pulls back or raises pitch so a living party body standing between the
/// camera and the pivot never draws taller than <see cref="CameraNearFade.ScreenHeightLimitPermille"/> (the Ally limit).
/// S1 spec: these tests are red on the assertion until the S2 implementation fills the <see cref="CameraFraming"/> seam.
/// </summary>
public sealed class CameraPartyOcclusionTests
{
    private const int DefaultBoom = 5000;
    private const int DefaultPitch = -300;
    private const int Fov = 59;
    private const int BodyDistanceFromPivot = 1500;
    private static readonly int BodyHeight = CameraFraming.PlayerHeightMm;

    [Fact]
    public void Framing_a_party_body_on_the_segment_keeps_it_at_or_under_the_ally_screen_height_limit()
    {
        var (boom, pitch) = CameraFraming.FrameForBody(
            DefaultBoom, DefaultPitch, Fov, BodyDistanceFromPivot, BodyHeight, bodyOnSegment: true);

        var drawn = CameraFraming.BodyScreenPermilleAt(boom, pitch, Fov, BodyDistanceFromPivot, BodyHeight);
        Assert.True(
            drawn <= CameraNearFade.ScreenHeightLimitPermille,
            $"a party body on the segment must draw at or under {CameraNearFade.ScreenHeightLimitPermille} permille, was {drawn} (boom {boom} mm, pitch {pitch} mrad)");
    }

    [Fact]
    public void A_body_off_the_segment_leaves_the_default_boom_and_pitch_unchanged()
    {
        var (boom, pitch) = CameraFraming.FrameForBody(
            DefaultBoom, DefaultPitch, Fov, BodyDistanceFromPivot, BodyHeight, bodyOnSegment: false);

        Assert.Equal(DefaultBoom, boom);
        Assert.Equal(DefaultPitch, pitch);
    }

    [Fact]
    public void At_the_default_rig_a_party_body_on_the_segment_exceeds_the_ally_limit()
    {
        // Red premise: the lane hand calculation put this near 389 permille, over the 333 limit.
        var drawn = CameraFraming.BodyScreenPermilleAt(DefaultBoom, DefaultPitch, Fov, BodyDistanceFromPivot, BodyHeight);
        Assert.True(
            drawn > CameraNearFade.ScreenHeightLimitPermille,
            $"the default rig must draw a body on the segment over {CameraNearFade.ScreenHeightLimitPermille} permille, was {drawn}");
    }
}
