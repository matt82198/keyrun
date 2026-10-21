using System;

namespace Keyrun.Content;

/// <summary>The boom and pitch that keep an engaged target's top inside the frame (<see cref="CameraHeadroom.Adjust"/>).</summary>
/// <param name="BoomMm">Boom, mm: the input boom unless the target needs more follow distance, never past the max boom.</param>
/// <param name="PitchMrad">Pitch, mrad: the input pitch, turned up (flatter) in <see cref="CameraHeadroom.PitchStepMrad"/>
/// steps only when the max boom alone cannot frame the top.</param>
/// <param name="Fits">True when the target's top is inside the headroom at this boom and pitch.</param>
public readonly record struct HeadroomFit(int BoomMm, int PitchMrad, bool Fits);

/// <summary>
/// CAMERA-HEADROOM (KF1 demo receipts demo-012-t048s and demo-025-t050s: the Hollow Warden, a 4.7 m boss, at melee reach
/// filled the frame at the default 5 m boom and -0.3 rad pitch, its head cut off at the top edge). The follow camera
/// keeps the top of its current target inside the frame: the boom is pulled back, and only when the largest boom cannot
/// frame it is the pitch turned up, each by the least that fits. Smaller or farther targets return the boom to the follow
/// distance (the caller eases it). No Godot types and integers only (the Keyrun.Content purity rule); unit-tested in
/// Keyrun.Content.Tests/CameraHeadroomTests.cs, applied by game/src/View/CameraRig.cs.
/// <para>Geometry: the camera sits at the pivot (<c>pivotHeightMm</c> above the target's feet) minus the boom along the view
/// direction, pitched by <c>pitchMrad</c>, as in <see cref="CameraFraming"/>. A target top at height <c>y</c> above the
/// pivot and <c>d</c> ahead of it along the view has camera-frame height <c>y cos p - d sin p</c> and depth
/// <c>y sin p + d cos p + B</c>; it is inside when that tangent is at most <c>tan(fov/2) * headroom</c>.</para>
/// Units: mm, milliradians, degrees, permille.
/// </summary>
public static class CameraHeadroom
{
    /// <summary>Pitch search step when the boom alone cannot frame the top, milliradians.</summary>
    public const int PitchStepMrad = 25;

    private const long Million = 1_000_000;

    /// <summary>
    /// The boom and pitch that put the top of an engaged target inside the frame with the given headroom.
    /// </summary>
    /// <param name="boomMm">The follow boom before this rule (the eased zoom), mm, above zero.</param>
    /// <param name="pitchMrad">The pitch before this rule, mrad (negative looks down).</param>
    /// <param name="fovDeg">Vertical field of view, degrees.</param>
    /// <param name="pivotHeightMm">The pivot above the target's feet, mm.</param>
    /// <param name="targetTopMm">The target's top above its feet, mm (the larger of its target and scaled model heights).</param>
    /// <param name="targetDistanceMm">The target's centre ahead of the pivot along the view's heading, mm (negative behind).</param>
    /// <param name="headroomPermille">The usable half-frame above the axis, permille (the HUD's top band, 850 = 15 percent margin).</param>
    /// <param name="maxBoomMm">The longest boom the rule may ask for, mm (the wheel zoom ceiling).</param>
    /// <param name="pitchCeilingMrad">The flattest pitch the rule may ask for, mrad (the rig's pitch clamp).</param>
    /// <returns>The boom and pitch to ease toward, and whether the target's top fits at them.</returns>
    /// <exception cref="ArgumentException">A non-positive boom or headroom, a headroom above 1000, or a field of view out of range.</exception>
    public static HeadroomFit Adjust(
        int boomMm,
        int pitchMrad,
        int fovDeg,
        int pivotHeightMm,
        int targetTopMm,
        int targetDistanceMm,
        int headroomPermille,
        int maxBoomMm,
        int pitchCeilingMrad)
    {
        if (boomMm <= 0 || maxBoomMm <= 0)
        {
            throw new ArgumentException("the boom bounds must be positive");
        }
        if (headroomPermille <= 0 || headroomPermille > 1000)
        {
            throw new ArgumentException("the headroom must be between 1 and 1000 permille");
        }
        var limitPpm = CameraFraming.TanHalfFovPpm(fovDeg) * headroomPermille / 1000;
        for (var pitch = pitchMrad; ; pitch += PitchStepMrad)
        {
            var need = BoomNeededMm(pitch, pivotHeightMm, targetTopMm, targetDistanceMm, limitPpm);
            if (need <= maxBoomMm)
            {
                return new HeadroomFit(Math.Max(boomMm, need), pitch, true);
            }
            if (pitch + PitchStepMrad > pitchCeilingMrad)
            {
                break;
            }
        }
        return new HeadroomFit(Math.Max(boomMm, maxBoomMm), Math.Max(pitchMrad, pitchCeilingMrad), false);
    }

    // The smallest boom (mm, zero when none is needed) at which the top's tangent is at most limitPpm / 1e6. With the top
    // above the axis (n > 0) the condition is B * 1e6 + k >= n * 1e6 / limit, where n = y cos p - d sin p and
    // k = d cos p + y sin p (both mm x ppm). A top at or below the axis never breaks the headroom.
    private static int BoomNeededMm(int pitchMrad, int pivotHeightMm, int targetTopMm, int targetDistanceMm, long limitPpm)
    {
        long cos = CameraFraming.CosPpm(pitchMrad);
        long sin = CameraFraming.SinPpm(pitchMrad);
        long y = (long)targetTopMm - pivotHeightMm;
        long d = targetDistanceMm;
        var n = y * cos - d * sin;
        if (n <= 0)
        {
            return 0;
        }
        var k = d * cos + y * sin;
        var needed = n * Million / limitPpm;
        return (int)Math.Clamp(CeilDiv(needed - k, Million), 0L, int.MaxValue);
    }

    // Ceiling of a / b for b > 0 (for a <= 0, truncation toward zero already is the ceiling).
    private static long CeilDiv(long a, long b) => a > 0 ? (a + b - 1) / b : a / b;
}
