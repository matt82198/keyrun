using System;

namespace Keyrun.Content;

/// <summary>
/// The default follow framing (CAMERA-FRAMING; Matt 2026-10-20 "it should feel almost exact to wow with less button
/// clutter", demo verdict KF1: the camera drew the fight about 60 px tall in a 720 px frame, so a swing, a cast and a
/// telegraph did not read). Like WoW's default camera: third person over the shoulder, following the player, the
/// player's body <see cref="PlayerMinPermille"/>..<see cref="PlayerMaxPermille"/> of the frame tall, mouse-wheel zoom
/// clamped to <see cref="MinZoomMm"/>..<see cref="MaxZoomMm"/>. The boom no longer grows to fit the whole party (the
/// PARTY-FRAMING fit pushed it to 12 m); the party frames carry where the bots are, as in WoW.
/// No Godot types and integers only (the Keyrun.Content purity rule); unit-tested in
/// Keyrun.Content.Tests/CameraFramingTests.cs, applied by game/src/View/CameraRig.cs. Units: mm, milliradians, degrees.
/// </summary>
public static class CameraFraming
{
    /// <summary>Default boom from the shoulder pivot to the camera, mm (the player about 28 percent of the frame).</summary>
    public const int DefaultBoomMm = 5000;

    /// <summary>Nearest wheel zoom, mm.</summary>
    public const int MinZoomMm = 2500;

    /// <summary>Farthest wheel zoom, mm (the old party-fit ceiling; walls may still shorten it).</summary>
    public const int MaxZoomMm = 12000;

    /// <summary>Boom change per mouse-wheel notch, mm.</summary>
    public const int ZoomStepMm = 500;

    /// <summary>Default pitch, milliradians (negative looks down): about 17 degrees, WoW's slight look-down.</summary>
    public const int DefaultPitchMrad = -300;

    /// <summary>
    /// Vertical field of view, degrees: WoW's default 90 degree horizontal view at 16:9 is 58.7 degrees vertical
    /// (Godot's Camera3D fov is vertical with keep_aspect height).
    /// </summary>
    public const int VerticalFovDeg = 59;

    /// <summary>Pivot (the point the camera looks at) above the player's feet, mm: shoulder height.</summary>
    public const int PivotHeightMm = 1400;

    /// <summary>The framed player's drawn height, mm (the Warden's <see cref="ActorBindingRow.TargetHeightMm"/>).</summary>
    public const int PlayerHeightMm = 1750;

    /// <summary>Shortest the player may be drawn at the default framing, permille of the frame height.</summary>
    public const int PlayerMinPermille = 200;

    /// <summary>Tallest the player may be drawn at the default framing, permille of the frame height.</summary>
    public const int PlayerMaxPermille = 350;

    private const long Million = 1_000_000;

    /// <summary>The boom after <paramref name="notches"/> wheel notches (negative zooms in), clamped to the zoom range.</summary>
    /// <param name="boomMm">The boom before the notches, mm.</param>
    /// <param name="notches">Wheel notches: -1 per wheel-up (in), +1 per wheel-down (out).</param>
    /// <returns>The new boom, mm, within <see cref="MinZoomMm"/>..<see cref="MaxZoomMm"/>.</returns>
    public static int Zoom(int boomMm, int notches) =>
        (int)Math.Clamp((long)boomMm + (long)notches * ZoomStepMm, MinZoomMm, MaxZoomMm);

    /// <summary>
    /// How tall a standing body is drawn, permille of the frame height, when the camera sits <paramref name="boomMm"/>
    /// behind a pivot <paramref name="pivotHeightMm"/> above its feet, pitched <paramref name="pitchMrad"/>: the
    /// projected span from the feet to <paramref name="bodyHeightMm"/> (a pinhole camera, the body at the frame's
    /// centre line).
    /// </summary>
    /// <param name="boomMm">Boom, mm.</param>
    /// <param name="pitchMrad">Pitch, mrad (negative looks down).</param>
    /// <param name="fovDeg">Vertical field of view, degrees.</param>
    /// <param name="pivotHeightMm">Pivot above the feet, mm.</param>
    /// <param name="bodyHeightMm">Body height, mm.</param>
    /// <returns>The drawn height, permille of the frame height (rounded).</returns>
    public static int BodyScreenPermille(int boomMm, int pitchMrad, int fovDeg, int pivotHeightMm, int bodyHeightMm)
    {
        var tanV = TanHalfFovPpm(fovDeg);
        var head = AxisTanPpm(boomMm, pitchMrad, bodyHeightMm - pivotHeightMm);
        var feet = AxisTanPpm(boomMm, pitchMrad, -pivotHeightMm);
        return (int)RoundDiv((head - feet) * 1000, 2 * tanV);
    }

