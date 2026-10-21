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

    // CAMERA-FG occlusion (ruling): the lens at the origin, the pivot 10 m down -z (the Warden's shoulder at the boss).
    private static readonly (int X, int Y, int Z) Lens = (0, 0, 0);
    private static readonly (int X, int Y, int Z) Pivot = (0, 0, -10000);

    [Fact]
    public void A_body_on_the_sight_line_between_the_lens_and_the_pivot_occludes()
    {
        Assert.True(CameraNearFade.Occludes(FadeRole.Ally, Lens, Pivot, (0, 0, -4000)));
    }

    [Fact]
    public void A_body_within_the_occlusion_radius_of_the_line_occludes()
    {
        Assert.True(CameraNearFade.Occludes(FadeRole.Ally, Lens, Pivot, (CameraNearFade.OcclusionRadiusMm - 100, 0, -4000)));
    }

    [Fact]
    public void A_body_just_outside_the_occlusion_radius_does_not_occlude()
    {
        Assert.False(CameraNearFade.Occludes(FadeRole.Ally, Lens, Pivot, (CameraNearFade.OcclusionRadiusMm + 100, 0, -4000)));
    }

    [Fact]
    public void A_body_beyond_the_pivot_or_behind_the_lens_does_not_occlude()
    {
        Assert.False(CameraNearFade.Occludes(FadeRole.Ally, Lens, Pivot, (0, 0, -11000)));
        Assert.False(CameraNearFade.Occludes(FadeRole.Ally, Lens, Pivot, (0, 0, 2000)));
    }

    [Fact]
    public void The_players_own_model_never_occludes()
    {
        Assert.False(CameraNearFade.Occludes(FadeRole.Self, Lens, Pivot, (0, 0, -4000)));
    }

    [Fact]
    public void An_occluding_body_takes_the_faded_alpha_at_the_boom_distance()
    {
        // A bot on the line 8 m out (past the 5 m near rule) is drawn at the party fade, not opaque.
        Assert.Equal(PartyFade.FadedPermille, CameraNearFade.AlphaPermille(8000, occludes: true));
    }

    [Fact]
    public void A_body_beside_the_line_at_the_boom_distance_stays_opaque()
    {
        Assert.False(CameraNearFade.Occludes(FadeRole.Ally, Lens, Pivot, (3000, 0, -8000)));
        Assert.Equal(Opaque, CameraNearFade.AlphaPermille(8000, occludes: false));
    }

    [Fact]
    public void An_occluding_body_near_the_lens_keeps_the_lower_of_the_two_rules()
    {
        // The distance rule gives 240 at 1 m (50 + 950 * 1000 / 5000); the occlusion rule's 250 is higher, so 240 wins.
        Assert.Equal(240, CameraNearFade.AlphaPermille(1000, occludes: true));
    }
}
