using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// MOUSELOOK HELD (spec, ruling seq 576): the camera-turn input is live right mouse OR a replay. A replay applies the
/// recorded camera headings, so the Warden faces the recorded mouselook heading even when the live right button is up.
/// A live right button that is not held keeps the strafe facing (walking never turns the Warden).
/// </summary>
public sealed class MouselookHeldTests
{
    [Fact]
    public void Live_right_held_is_mouselook()
    {
        Assert.True(MouseoverRule.MouselookHeld(rightHeld: true, replaying: false));
    }

    [Fact]
    public void Live_right_not_held_is_not_mouselook_so_strafe_keeps_facing()
    {
        Assert.False(MouseoverRule.MouselookHeld(rightHeld: false, replaying: false));
    }

    [Fact]
    public void Replaying_with_right_not_held_is_mouselook()
    {
        Assert.True(MouseoverRule.MouselookHeld(rightHeld: false, replaying: true));
    }

    [Fact]
    public void A_replay_with_right_not_held_faces_the_recorded_camera_heading_while_walking()
    {
        const int walkYaw = 0;
        const int cameraYaw = 512;
        const int actorYaw = 0;
        var held = MouseoverRule.MouselookHeld(rightHeld: false, replaying: true);
        Assert.Equal(cameraYaw, MouseoverRule.FacingYaw(held, moving: true, walkYaw, cameraYaw, actorYaw));
    }
}
