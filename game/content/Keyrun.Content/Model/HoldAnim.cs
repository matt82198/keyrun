using System;

namespace Keyrun.Content;

/// <summary>
/// KF1-SPAWN-HOLD (f-fa28e36e, "combat animation reads badly"): what an enemy shows while it exists but holds before it
/// engages, and how it leaves that hold. Pure, no Godot types: src/View/ActorAnimator.cs asks it. It reads only the
/// per-frame <see cref="AnimFacts"/> and the actor id, so it works the same for a pack that spawns on room entry and for
/// one that prespawns idle at run start (PRESPAWN-AGGRO). Two rules: the spawn rise is held only while the body stands
/// still with nothing else to show (a 3.567 s skeleton rise held to its end slid a half-buried chaser up to 12.8 m and
/// hid its first swing), and each actor's idle loop starts at its own phase (a pack bound on one frame otherwise
/// breathes in lockstep).
/// </summary>
public static class HoldAnim
{
    /// <summary>
    /// Blend from a cut spawn rise into the clip that replaces it, in ms: longer than the animator's 150 ms clip blend,
    /// so a body cut mid-rise comes up out of the floor instead of popping.
    /// </summary>
    public const int RiseCutBlendMs = 300;

    /// <summary>
    /// True while the spawn rise keeps playing this frame: the actor is alive, still, not casting, and this step gave
    /// it no swing or stagger to show. A health drop alone keeps the rise (the KF1 hit flash shows it). Anything else
    /// cuts the rise and <see cref="AnimRule.Decide"/> picks the replacement (run, cast, swing, hit, death).
    /// </summary>
    public static bool KeepsRise(AnimFacts f) =>
        f.Alive && !f.Moving && !f.Casting && !f.Acted && !f.Staggered;

    /// <summary>
    /// Where <paramref name="actorId"/>'s idle loop starts, in ms into a loop <paramref name="loopMs"/> long:
    /// a deterministic multiplicative hash (Knuth, 2654435761) of the id, so consecutive pack ids land far apart on the
    /// loop. 0 for a non-positive loop length.
    /// </summary>
    public static int IdlePhaseMs(int actorId, int loopMs)
    {
        if (loopMs <= 0)
        {
            return 0;
        }
        var h = unchecked((uint)actorId * 2654435761u);
        return (int)(h % (uint)loopMs);
    }
}
