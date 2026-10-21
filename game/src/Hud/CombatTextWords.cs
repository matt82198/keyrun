using System;
using Keyrun.Sim.Core;
using CombatTextRule = Keyrun.Content.CombatTextRule;

namespace Keyrun.Hud;

/// <summary>
/// The word floats of the combat text (Godot-free, so real runs drive it in tools/Keyrun.Playtest.Tests): which sim
/// event floats a word instead of a number, over which actor, and the proof line <see cref="CombatText"/> prints for it.
/// Lane MISS-VIEW: the human's own "miss" floats "MISS" (<see cref="CombatTextRule.HumanMiss"/>). Finding f-04dba988
/// (WoW floats "Immune" over the target): the human's stun refused by a stun-immune actor (#429's "immune" event)
/// floats "IMMUNE" over that actor (<see cref="CombatTextRule.HumanImmune"/>). A bot's or an enemy's miss or refused
/// effect floats nothing (HUD-DECLUTTER). Display only; never a rule.
/// </summary>
public static class CombatTextWords
{
    /// <summary>
    /// True when <paramref name="e"/> floats a word over an actor. Each word has its own proof prefix
    /// (<c>HUD miss</c>, <c>HUD immune</c>), so <c>HUD fct</c> stays damage-only.
    /// </summary>
    /// <param name="e">One sim event of the step.</param>
    /// <param name="humanActorId">The played character's actor id.</param>
    /// <param name="targetId">The actor the word floats over (0 when none).</param>
    /// <param name="text">The word (empty when none).</param>
    /// <param name="proof">The proof line to print (empty when none).</param>
    /// <returns>True when a word floats.</returns>
    public static bool WordFloat(SimEvent e, int humanActorId, out int targetId, out string text, out string proof)
    {
        ArgumentNullException.ThrowIfNull(e);
        targetId = 0;
        text = string.Empty;
        proof = string.Empty;
        var source = (e.Get("sourceId") as JsInt)?.Value;
        var target = (e.Get("targetId") as JsInt)?.Value;
        if (CombatTextRule.HumanMiss(e.Type, source, target, humanActorId, out var missed))
        {
            targetId = missed;
            text = CombatTextRule.MissText;
            proof = FormattableString.Invariant($"HUD miss source={humanActorId} target={missed} text={CombatTextRule.MissText}");
            return true;
        }
        if (CombatTextRule.HumanImmune(e.Type, source, target, humanActorId, out var immune))
        {
            var effect = e.Get("effectId") is JsString id ? id.Value : "-";
            targetId = immune;
            text = CombatTextRule.ImmuneText;
            proof = FormattableString.Invariant($"HUD immune source={humanActorId} target={immune} effect={effect} text={CombatTextRule.ImmuneText}");
            return true;
        }
        return false;
    }
}
