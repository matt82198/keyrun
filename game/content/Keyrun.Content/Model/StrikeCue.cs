using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>One sim event as the strike cue reads it: its type, tick, the actor it names and the ability (cast_complete only).</summary>
/// <param name="Type">The sim event type (<see cref="StrikeCueRule.DamageEvent"/> or <see cref="StrikeCueRule.ResolveEvent"/>).</param>
/// <param name="Tick">The sim tick the event was emitted on.</param>
/// <param name="ActorId">The attacker: <c>sourceId</c> on a damage event, <c>actorId</c> on a cast_complete.</param>
/// <param name="AbilityId">The resolved ability (cast_complete only); null on a damage event.</param>
/// <param name="TargetId">The damaged actor (<c>targetId</c>, damage event only); null otherwise.</param>
public readonly record struct StrikeEvent(string Type, int Tick, int ActorId, string? AbilityId = null, int? TargetId = null);

/// <summary>
/// One melee swing's visible strike cue: drawn on the tick the strike's damage lands (KF1 "combat animation reads badly";
/// the swing clip alone does not show where the blow goes). The view draws a blade at hand height that sweeps across the
/// target for <see cref="DurationMs"/> (<see cref="StrikeCueRule.Pose"/>), aimed at <see cref="TargetId"/> or, with none,
/// facing <see cref="Yaw"/> from the actor.
/// </summary>
/// <param name="ActorId">The attacker's sim actor id.</param>
/// <param name="Tick">The sim tick the damage landed on.</param>
/// <param name="Yaw">The attacker's facing, 1/1024 turn (the sim's <c>ActorState.Yaw</c>, as the view reads it on that tick).</param>
/// <param name="DurationMs">How long the cue is shown, in ms.</param>
/// <param name="AbilityId">The melee ability that landed.</param>
/// <param name="TargetId">The first actor the strike damaged on that tick (the swing is aimed at it), or null.</param>
public readonly record struct StrikeCue(int ActorId, int Tick, int Yaw, int DurationMs, string AbilityId, int? TargetId = null);

/// <summary>Where the strike cue is drawn at a moment of its window (integers; the view converts to metres and radians).</summary>
/// <param name="Visible">True while the cue shows (0 &lt;= elapsed &lt; <see cref="StrikeCueRule.DurationMs"/>).</param>
/// <param name="HeightMm">The blade's height above the attacker's feet, mm.</param>
/// <param name="HasAim">True when the cue aims at a separate target; false means use the attacker's facing.</param>
/// <param name="AimXMm">Aim direction x (target minus caster), mm.</param>
/// <param name="AimZMm">Aim direction z (target minus caster), mm.</param>
/// <param name="SweepYaw">The blade's offset from the aim, 1/1024 turn (negative to positive across the swing).</param>
/// <param name="ReachMm">How far in front of the attacker the blade reaches, mm.</param>
public readonly record struct StrikeCuePose(bool Visible, int HeightMm, bool HasAim, int AimXMm, int AimZMm, int SweepYaw, int ReachMm);

/// <summary>
/// The Godot-free rule that turns a step's sim events into <see cref="StrikeCue"/>s (KF1, COMBAT-ANIM follow-up). Pure, no
/// Godot types. A cue is emitted once per melee <c>cast_complete</c> whose actor has a <c>damage</c> event on the same
/// tick (a multi-target cleave is one cue, not one per target). The melee set comes from abilities tagged
/// <see cref="MeleeTag"/> (see <see cref="MeleeIds(IEnumerable{string})"/>, fed from the melee data list).
/// </summary>
public static class StrikeCueRule
{
    /// <summary>The sim event for damage applied; <c>sourceId</c> is the attacker.</summary>
    public const string DamageEvent = "damage";

    /// <summary>The sim event for an ability resolving (the same event <see cref="AnimRule.ResolveEvent"/> names).</summary>
    public const string ResolveEvent = AnimRule.ResolveEvent;

    /// <summary>The ability tag in abilities data that marks a melee ability.</summary>
    public const string MeleeTag = "melee";

