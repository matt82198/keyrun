using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// MOUSEOVER (Matt 2026-10-20, "like wow you should be able to hover and interrupt"; BIBLE s3, cast bars are the
/// enemy's language): the WoW input model in <see cref="MouseoverRule"/>. The cursor is free unless the right mouse
/// button is held (mouselook); the mouseover unit is the live enemy whose body box is under the cursor (nearest the
/// camera first, lowest id on a tie) and there is none during mouselook; the class kick goes at the mouseover unit when
/// one exists, else the current target; during mouselook the Warden faces the camera heading.
/// </summary>
public sealed class MouseoverRuleTests
{
    private static HoverBody Enemy(int id, PixelRect rect, long depth) => new(id, rect, depth, Hostile: true, Alive: true);

    [Fact]
    public void Cursor_is_free_unless_right_mouse_is_held()
    {
        Assert.False(MouseoverRule.CursorCaptured(rightHeld: false));
        Assert.True(MouseoverRule.CursorCaptured(rightHeld: true));
    }

    [Fact]
    public void Body_rect_spans_head_to_feet_centred_on_the_feet()
    {
        var r = MouseoverRule.BodyRect(new PixelPoint(400, 500), new PixelPoint(402, 380));
        Assert.Equal(380, r.Y);
        Assert.Equal(500, r.Bottom);
        Assert.Equal(400 - (r.Width / 2), r.X);
        Assert.True(r.Width >= 2 * MouseoverRule.MinHalfWidthPx);
        Assert.Equal(2 * (120 / 4), r.Width);
    }

    [Fact]
    public void Body_rect_has_a_minimum_width_when_the_body_is_far_away()
    {
        var r = MouseoverRule.BodyRect(new PixelPoint(100, 100), new PixelPoint(100, 92));
        Assert.Equal(2 * MouseoverRule.MinHalfWidthPx, r.Width);
        Assert.True(r.Height >= MouseoverRule.MinHeightPx);
    }

    [Fact]
    public void Hovering_an_enemy_body_makes_it_the_mouseover_unit()
    {
        var bodies = new List<HoverBody> { Enemy(101, new PixelRect(100, 100, 40, 80), 10), Enemy(102, new PixelRect(300, 100, 40, 80), 10) };
        Assert.Equal(102, MouseoverRule.Unit(new PixelPoint(320, 150), rightHeld: false, bodies));
        Assert.Equal(101, MouseoverRule.Unit(new PixelPoint(100, 100), rightHeld: false, bodies));
        Assert.Null(MouseoverRule.Unit(new PixelPoint(140, 150), rightHeld: false, bodies));
        Assert.Null(MouseoverRule.Unit(new PixelPoint(200, 150), rightHeld: false, bodies));
    }

    [Fact]
    public void No_mouseover_while_right_mouse_is_held()
    {
        var bodies = new List<HoverBody> { Enemy(101, new PixelRect(100, 100, 40, 80), 10) };
        Assert.Null(MouseoverRule.Unit(new PixelPoint(110, 150), rightHeld: true, bodies));
    }

    [Fact]
    public void Overlapping_bodies_pick_the_nearest_then_the_lowest_id()
    {
        var box = new PixelRect(100, 100, 40, 80);
        var bodies = new List<HoverBody> { Enemy(201, box, 50), Enemy(202, box, 20), Enemy(203, box, 20) };
        Assert.Equal(202, MouseoverRule.Unit(new PixelPoint(110, 150), rightHeld: false, bodies));
    }

    [Fact]
    public void Dead_and_friendly_bodies_are_never_the_mouseover_unit()
    {
        var box = new PixelRect(100, 100, 40, 80);
        var bodies = new List<HoverBody>
        {
            new(2, box, 1, Hostile: false, Alive: true),
            new(301, box, 2, Hostile: true, Alive: false),
            Enemy(302, box, 9),
        };
        Assert.Equal(302, MouseoverRule.Unit(new PixelPoint(110, 150), rightHeld: false, bodies));
        Assert.Null(MouseoverRule.Unit(new PixelPoint(110, 150), rightHeld: false, bodies.GetRange(0, 2)));
    }

    [Fact]
    public void Kick_goes_to_the_mouseover_unit_when_one_exists()
    {
        Assert.Equal(102, MouseoverRule.CastTarget(interrupt: true, mouseover: 102, fallback: 101));
    }

    [Fact]
    public void Kick_goes_to_the_target_without_a_mouseover()
    {
        Assert.Equal(101, MouseoverRule.CastTarget(interrupt: true, mouseover: null, fallback: 101));
    }

    [Fact]
    public void Non_kick_casts_keep_their_target_with_a_mouseover()
    {
        Assert.Equal(101, MouseoverRule.CastTarget(interrupt: false, mouseover: 102, fallback: 101));
    }

    [Fact]
    public void Walking_without_mouselook_faces_the_walk_direction()
    {
        Assert.Equal(300, MouseoverRule.FacingYaw(rightHeld: false, moving: true, walkYaw: 300, cameraYaw: 768, actorYaw: 0));
        Assert.Null(MouseoverRule.FacingYaw(rightHeld: false, moving: false, walkYaw: 300, cameraYaw: 768, actorYaw: 0));
    }

    [Fact]
    public void Mouselook_faces_the_camera_heading_walking_or_standing()
    {
        Assert.Equal(768, MouseoverRule.FacingYaw(rightHeld: true, moving: true, walkYaw: 300, cameraYaw: 768, actorYaw: 0));
        Assert.Equal(768, MouseoverRule.FacingYaw(rightHeld: true, moving: false, walkYaw: 300, cameraYaw: 768, actorYaw: 0));
        Assert.Null(MouseoverRule.FacingYaw(rightHeld: true, moving: false, walkYaw: 300, cameraYaw: 768, actorYaw: 768));
    }
}
