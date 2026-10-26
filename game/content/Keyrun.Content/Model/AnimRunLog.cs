using System;

namespace Keyrun.Content;

/// <summary>
/// The run-log proof line for a looped clip change (<c>ANIM actor=N clip=X ability=- at=T</c>). Pure: no Godot types. A line is
/// written only when the clip changed, so an unengaged pack that stays Idle logs nothing, and a capture can prove that
/// it played its Move clip. <c>src/View/ActorAnimator.cs</c> prints the line through the same run-log sink as the
/// ABILITY-ANIMS lines.
/// </summary>
public static class AnimRunLog
{
    /// <summary>
    /// The line for <paramref name="actorId"/> changing from <paramref name="previous"/> to <paramref name="clip"/> at sim
    /// <paramref name="tick"/>, or null when the clip did not change.
    /// </summary>
    public static string? Line(int actorId, AnimClip previous, AnimClip clip, int tick)
    {
        if (previous == clip)
        {
            return null;
        }
        // ability=- is the scene parser's placeholder for a clip no ability started (AbilityAnimSceneTest.ps1 header).
        return FormattableString.Invariant($"ANIM actor={actorId} clip={clip} ability=- at={tick}");
    }
}
