using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// Port of bots/src/shared/command.ts: integer-valid "walk from A to B" command construction. Positions are
/// sim-space integer mm, so <c>dir</c> and <c>yaw</c> always come from the sim's integer helpers
/// (<see cref="Fixed.NormaliseTo"/>, <see cref="Fixed.YawBetween"/>, <see cref="Fixed.AngleDiff"/>), never from
/// float trig. A command built any other way fails the sim's dir/yaw validation and is dropped.
/// </summary>
public static class Moves
{
    /// <summary>command.ts DIR_SCALE: <c>dir</c> components are a unit vector scaled to the [-1000, 1000] range.</summary>
    public const long DirScale = 1000;

    /// <summary>command.ts yawToward: integer yaw (0..1023) facing from <paramref name="from"/> toward <paramref name="to"/>.</summary>
    /// <param name="from">Origin.</param>
    /// <param name="to">Target point.</param>
    /// <returns>Yaw.</returns>
    public static int YawToward(PointMm from, PointMm to) =>
        Fixed.YawBetween(Vec3.Zero, new Vec3(to.X - from.X, 0, to.Z - from.Z));

    /// <summary>command.ts yawChanged: true when <paramref name="desired"/> is a different facing than <paramref name="current"/>.</summary>
    /// <param name="current">Current yaw.</param>
    /// <param name="desired">Desired yaw.</param>
    /// <returns>True when the angle differs.</returns>
    public static bool YawChanged(long current, long desired) => Fixed.AngleDiff(current, desired) != 0;

    /// <summary>
    /// command.ts moveToward: a "move" command walking <paramref name="actorId"/> from <paramref name="from"/>
    /// toward <paramref name="to"/>; null when the points coincide (no direction to walk or face).
    /// </summary>
    /// <param name="tick">Tick the command applies to.</param>
    /// <param name="actorId">Actor.</param>
    /// <param name="from">Start.</param>
    /// <param name="to">Destination.</param>
    /// <returns>The command, or null.</returns>
    public static MoveCommand? MoveToward(int tick, int actorId, PointMm from, PointMm to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        if (dx == 0 && dz == 0)
        {
            return null;
        }
        var dir = Fixed.NormaliseTo(new Vec3(dx, 0, dz), DirScale);
        return new MoveCommand(tick, actorId, new Dir2(dir.X, dir.Z), YawToward(from, to));
    }
}
