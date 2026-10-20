using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// HOLDER-LOCK (STATE.md D30; Matt 2026-10-20 "no aggro meters ... but no pulling aggro unless they intentionally
/// do", "the classic butt pull should still be possible"). Each engaged enemy has one holder
/// (<see cref="ActorState.HolderId"/>) and its scripted casts and chase go at that holder only
/// (Encounter/ScriptTarget.cs). Engage is the body pull: an enemy with no living holder locks the nearest living
/// player (range, then lowest id). After that only a taunt (<see cref="IsTaunt"/>) moves it; the holder's death hands
/// it to the nearest living player; a wipe clears it. Damage, healing and threat never move it. The threat table
/// (<see cref="Threat"/>, including the Warden's Momentum threat) still fills but no longer picks the target; whether
/// it is removed is Matt's open question on PR #306. Mirror: sim/combat/holder.ts (the frozen TS oracle).
/// </summary>
public static class Holder
{
    /// <summary>The tag a taunt carries (warden.taunt is the class's "utility" ability).</summary>
    public const string TauntTag = "utility";

    /// <summary>
    /// A taunt is a target-shaped "utility" ability carrying a threat op: warden.taunt, the same rule the bots use to
    /// find the taunt (bots KitHeals.TauntAbilityId). Shield Bash (interrupt/control, threat 50) is not one.
    /// </summary>
    /// <param name="def">The ability.</param>
    /// <returns>True for a taunt.</returns>
    public static bool IsTaunt(AbilityDef def) =>
        def.Shape.Kind == ShapeKind.Target
        && def.Tags is { } tags && tags.Contains(TauntTag)
        && def.Effects.Any(op => op is ThreatOp);

    /// <summary>A completed taunt by a living player on a living enemy makes the taunter its holder.</summary>
    /// <param name="caster">The taunter.</param>
    /// <param name="target">The enemy it landed on.</param>
    public static void ApplyTaunt(ActorState caster, ActorState target)
    {
        if (caster.Kind == ActorKind.Player && caster.Alive && target.Kind == ActorKind.Enemy && target.Alive)
        {
            target.HolderId = caster.Id;
        }
    }

    /// <summary>
    /// Run each tick by the run root after spawns (and at run creation): every living enemy whose holder is missing or
    /// no longer a living player locks the nearest living player (engage, or the holder's death); with no living
    /// player it holds no one. A living holder is never replaced here.
    /// </summary>
    /// <param name="state">The post-step state (mutated).</param>
    public static void LockHolders(SimState state)
    {
        var players = state.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive).ToList();
        foreach (var enemy in state.Actors.Values)
        {
            if (enemy.Kind != ActorKind.Enemy || !enemy.Alive)
            {
                continue;
            }
            if (enemy.HolderId is { } h && state.Actors.TryGetValue(h, out var holder) && holder.Alive && holder.Kind == ActorKind.Player)
            {
                continue;
            }
            enemy.HolderId = players.Count == 0
                ? null
                : players.OrderBy(p => Casting.RangeMm(enemy, p)).ThenBy(p => p.Id).First().Id;
        }
    }
}
