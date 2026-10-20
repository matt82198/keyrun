namespace Keyrun.Content;

/// <summary>
/// Which sim "miss" events float a "MISS" text in the world (lane MISS-VIEW, finding f-8a1e4403). PR #223 made a
/// blinded source's hit emit "miss" (sourceId, targetId, rawAmount) in place of "damage"
/// (game/sim/Keyrun.Sim/Combat/Mitigation.cs DealDamage). HUD-DECLUTTER (Matt 2026-10-19: "only show the played
/// characters damage numbers in fight") limits floating text to the human's seat, so only the human's own miss shows;
/// a bot's or an enemy's miss shows nothing. Pure and Godot-free: src/Hud/CombatText.cs reads it, so what is drawn and
/// the proof line it prints come from the same decision. Display only; never a rule.
/// </summary>
public static class CombatTextRule
{
    /// <summary>The sim event type a blinded source's hit emits in place of "damage".</summary>
    public const string MissEventType = "miss";

    /// <summary>The text a human miss floats over its target.</summary>
    public const string MissText = "MISS";

    /// <summary>
    /// True when one sim event floats a <see cref="MissText"/> over its target: a <see cref="MissEventType"/> event
    /// whose source is the human's seat and that names a target. Any other event type, a bot's or an enemy's miss, and
    /// a miss with no source or target id spawn nothing.
    /// </summary>
    /// <param name="eventType">The event's type.</param>
    /// <param name="sourceId">The event's sourceId, or null when it carries none.</param>
    /// <param name="targetId">The event's targetId, or null when it carries none.</param>
    /// <param name="humanActorId">The played character's actor id.</param>
    /// <param name="missedTargetId">The actor the text floats over (0 when no text).</param>
    /// <returns>True when a MISS text is shown.</returns>
    public static bool HumanMiss(string eventType, long? sourceId, long? targetId, int humanActorId, out int missedTargetId)
    {
        missedTargetId = 0;
        if (eventType != MissEventType || sourceId != humanActorId || targetId is not { } target)
        {
            return false;
        }
        missedTargetId = (int)target;
        return true;
    }
}
