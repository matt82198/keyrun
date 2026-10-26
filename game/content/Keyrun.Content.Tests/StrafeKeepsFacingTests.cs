using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// R-STRAFE-1 (Matt 2026-10-25, G2, f-d0a22d13): movement never turns the character. With right mouse NOT held, a pure
/// strafe (A or D, no W) and a backpedal (S only) leave the Warden's facing as it was; only mouselook (right mouse held)
/// turns him, to the camera heading. Red until <see cref="MouseoverRule.FacingYaw"/> stops taking facing from the walk
/// direction when the mouse is free.
/// </summary>
public sealed class StrafeKeepsFacingTests
{
    private const int CameraYaw = 768;
    private const int ActorYaw = 0;

    [Fact]
    public void Pure_strafe_without_right_mouse_does_not_change_facing()
    {
        // A strafe: the walk direction is 90 degrees off the camera heading. The Warden must keep ActorYaw.
        const int strafeWalkYaw = 256;
        var face = MouseoverRule.FacingYaw(rightHeld: false, moving: true, walkYaw: strafeWalkYaw, cameraYaw: CameraYaw, actorYaw: ActorYaw);
        Assert.True(face is null || face == ActorYaw,
            $"strafe with right mouse free must not change facing: got {face?.ToString() ?? "null"}, actorYaw {ActorYaw}");
    }

    [Fact]
    public void Backpedal_without_right_mouse_does_not_change_facing()
    {
        // S only: the walk direction points back along the camera heading. The Warden must keep ActorYaw.
        const int backpedalWalkYaw = CameraYaw + 512;
        var face = MouseoverRule.FacingYaw(rightHeld: false, moving: true, walkYaw: backpedalWalkYaw, cameraYaw: CameraYaw, actorYaw: ActorYaw);
        Assert.True(face is null || face == ActorYaw,
            $"backpedal with right mouse free must not change facing: got {face?.ToString() ?? "null"}, actorYaw {ActorYaw}");
    }

    [Fact]
    public void Strafe_with_right_mouse_held_faces_the_camera_heading()
    {
        // Control row (existing behaviour, must stay green): mouselook turns the Warden to the camera heading.
        const int strafeWalkYaw = 256;
        Assert.Equal(CameraYaw, MouseoverRule.FacingYaw(rightHeld: true, moving: true, walkYaw: strafeWalkYaw, cameraYaw: CameraYaw, actorYaw: ActorYaw));
    }
}