    /// <summary>
    /// How far below the view axis a point <paramref name="heightAbovePivotMm"/> above the pivot (negative is below)
    /// is drawn, permille of the half-frame (positive below the centre line, 1000 at the bottom edge).
    /// </summary>
    /// <param name="boomMm">Boom, mm.</param>
    /// <param name="pitchMrad">Pitch, mrad.</param>
    /// <param name="fovDeg">Vertical field of view, degrees.</param>
    /// <param name="heightAbovePivotMm">The point's height above the pivot, mm.</param>
    /// <returns>Permille of the half-frame below the axis (rounded).</returns>
    public static int BelowAxisPermille(int boomMm, int pitchMrad, int fovDeg, int heightAbovePivotMm) =>
        (int)RoundDiv(-AxisTanPpm(boomMm, pitchMrad, heightAbovePivotMm) * 1000, TanHalfFovPpm(fovDeg));

    /// <summary>tan of half the vertical field of view, ppm.</summary>
    /// <param name="fovDeg">Vertical field of view, degrees, 1..179.</param>
    /// <returns>tan(fov / 2), ppm.</returns>
    public static long TanHalfFovPpm(int fovDeg)
    {
        if (fovDeg <= 0 || fovDeg >= 180)
        {
            throw new ArgumentException("the field of view must be between 0 and 180 degrees");
        }
        var halfUrad = fovDeg * 3_141_593L / 360;
        return SinUrad(halfUrad) * Million / CosUrad(halfUrad);
    }

    /// <summary>sin of an angle, ppm (Taylor series, exact to a few ppm for |angle| up to 1.6 rad).</summary>
    /// <param name="mrad">Angle, mrad.</param>
    /// <returns>sin, ppm.</returns>
    public static int SinPpm(int mrad) => (int)SinUrad(mrad * 1000L);

    /// <summary>cos of an angle, ppm (Taylor series, exact to a few ppm for |angle| up to 1.6 rad).</summary>
    /// <param name="mrad">Angle, mrad.</param>
    /// <returns>cos, ppm.</returns>
    public static int CosPpm(int mrad) => (int)CosUrad(mrad * 1000L);

    // tan of the angle below (negative) or above the view axis at which a point y mm above the pivot is drawn, ppm.
    // The camera sits at pivot - B f with f = (0, sin p, -cos p) and up u = (0, cos p, sin p); for the point v = (0, y, 0)
    // the depth is v.f + B = y sin p + B and the height v.u = y cos p.
    private static long AxisTanPpm(int boomMm, int pitchMrad, int yMm)
    {
        var sin = SinUrad(pitchMrad * 1000L);
        var cos = CosUrad(pitchMrad * 1000L);
        var depth = (long)yMm * sin + (long)boomMm * Million;
        if (depth <= 0)
        {
            throw new ArgumentException("the point is behind the camera");
        }
        return (long)yMm * cos * Million / depth;
    }

    private static long SinUrad(long x)
    {
        long sum = x, term = x;
        for (var n = 2; n <= 16; n += 2)
        {
            term = -term * x / Million * x / Million / (n * (n + 1));
            sum += term;
        }
        return sum;
    }

    private static long CosUrad(long x)
    {
        long sum = Million, term = Million;
        for (var n = 1; n <= 15; n += 2)
        {
            term = -term * x / Million * x / Million / (n * (n + 1));
            sum += term;
        }
        return sum;
    }

    private static long RoundDiv(long a, long b) => a >= 0 ? (a + b / 2) / b : -((-a + b / 2) / b);
}
