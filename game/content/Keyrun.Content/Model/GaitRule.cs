using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// KF1 gait (Matt 2026-10-19, "combat animation reads badly"): the playback rate of a run or walk loop that keeps the
/// planted foot still on the floor. RED STUB: plays every loop at its own rate (the defect), so the body skates.
/// </summary>
public static class GaitRule
{
    /// <summary>Playback at the clip's own rate, permille.</summary>
    public const int UnitPermille = 1000;

    /// <summary>Slowest playback, permille.</summary>
    public const int MinPermille = 250;

    /// <summary>Fastest playback, permille.</summary>
    public const int MaxPermille = 3000;

    private static readonly Dictionary<string, int> GroundMmpsByClip = new(StringComparer.Ordinal)
    {
        ["Running_A"] = 3370,
        ["Walking_A"] = 717,
    };

    /// <summary>The clip's planted-foot ground speed at native scale, mm/s; null for a clip that is not a gait.</summary>
    public static int? GroundMmps(string clip) =>
        GroundMmpsByClip.TryGetValue(clip, out var mmps) ? mmps : null;

    /// <summary>RED STUB: always the clip's own rate.</summary>
    public static int PlaybackPermille(string clip, int speedMmps, int scalePermille) => UnitPermille;
}
