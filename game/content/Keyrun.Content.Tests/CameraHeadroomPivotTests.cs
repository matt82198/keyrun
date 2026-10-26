using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// CAMERA-HEADROOM pivot rule (R-BOSSROOM-1 (c)): an engaged hostile that stands at the camera pivot, or within a few
/// hundred mm of it, is the headroom body regardless of the view cone, so the camera stops clipping into a boss that is
/// standing on top of the Warden. A body behind the camera is still rejected. Pivot is the origin; the camera is at
/// (-1500, 0) looking down +Z, so the pivot and the 300 mm body both sit outside the 46-degree cone.
/// </summary>
public sealed class CameraHeadroomPivotTests
{
    private const int BossTopMm = 4687;
    private const int Reach = 3000;
    private const int CameraX = -1500;
    private const int CameraZ = 0;
    private const int ForwardX = 0;
    private const int ForwardZ = 1000;

    [Fact]
    public void Engaged_hostile_at_or_near_the_pivot_is_selected_regardless_of_cone_and_one_behind_the_camera_is_not()
    {
        var nearPivot = new[] { new HeadroomBody(0, 300, BossTopMm, Hostile: true, Engaged: true) };
        var atPivot = new[] { new HeadroomBody(0, 0, BossTopMm, Hostile: true, Engaged: true) };
        var behindCamera = new[] { new HeadroomBody(CameraX, -2500, BossTopMm, Hostile: true, Engaged: true) };

        Assert.Equal(0, CameraHeadroomBody.Select(nearPivot, CameraX, CameraZ, ForwardX, ForwardZ, 0, 0, Reach));
        Assert.Equal(0, CameraHeadroomBody.Select(atPivot, CameraX, CameraZ, ForwardX, ForwardZ, 0, 0, Reach));
        Assert.Null(CameraHeadroomBody.Select(behindCamera, CameraX, CameraZ, ForwardX, ForwardZ, 0, 0, Reach));
    }
}
