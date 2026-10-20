using System;
using Keyrun.Sim.Core;

namespace Keyrun.Hud;

/// <summary>
/// Lane HUD-DECLUTTER (Matt 2026-10-19: "move friendly casts into party frames btw, only show the played characters
/// damage numbers in fight, reduce screen clutter essentially... enemy health does not need to be overhead, they can
/// have unitframes but not just a random number"). The Godot-free placement rules every HUD and view node reads, so
/// what a node draws and the proof line it prints come from the same decision. Display only; never a rule.
/// </summary>
public static class HudDeclutter
{
    /// <summary>
    /// True when <paramref name="actor"/>'s cast bar is drawn over its head: enemies only. A party member's cast (the
    /// human's and the bots') is drawn inside its party frame instead. Enemy cast bars stay over the head (BIBLE s3,
    /// "Cast bars are the enemy's language") and are also drawn on the enemy's unit frame.
    /// </summary>
    /// <param name="actor">The casting actor.</param>
    /// <returns>True for an overhead bar.</returns>
    public static bool OverheadCast(ActorState actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.Kind != ActorKind.Player;
    }

    /// <summary>"overhead" or "frame": where <paramref name="actor"/>'s cast bar is drawn.</summary>
    /// <param name="actor">The casting actor.</param>
    /// <returns>The placement word the proof lines print.</returns>
    public static string CastPlacement(ActorState actor) => OverheadCast(actor) ? "overhead" : "frame";

    /// <summary>
    /// True when <paramref name="e"/> spawns a floating damage number in the world: a "damage" event whose source is
    /// the human's seat and whose modified amount (health damage plus absorbed) is positive. A bot's hit, an enemy's
    /// hit and a zero hit spawn nothing.
    /// </summary>
    /// <param name="e">One sim event of the step.</param>
    /// <param name="humanActorId">The played character's actor id.</param>
    /// <param name="targetId">The hit actor.</param>
    /// <param name="amount">The number to show.</param>
    /// <returns>True when a number is shown.</returns>
    public static bool WorldDamageNumber(SimEvent e, int humanActorId, out int targetId, out long amount)
    {
        ArgumentNullException.ThrowIfNull(e);
        targetId = 0;
        amount = 0;
        if (e.Type != "damage" || e.Get("sourceId") is not JsInt source || source.Value != humanActorId ||
            e.Get("targetId") is not JsInt target || e.Get("modifiedAmount") is not JsInt modified || modified.Value <= 0)
        {
            return false;
        }
        targetId = (int)target.Value;
        amount = modified.Value;
        return true;
    }

    /// <summary>"player" or "enemy", as the proof lines print an actor's side.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The side word.</returns>
    public static string Side(ActorState actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.Kind == ActorKind.Player ? "player" : "enemy";
    }
}
