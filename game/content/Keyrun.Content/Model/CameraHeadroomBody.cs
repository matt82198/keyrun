using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>A body the headroom rule may frame: its feet at (<paramref name="X"/>, <paramref name="Z"/>) mm on the ground
/// plane, its top <paramref name="TopMm"/> mm above its feet, and whether it is an engaged hostile of the Warden.</summary>
/// <param name="X">Feet X, mm.</param>
/// <param name="Z">Feet Z, mm.</param>
/// <param name="TopMm">Top above the feet, mm.</param>
/// <param name="Hostile">True when the body is hostile to the Warden.</param>
/// <param name="Engaged">True when the pull has engaged it (not idle, alive).</param>
public readonly record struct HeadroomBody(int X, int Z, int TopMm, bool Hostile, bool Engaged);

/// <summary>
/// CAMERA-HEADROOM body choice (Matt's dungeon-loop ruling: the camera serves the readability of the thing that will kill
/// you, the WoW Mythic+ way). The headroom body is the tallest engaged hostile that stands inside the camera's view cone
/// and within the Warden's reach; a boss beats a twin and a twin beats a trash body by height alone. When none qualifies
/// the caller falls back to the Warden's current target. Pure: integers only, no Godot types, no sim state.
/// </summary>
public static class CameraHeadroomBody
{
    /// <summary>Cosine of the view-cone half-angle, permille (700 = about 46 degrees either side of the view heading).</summary>
    public const int ConeCosPermille = 700;

    /// <summary>
    /// The index in <paramref name="bodies"/> of the headroom body, or null when no body is an engaged hostile inside the
    /// view cone and within reach. The tallest such body wins; on a tie the earlier index wins.
    /// </summary>
    /// <param name="bodies">The candidate bodies, in a stable order (the caller sorts by actor id).</param>
    /// <param name="cameraX">Camera X, mm: the view cone's apex.</param>
    /// <param name="cameraZ">Camera Z, mm.</param>
    /// <param name="forwardX">Horizontal view heading, permille (the unit vector times 1000).</param>
    /// <param name="forwardZ">Horizontal view heading Z, permille.</param>
    /// <param name="pivotX">The Warden's pivot X, mm: the reach is measured from here.</param>
    /// <param name="pivotZ">The Warden's pivot Z, mm.</param>
    /// <param name="reachMm">The Warden's reach, mm.</param>
    /// <returns>The index of the headroom body, or null.</returns>
    public static int? Select(
        IReadOnlyList<HeadroomBody> bodies,
        int cameraX,
        int cameraZ,
        int forwardX,
        int forwardZ,
        int pivotX,
        int pivotZ,
        int reachMm)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        int? best = null;
        var bestTop = int.MinValue;
        for (var i = 0; i < bodies.Count; i++)
        {
            var body = bodies[i];
            if (!body.Hostile || !body.Engaged || body.TopMm <= bestTop)
            {
                continue;
            }
            if (!InCone(body.X - cameraX, body.Z - cameraZ, forwardX, forwardZ)
                || !InReach(body.X - pivotX, body.Z - pivotZ, reachMm))
            {
                continue;
            }
            best = i;
            bestTop = body.TopMm;
        }
        return best;
    }

    // Inside the view cone: the offset points forward and its cosine to the heading is at least ConeCosPermille / 1000,
    // tested squared in Int128 (dot^2 * 1e6 >= cos^2 * 1e6 * |v|^2 * |f|^2 with the permille heading).
    private static bool InCone(long vx, long vz, long fx, long fz)
    {
        var dot = vx * fx + vz * fz;
        if (dot <= 0)
        {
            return false;
        }
        var lhs = (Int128)dot * dot * 1_000_000;
        var rhs = (Int128)(ConeCosPermille * ConeCosPermille) * ((vx * vx + vz * vz) * (fx * fx + fz * fz));
        return lhs >= rhs;
    }

    private static bool InReach(long dx, long dz, int reachMm) => dx * dx + dz * dz <= (long)reachMm * reachMm;
}