    /// <summary>
    /// How long the strike arc is shown, in ms. f-e20614cc: the core dungeon loop defers to WoW, whose melee swing runs
    /// about 0.5 to 1.0 s; the old 200 ms cue showed in 1 of about 30 combat frames and read as a floor line.
    /// </summary>
    public const int DurationMs = 500;

    /// <summary>Blade height above the attacker's feet at the start of the swing, mm (top of the hand band).</summary>
    public const int HandHighMm = 1200;

    /// <summary>Blade height at the end of the swing, mm (bottom of the hand band; floor telegraphs stay on the floor).</summary>
    public const int HandLowMm = 1000;

    /// <summary>Half the swing arc, 1/1024 turn (160 = about 56 degrees each side of the target).</summary>
    public const int SweepHalfYaw = 160;

    /// <summary>How far the blade reaches in front of the attacker, mm.</summary>
    public const int ReachMm = 1100;

    /// <summary>
    /// The melee set from the C#-only list (game/content/data/melee-abilities.json, loaded as
    /// <c>ContentRegistry.MeleeAbilities</c>). This is the live source: the root abilities JSON is frozen and carries no melee tag.
    /// </summary>
    /// <param name="meleeAbilityIds">The melee ability ids.</param>
    public static IReadOnlySet<string> MeleeIds(IEnumerable<string> meleeAbilityIds)
    {
        ArgumentNullException.ThrowIfNull(meleeAbilityIds);
        return new HashSet<string>(meleeAbilityIds, StringComparer.Ordinal);
    }

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
    /// <param name="meleeAbilities">The melee ability ids (see <see cref="MeleeIds(IEnumerable{string})"/>).</param>
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
            if (!LandsDamage(list, resolve.ActorId, resolve.Tick, out var targetId))
            {
                continue;
            }
            cues.Add(new StrikeCue(resolve.ActorId, resolve.Tick, yawOf(resolve.ActorId), DurationMs, resolve.AbilityId, targetId));
        }
        return cues;
    }

    /// <summary>
    /// The cue pose <paramref name="elapsedMs"/> into its window (view-only; the hit already landed on the cue's tick, so
    /// nothing here touches hit timing). The blade is held at hand height (<see cref="HandHighMm"/> coming down to
    /// <see cref="HandLowMm"/>), aimed from the caster at the target, and sweeps from -<see cref="SweepHalfYaw"/> to
    /// +<see cref="SweepHalfYaw"/> across the window, crossing the target at mid-swing. A target on the caster's own spot
    /// has no aim (the view falls back to the attacker's facing).
    /// </summary>
    /// <param name="casterXMm">Caster x, mm.</param>
    /// <param name="casterZMm">Caster z, mm.</param>
    /// <param name="targetXMm">Target x, mm.</param>
    /// <param name="targetZMm">Target z, mm.</param>
    /// <param name="elapsedMs">Time since the cue started, ms.</param>
    public static StrikeCuePose Pose(int casterXMm, int casterZMm, int targetXMm, int targetZMm, int elapsedMs)
    {
        var visible = elapsedMs >= 0 && elapsedMs < DurationMs;
        var e = Math.Clamp(elapsedMs, 0, DurationMs - 1);
        var last = DurationMs - 1;
        var height = HandHighMm - ((HandHighMm - HandLowMm) * e / last);
        var sweep = -SweepHalfYaw + (2 * SweepHalfYaw * e / last);
        var ax = targetXMm - casterXMm;
        var az = targetZMm - casterZMm;
        var hasAim = ax != 0 || az != 0;
        return new StrikeCuePose(visible, height, hasAim, ax, az, sweep, ReachMm);
    }

    // True when the step holds a damage event from this actor on this tick (a cleave counts once, at the cue level);
    // targetId is the first such event's target.
    private static bool LandsDamage(IReadOnlyList<StrikeEvent> events, int actorId, int tick, out int? targetId)
    {
        foreach (var e in events)
        {
            if (e.Tick == tick && e.ActorId == actorId && string.Equals(e.Type, DamageEvent, StringComparison.Ordinal))
            {
                targetId = e.TargetId;
                return true;
            }
        }
        targetId = null;
        return false;
    }
}
