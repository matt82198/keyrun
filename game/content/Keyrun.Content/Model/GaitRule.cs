using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// KF1 gait (Matt 2026-10-19, "combat animation reads badly"): the playback rate of a run or walk loop that keeps the
/// planted foot still on the floor. The KayKit loops run in place, so the body only looks like it walks when the sim
/// moves it as fast as the planted foot sweeps backwards in the clip. At speed 1 every bound body skated: the party
/// Warden (Running_A at 0.709 scale) sweeps 2389 mm/s against the sim's 3600, and the Hollow Warden (Walking_A at 1.9)
/// sweeps 1362 mm/s. Integers only (game/CLAUDE.md purity rule): the view divides the permille by 1000.
/// </summary>
public static class GaitRule
{
    /// <summary>Playback at the clip's own rate, permille: anything with no stride to match.</summary>
    public const int UnitPermille = 1000;

    /// <summary>Slowest playback, permille: a body easing to a stop keeps visibly stepping.</summary>
    public const int MinPermille = 250;

    /// <summary>
    /// Fastest playback, permille: a render spike (a snap, a knockback) never blurs the legs. Above every bound row's
    /// sim speed (the Hollow Warden walks at 2642 at the base 180 mm/tick), so it never caps ordinary movement.
    /// </summary>
    public const int MaxPermille = 3000;

    // Planted-foot ground speed of each in-place loop at native scale (1 glTF unit = 1000 mm), mm/s: the mean backward
    // speed of the lower foot while it is within 40 mm of the floor, by forward kinematics over the clip's tracks. The
    // KayKit Adventurers and Skeletons share the rig and both clips (measured identical in Knight, Mage, Skeleton_Minion
    // and Skeleton_Warrior). Running_A contacts for 0.18 s of its 0.80 s cycle; Walking_A is grounded almost
    // throughout its 1.07 s cycle.
    private static readonly Dictionary<string, int> GroundMmpsByClip = new(StringComparer.Ordinal)
    {
        ["Running_A"] = 3370,
        ["Walking_A"] = 717,
    };

    /// <summary>The clip's planted-foot ground speed at native scale, mm/s; null for a clip that is not a gait.</summary>
    public static int? GroundMmps(string clip) =>
        GroundMmpsByClip.TryGetValue(clip, out var mmps) ? mmps : null;

    /// <summary>
    /// The playback rate, permille, that makes <paramref name="clip"/>'s planted foot sweep at
    /// <paramref name="speedMmps"/> on a model drawn at <paramref name="scalePermille"/>, clamped to
    /// [<see cref="MinPermille"/>, <see cref="MaxPermille"/>]. <see cref="UnitPermille"/> for a clip with no planted-foot
    /// speed, a body that is not moving, or a non-positive scale.
    /// </summary>
    public static int PlaybackPermille(string clip, int speedMmps, int scalePermille)
    {
        if (GroundMmps(clip) is not int ground || speedMmps <= 0 || scalePermille <= 0)
        {
            return UnitPermille;
        }
        var footAtUnitMmps = (long)ground * scalePermille; // mm/s x 1000
        var permille = (long)speedMmps * 1_000_000 / footAtUnitMmps;
        return (int)Math.Clamp(permille, MinPermille, MaxPermille);
    }
}
