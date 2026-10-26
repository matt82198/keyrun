using System;

namespace Keyrun.Content;

/// <summary>
/// Move hysteresis (ruling 1, S5 capture 4135f4fc): an actor whose position changed within the last
/// <see cref="HoldTicks"/> sim ticks keeps its Move clip. The sim steps a body's position once every few ticks, so the
/// interpolated speed reads zero between steps and <see cref="AnimRule.Decide"/> would flip Move to Idle and back.
/// Pure, no Godot types: the view (src/View/ActorAnimator.cs) records the tick of the last position change and feeds it
/// here; the clip rule itself stays stateless.
/// </summary>
public static class MoveHold
{
    /// <summary>How many ticks after a position change the actor still reads as moving (at least the 3-tick step cadence).</summary>
    public const int HoldTicks = 3;

    /// <summary>
    /// True while <paramref name="tick"/> is within <see cref="HoldTicks"/> ticks of <paramref name="lastMovedTick"/>, the
    /// tick of the last position change. A negative <paramref name="lastMovedTick"/> means the actor never moved, so the
    /// hold never reports moving.
    /// </summary>
    public static bool Holds(int tick, int lastMovedTick)
    {
        if (lastMovedTick < 0)
        {
            return false;
        }
        var age = tick - lastMovedTick;
        return age >= 0 && age < HoldTicks;
    }
}
