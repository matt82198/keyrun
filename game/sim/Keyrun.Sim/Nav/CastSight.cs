using System;
using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Nav;

/// <summary>
/// L5 caster-los (design/DUNGEON-SYNERGY.md section 5, R-SYNERGY-2): the pure, integer-only sight rule an enemy cast
/// obeys. A cast has sight when the straight segment from the caster to its target crosses no layout obstacle
/// (<see cref="NavGrid.SegmentCrosses"/>, exact, no sampling and no float). The obstacles are the layout's own boxes,
/// not the clearance-grown walking boxes <see cref="NavGrid.LineOfSight"/> uses, and the walkable-union test is left
/// out: a caster at a doorway is never blind because of a room's outer corner, only because a pillar or wall is in the way.
/// </summary>
public static class CastSight
{
    /// <summary>True when the segment from <paramref name="from"/> to <paramref name="to"/> crosses none of <paramref name="obstacles"/>.</summary>
    /// <param name="obstacles">The layout obstacles (an empty list is always clear).</param>
    /// <param name="from">The caster's position.</param>
    /// <param name="to">The target's position.</param>
    /// <returns>True when the cast has sight.</returns>
    public static bool Clear(IReadOnlyList<Bounds> obstacles, Vec3 from, Vec3 to)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        for (var i = 0; i < obstacles.Count; i++)
        {
            if (NavGrid.SegmentCrosses(from, to, obstacles[i]))
            {
                return false;
            }
        }
        return true;
    }
}
