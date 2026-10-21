using System;
using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// f-84d213a5 (design/BIBLE.md section 3, readability): a party bot standing between the chase camera and the player or
/// the player's target fades to <see cref="PartyFade.FadedPermille"/> while it occludes, as WoW fades a model in front of
/// the camera; the player's own model and enemies never fade. The camera sits at the origin looking down -z in these
/// cases; the player is 5 m ahead of it and the boss 20 m ahead.
/// </summary>
public sealed class PartyFadeTests
{
    private static readonly (int X, int Y, int Z) Camera = (0, 0, 0);
    private static readonly (int X, int Y, int Z) Player = (0, 0, -5000);
    private static readonly (int X, int Y, int Z) Boss = (0, 0, -20000);

    private static IReadOnlyList<(int X, int Y, int Z)> PlayerOnly => new[] { Player };

    private static IReadOnlyList<(int X, int Y, int Z)> PlayerAndBoss => new[] { Player, Boss };

    private static FadeBody Ally(int x, int y, int z) => new(FadeRole.Ally, (x, y, z), PartyFade.BodyRadiusMm);

    [Fact]
    public void Ally_on_the_camera_to_player_line_fades()
    {
        var alpha = PartyFade.AlphaPermille(Camera, PlayerOnly, Ally(0, 0, -3000));

        Assert.Equal(PartyFade.FadedPermille, alpha);
    }

    [Fact]
    public void Ally_on_the_camera_to_target_line_fades()
    {
        // Past the player (so off the player line) but well inside the near distance toward the boss.
        var alpha = PartyFade.AlphaPermille(Camera, PlayerAndBoss, Ally(0, 0, -8000));

        Assert.Equal(PartyFade.FadedPermille, alpha);
    }

    [Fact]
    public void Ally_grazing_the_line_within_its_radius_fades()
    {
        var alpha = PartyFade.AlphaPermille(Camera, PlayerOnly, Ally(PartyFade.BodyRadiusMm - 100, 0, -3000));

        Assert.Equal(PartyFade.FadedPermille, alpha);
    }

    [Fact]
    public void Ally_beside_the_line_stays_opaque()
    {
        var alpha = PartyFade.AlphaPermille(Camera, PlayerAndBoss, Ally(3000, 0, -3000));

        Assert.Equal(PartyFade.OpaquePermille, alpha);
    }

    [Fact]
    public void Ally_just_outside_its_radius_of_the_line_stays_opaque()
    {
        var alpha = PartyFade.AlphaPermille(Camera, PlayerOnly, Ally(PartyFade.BodyRadiusMm + 100, 0, -3000));

        Assert.Equal(PartyFade.OpaquePermille, alpha);
    }

    [Fact]
    public void Ally_behind_the_focus_stays_opaque()
    {
        // On the extended camera-to-player line, 2 m past the player: the player is drawn in front of it.
        var alpha = PartyFade.AlphaPermille(Camera, PlayerOnly, Ally(0, 0, -7000));

        Assert.Equal(PartyFade.OpaquePermille, alpha);
    }

    [Fact]
    public void Ally_on_the_target_line_beyond_the_near_distance_stays_opaque()
    {
        // On the camera-to-boss line but 15 m out (past NearMm): a melee bot at the boss is drawn small, never faded.
        Assert.True(15000 > PartyFade.NearMm);
        var alpha = PartyFade.AlphaPermille(Camera, PlayerAndBoss, Ally(0, 0, -15000));

        Assert.Equal(PartyFade.OpaquePermille, alpha);
    }

    [Fact]
    public void Ally_behind_the_camera_stays_opaque()
    {
        var alpha = PartyFade.AlphaPermille(Camera, PlayerAndBoss, Ally(0, 0, 4000));

        Assert.Equal(PartyFade.OpaquePermille, alpha);
    }

    [Fact]
    public void Ally_pressed_against_the_lens_fades_even_off_the_lines()
    {
        // Off every sight line, but its body nearly touches the camera: it fills a corner of the frame (the D15 frames).
        var alpha = PartyFade.AlphaPermille(Camera, PlayerAndBoss, Ally(1500, 0, -500));

        Assert.Equal(PartyFade.FadedPermille, alpha);
    }

    [Fact]
    public void The_players_own_model_never_fades()
    {
        var self = new FadeBody(FadeRole.Self, (0, 0, -3000), PartyFade.BodyRadiusMm);

        Assert.Equal(PartyFade.OpaquePermille, PartyFade.AlphaPermille(Camera, PlayerAndBoss, self));
        Assert.Equal(PartyFade.OpaquePermille, PartyFade.AlphaPermille(Camera, PlayerAndBoss, self with { CentreMm = (0, 0, -200) }));
    }

    [Fact]
    public void Enemies_never_fade()
    {
        var enemy = new FadeBody(FadeRole.Enemy, (0, 0, -3000), PartyFade.BodyRadiusMm);

        Assert.Equal(PartyFade.OpaquePermille, PartyFade.AlphaPermille(Camera, PlayerAndBoss, enemy));
        Assert.Equal(PartyFade.OpaquePermille, PartyFade.AlphaPermille(Camera, PlayerAndBoss, enemy with { CentreMm = (0, 0, -200) }));
    }

    [Fact]
    public void A_focus_at_the_camera_is_ignored()
    {
        var alpha = PartyFade.AlphaPermille(Camera, new[] { Camera }, Ally(0, 0, -3000));

        Assert.Equal(PartyFade.OpaquePermille, alpha);
    }

    [Fact]
    public void A_tilted_line_far_from_the_origin_is_measured_without_overflow()
    {
        // A camera up and behind the player in the far corner of the plan, looking down at the boss 40 m away.
        var camera = (60000, 4500, 60000);
        var boss = (60000 - 28000, 1000, 60000 - 28000);
        var onLine = (60000 - 7000, 4500 - 875, 60000 - 7000); // a quarter of the way along
        var beside = (onLine.Item1 + 2000, onLine.Item2, onLine.Item3 - 2000);

        Assert.Equal(PartyFade.FadedPermille, PartyFade.AlphaPermille(camera, new[] { boss }, Ally(onLine.Item1, onLine.Item2, onLine.Item3)));
        Assert.Equal(PartyFade.OpaquePermille, PartyFade.AlphaPermille(camera, new[] { boss }, Ally(beside.Item1, beside.Item2, beside.Item3)));
    }

    [Fact]
    public void Null_foci_throw()
    {
        Assert.Throws<ArgumentNullException>(() => PartyFade.AlphaPermille(Camera, null!, Ally(0, 0, -3000)));
    }
}
