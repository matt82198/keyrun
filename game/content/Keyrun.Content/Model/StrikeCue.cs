using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>One sim event as the strike cue reads it: its type, tick, the actor it names and the ability (cast_complete only).</summary>
/// <param name="Type">The sim event type (<see cref="StrikeCueRule.DamageEvent"/> or <see cref="StrikeCueRule.ResolveEvent"/>).</param>
/// <param name="Tick">The sim tick the event was emitted on.</param>
/// <param name="ActorId">The attacker: <c>sourceId</c> on a damage event, <c>actorId</c> on a cast_complete.</param>
/// <param name="AbilityId">The resolved ability (cast_complete only); null on a damage event.</param>
public readonly record struct StrikeEvent(string Type, int Tick, int ActorId, string? AbilityId = null);

/// <summary>
/// One melee swing's visible strike cue: drawn on the tick the strike's damage lands (KF1 "combat animation reads badly";
/// the swing clip alone does not show where the blow goes). The view draws a short arc flash of <see cref="DurationMs"/>
/// facing <see cref="Yaw"/> from the actor.
/// </summary>
/// <param name="ActorId">The attacker's sim actor id.</param>
/// <param name="Tick">The sim tick the damage landed on.</param>
/// <param name="Yaw">The attacker's facing, 1/1024 turn (the sim's <c>ActorState.Yaw</c>, as the view reads it on that tick).</param>
/// <param name="DurationMs">How long the cue is shown, in ms.</param>
/// <param name="AbilityId">The melee ability that landed.</param>
public readonly record struct StrikeCue(int ActorId, int Tick, int Yaw, int DurationMs, string AbilityId);

/// <summary>
/// The Godot-free rule that turns a step's sim events into <see cref="StrikeCue"/>s (KF1, COMBAT-ANIM follow-up). Pure, no
/// Godot types. A cue is emitted once per melee <c>cast_complete</c> whose actor has a <c>damage</c> event on the same
/// tick (a multi-target cleave is one cue, not one per target). The melee set comes from abilities tagged
/// <see cref="MeleeTag"/> (see <see cref="MeleeIds"/>).
/// </summary>
public static class StrikeCueRule
{
    /// <summary>The sim event for damage applied; <c>sourceId</c> is the attacker.</summary>
    public const string DamageEvent = "damage";

    /// <summary>The sim event for an ability resolving (the same event <see cref="AnimRule.ResolveEvent"/> names).</summary>
    public const string ResolveEvent = AnimRule.ResolveEvent;

    /// <summary>The ability tag in abilities data that marks a melee ability.</summary>
    public const string MeleeTag = "melee";

    /// <summary>How long the strike arc is shown, in ms (150 to 250 so it reads from the demo camera without lingering).</summary>
    public const int DurationMs = 200;

    /// <summary>The ids of the abilities whose tags contain <see cref="MeleeTag"/>.</summary>
    /// <param name="abilities">Each ability's id and tags (null tags count as none).</param>
    public static IReadOnlySet<string> MeleeIds(IEnumerable<(string Id, IReadOnlyList<string>? Tags)> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, tags) in abilities)
        {
            if (tags is not null && tags.Contains(MeleeTag, StringComparer.Ordinal))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    /// <summary>The strike cues for one step's events, in event order.</summary>
    /// <param name="events">The step's events (damage and cast_complete; other types are ignored).</param>
    /// <param name="meleeAbilities">The melee ability ids (see <see cref="MeleeIds"/>).</param>
    /// <param name="yawOf">The actor's facing on the step, 1/1024 turn.</param>
    public static IReadOnlyList<StrikeCue> Cues(
        IEnumerable<StrikeEvent> events,
        IReadOnlySet<string> meleeAbilities,
        Func<int, int> yawOf)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(meleeAbilities);
        ArgumentNullException.ThrowIfNull(yawOf);
        var list = events as IReadOnlyList<StrikeEvent> ?? new List<StrikeEvent>(events);
        var cues = new List<StrikeCue>();
        for (var i = 0; i < list.Count; i++)
        {
            var resolve = list[i];
            if (!string.Equals(resolve.Type, ResolveEvent, StringComparison.Ordinal)
                || resolve.AbilityId is null
                || !meleeAbilities.Contains(resolve.AbilityId))
            {
                continue;
            }
            if (!LandsDamage(list, resolve.ActorId, resolve.Tick))
            {
                continue;
            }
            cues.Add(new StrikeCue(resolve.ActorId, resolve.Tick, yawOf(resolve.ActorId), DurationMs, resolve.AbilityId));
        }
        return cues;
    }

    // True when the step holds a damage event from this actor on this tick (a cleave counts once, at the cue level).
    private static bool LandsDamage(IReadOnlyList<StrikeEvent> events, int actorId, int tick)
    {
        foreach (var e in events)
        {
            if (e.Tick == tick && e.ActorId == actorId && string.Equals(e.Type, DamageEvent, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
