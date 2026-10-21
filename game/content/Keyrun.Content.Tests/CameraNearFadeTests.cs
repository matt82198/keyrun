using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// CAMERA-FG (receipt demo-5e34e26b, demo-012-t048s and demo-013-t052s): the two untextured foreground bodies between the
/// camera and the Hollow Warden are the party bots Chanter and Cinder (actors, not props). <see cref="CameraNearFade"/>
/// holds the WoW rule for an actor body near the lens; game/src/View/ActorView.cs applies it to the body's materials.
/// </summary>
public sealed class CameraNearFadeTests
{
    private const int Opaque = PartyFade.OpaquePermille;

    [Fact]
    public void A_body_at_the_lens_is_drawn_at_the_floor()
    {
        Assert.Equal(CameraNearFade.FloorPermille, CameraNearFade.AlphaPermille(0));
    }

    [Fact]
    public void A_body_at_the_fade_distance_or_farther_is_opaque()
    {
        Assert.Equal(Opaque, CameraNearFade.AlphaPermille(CameraNearFade.FadeDistanceMm));
        Assert.Equal(Opaque, CameraNearFade.AlphaPermille(CameraNearFade.FadeDistanceMm + 1500));
        Assert.Equal(Opaque, CameraNearFade.AlphaPermille(int.MaxValue));
    }

    [Fact]
    public void A_body_at_the_default_follow_boom_is_opaque()
    {
        // The Warden's own model sits about one follow boom behind the camera, so it is never faded at the zoom.
        Assert.Equal(Opaque, CameraNearFade.AlphaPermille(CameraFraming.DefaultBoomMm));
    }

    [Fact]
    public void The_alpha_is_linear_between_the_floor_and_the_fade_distance()
    {
        // 50 + (1000 - 50) * 2500 / 5000 = 525
        Assert.Equal(525, CameraNearFade.AlphaPermille(CameraNearFade.FadeDistanceMm / 2));
    }

    [Fact]
    public void A_body_behind_the_lens_is_drawn_at_the_floor()
    {
        Assert.Equal(CameraNearFade.FloorPermille, CameraNearFade.AlphaPermille(-500));
    }

    [Fact]
    public void The_alpha_never_falls_as_the_body_moves_away()
    {
        var previous = -1;
        for (var d = 0; d <= 6000; d += 250)
        {
            var alpha = CameraNearFade.AlphaPermille(d);
            Assert.True(alpha >= previous, $"alpha fell from {previous} to {alpha} at {d} mm");
            previous = alpha;
        }
    }
}
