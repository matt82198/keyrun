using System;

namespace Keyrun.Content;

/// <summary>
/// KF1-SPAWN-HOLD (f-fa28e36e, "combat animation reads badly"): what an enemy shows while it exists but holds before it
/// engages, and how it leaves that hold. Pure, no Godot types: src/View/ActorAnimator.cs asks it. Works the same for a
/// pack that spawns on room entry and for one that prespawns idle at run start (PRESPAWN-AGGRO).
/// </summary>
public static class HoldAnim
{
    /// <summary>Blend from a cut spawn rise into the clip that replaces it, in ms.</summary>
    public const int RiseCutBlendMs = 300;

    /// <summary>
    /// True while the spawn rise keeps playing this frame. RED STUB: the rise is held to its end whatever the body does
    /// (main's behaviour).
    /// </summary>
    public static bool KeepsRise(AnimFacts f) => true;

    /// <summary>
    /// Where <paramref name="actorId"/>'s idle loop starts, in ms into a loop <paramref name="loopMs"/> long. RED STUB:
    /// every idle starts from frame 0 (main's behaviour).
    /// </summary>
    public static int IdlePhaseMs(int actorId, int loopMs) => 0;
}
