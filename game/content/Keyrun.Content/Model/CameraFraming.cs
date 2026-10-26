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

    /// <summary>Steepest pitch FrameForBody will try, mrad (about 86 degrees look-down).</summary>
    private const int SteepestPitchMrad = -1500;

    /// <summary>Pitch change per search step in FrameForBody, mrad.</summary>
    private const int PitchStepMrad = 10;

    /// <summary>Boom change per search step in FrameForBody, mm.</summary>
    private const int BoomStepMm = 50;

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

    /// <summary>
    /// The permille a body standing on the ground <paramref name="bodyDistanceFromPivotMm"/> in front of the pivot
    /// (toward the camera, horizontally) is drawn at, <paramref name="bodyHeightMm"/> tall, feet at the ground under
    /// the pivot's height (R-CAMERA-OCC-1, ruling seq 273). The body's feet are <see cref="PivotHeightMm"/> below the pivot.
    /// </summary>
    /// <param name="boomMm">Boom, mm.</param>
    /// <param name="pitchMrad">Pitch, mrad.</param>
    /// <param name="fovDeg">Vertical field of view, degrees.</param>
    /// <param name="bodyDistanceFromPivotMm">The body's horizontal distance from the pivot toward the camera, mm.</param>
    /// <param name="bodyHeightMm">Body height, mm.</param>
    /// <returns>The drawn height, permille of the frame height (rounded).</returns>
    public static int BodyScreenPermilleAt(int boomMm, int pitchMrad, int fovDeg, int bodyDistanceFromPivotMm, int bodyHeightMm)
    {
        var tanV = TanHalfFovPpm(fovDeg);
        var head = TanToPointPpm(boomMm, pitchMrad, bodyHeightMm - PivotHeightMm, bodyDistanceFromPivotMm);
        var feet = TanToPointPpm(boomMm, pitchMrad, -PivotHeightMm, bodyDistanceFromPivotMm);
        return (int)RoundDiv((head - feet) * 1000, 2 * tanV);
    }

    /// <summary>
    /// The boom and pitch that keep a party body standing on the camera-to-pivot segment at or under the Ally
    /// screen-height limit (R-CAMERA-OCC-1). Pitch steepens first, in 10 mrad steps from the default down to -1500 mrad,
    /// and only when that is not enough does the boom lengthen, in 50 mm steps at the steepest pitch, up to
    /// <see cref="MaxZoomMm"/>; the first setting that passes is returned, so the change is the smallest of that order.
    /// A body off the segment returns the defaults unchanged.
    /// </summary>
    /// <param name="defaultBoomMm">Default boom, mm.</param>
    /// <param name="defaultPitchMrad">Default pitch, mrad.</param>
    /// <param name="fovDeg">Vertical field of view, degrees.</param>
    /// <param name="bodyDistanceFromPivotMm">The body's distance from the pivot, mm.</param>
    /// <param name="bodyHeightMm">Body height, mm.</param>
    /// <param name="bodyOnSegment">True when the body stands on the camera-to-pivot segment.</param>
    /// <returns>The boom, mm, and pitch, mrad.</returns>
    public static (int BoomMm, int PitchMrad) FrameForBody(
        int defaultBoomMm, int defaultPitchMrad, int fovDeg, int bodyDistanceFromPivotMm, int bodyHeightMm, bool bodyOnSegment)
    {
        if (!bodyOnSegment)
        {
            return (defaultBoomMm, defaultPitchMrad);
        }

        const int limit = CameraNearFade.ScreenHeightLimitPermille;
        for (var pitch = defaultPitchMrad; pitch >= SteepestPitchMrad; pitch -= PitchStepMrad)
        {
            if (BodyScreenPermilleAt(defaultBoomMm, pitch, fovDeg, bodyDistanceFromPivotMm, bodyHeightMm) <= limit)
            {
                return (defaultBoomMm, pitch);
            }
        }

        for (var boom = defaultBoomMm; boom <= MaxZoomMm; boom += BoomStepMm)
        {
            if (BodyScreenPermilleAt(boom, SteepestPitchMrad, fovDeg, bodyDistanceFromPivotMm, bodyHeightMm) <= limit)
            {
                return (boom, SteepestPitchMrad);
            }
        }

        return (MaxZoomMm, SteepestPitchMrad);
    }

    private static long RoundDiv(long a, long b) => a >= 0 ? (a + b / 2) / b : -((-a + b / 2) / b);

    // tan of the angle above (positive) or below the view axis at which a point yMm above the pivot and zMm toward the
    // camera from the pivot is drawn, ppm. Same frame as AxisTanPpm (depth = X.f + boom, height = X.u with
    // f = (sin p, -cos p) and u = (cos p, sin p) in (y, z)); with zMm = 0 the two agree exactly.
    private static long TanToPointPpm(int boomMm, int pitchMrad, int yMm, int zMm)
    {
        var sin = SinUrad(pitchMrad * 1000L);
        var cos = CosUrad(pitchMrad * 1000L);
        var depth = (long)yMm * sin - (long)zMm * cos + (long)boomMm * Million;
        if (depth <= 0)
        {
            throw new ArgumentException("the point is behind the camera");
        }
        var height = (long)yMm * cos + (long)zMm * sin;
        return height * Million / depth;
    }
}
