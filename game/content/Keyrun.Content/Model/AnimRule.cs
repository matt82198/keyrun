using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>Which of a row's <see cref="ActorClips"/> an actor plays.</summary>
public enum AnimClip
{
    /// <summary>No clip (as an input: no one-shot is still playing).</summary>
    None,

    /// <summary><see cref="ActorClips.Idle"/>, looped.</summary>
    Idle,

    /// <summary><see cref="ActorClips.Move"/>, looped.</summary>
    Move,

    /// <summary><see cref="ActorClips.Cast"/>, looped while the sim actor has a <c>CastId</c>.</summary>
    Cast,

    /// <summary><see cref="ActorClips.Act"/>, the swing: once when an ability resolves.</summary>
    Act,

    /// <summary><see cref="ActorClips.Hit"/>, the hit react: once on a health drop or a stagger.</summary>
    Hit,

    /// <summary><see cref="ActorClips.Death"/>, once, last frame held.</summary>
    Death,
}

/// <summary>The per-frame facts the animator reads from the sim (all view-readable, none written back).</summary>
/// <param name="Alive">The sim actor's <c>Alive</c>.</param>
/// <param name="NewTick">True on the first frame after a sim step; the tick facts below only count then.</param>
/// <param name="Moving">Interpolated speed above the run threshold (the view compares the float; the rule sees a bool).</param>
/// <param name="Casting">The sim actor has a <c>CastId</c>.</param>
/// <param name="Acted">This step resolved one of the actor's abilities (<see cref="AnimRule.Resolved"/>) or restarted its GCD.</param>
/// <param name="Hurt">This step dropped the actor's health.</param>
/// <param name="Staggered">This step applied or refreshed the stagger effect (<see cref="ActorBinding.StaggerEffectId"/>).</param>
/// <param name="Playing">The one-shot still playing from an earlier frame, or <see cref="AnimClip.None"/>.</param>
/// <param name="Released">
/// This step's events hold the actor's own <see cref="AnimRule.ResolveEvent"/> (a cast or instant resolved), as opposed
/// to <paramref name="Acted"/>, which also fires when a cast merely starts (the GCD restarts on cast start).
/// </param>
/// <param name="InRelease">The playing swing one-shot is still inside <see cref="AnimRule.ReleaseEndsMs"/>.</param>
public readonly record struct AnimFacts(
    bool Alive, bool NewTick, bool Moving, bool Casting, bool Acted, bool Hurt, bool Staggered, AnimClip Playing,
    bool Released = false, bool InRelease = false);

/// <summary>What the animator does this frame.</summary>
/// <param name="Clip">The clip to start or keep.</param>
/// <param name="OneShot">True for a clip that plays once and then hands back to run or idle.</param>
/// <param name="Restart">True to start <paramref name="Clip"/> from its first frame now (a fresh swing or hit).</param>
public readonly record struct AnimDecision(AnimClip Clip, bool OneShot, bool Restart);

/// <summary>
/// The view-only rule for which clip an actor plays (COMBAT-ANIM, KF1; BIBLE s3: what enemies do is the player's
/// language, so every swing, cast and hit reads on an actor in motion). Pure, no Godot types: src/View/ActorAnimator.cs
/// feeds it each frame. Priority: death, then the cast loop, then on a new tick a fresh swing (restart), a stagger
/// (restart the hit), a health drop (restart the hit unless a one-shot is still playing), then a playing one-shot is
/// held to its end whether the actor moves or not, then run or idle. A one-shot interrupts the run cycle for the
/// clip's own length and returns. KF1 (2026-10-20, "combat animation reads badly"): a swing starts at
/// <see cref="SwingStartMs"/> so its blow lands on the hit tick, every health drop flashes the victim
/// (<see cref="FlashPermille"/>) even while a one-shot hides the hit clip, and a kicked caster flinches
/// (<see cref="Kicked"/>).
/// </summary>
public static class AnimRule
{
    /// <summary>The sim event that marks an ability resolving (instants included): <c>actorId</c> is the caster.</summary>
    public const string ResolveEvent = "cast_complete";

    /// <summary>The sim event for a kick landing on a cast: <c>targetId</c> is the caster whose cast was cut.</summary>
    public const string InterruptEvent = "interrupt";

    /// <summary>
    /// KF1 swing timing: how far before the clip's contact frame the swing starts when its ability resolves, in ms
    /// (two 50 ms ticks). The sim resolves a melee swing and its damage on one tick, so a clip started from frame 0 on
    /// that tick lands its blow 300 to 800 ms after the damage number; starting it this close to contact lands the
    /// blow with the number and keeps a short wind-up.
    /// </summary>
    public const int SwingLeadMs = 100;

    /// <summary>KF1-READABLE: how long a release keeps playing past its contact frame over a chained cast, in ms.</summary>
    public const int ReleaseTailMs = 250;

    /// <summary>RED STUB (KF1-READABLE): where the release window ends, in ms into the swing clip.</summary>
    public static int ReleaseEndsMs(string clip) => 0;

    /// <summary>RED STUB (KF1-READABLE): true while <paramref name="playedMs"/> is inside the release window.</summary>
    public static bool InRelease(string clip, int playedMs) => false;

    /// <summary>RED STUB (KF1-READABLE): the engine proof line for a clip start.</summary>
    public static string ViewLine(int actorId, string clip, int tick) => string.Empty;

    /// <summary>KF1 hit flash: a health drop lights the victim for this many sim ticks, fading linearly.</summary>
    public const int FlashTicks = 3;

    // Contact frame (ms into the clip) of each attack clip the binding rows play: the time the striking hand (foot for
    // the kick) reaches furthest forward, measured from the KayKit Rig_Medium glTF tracks by forward kinematics.
    private static readonly Dictionary<string, int> ContactMsByClip = new(StringComparer.Ordinal)
    {
        ["1H_Melee_Attack_Chop"] = 700,
        ["2H_Melee_Attack_Chop"] = 800,
        ["Unarmed_Melee_Attack_Punch_A"] = 500,
        ["Unarmed_Melee_Attack_Kick"] = 400,
        ["Spellcast_Shoot"] = 300,
        ["Spellcast_Raise"] = 600,
    };

    /// <summary>The contact frame of <paramref name="clip"/> in ms, or null for a clip not in the table.</summary>
    public static int? ContactMs(string clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return ContactMsByClip.TryGetValue(clip, out var ms) ? ms : null;
    }

    /// <summary>
    /// Where the swing one-shot starts, in ms into <paramref name="clip"/>, on the tick its ability resolves:
    /// <see cref="SwingLeadMs"/> before contact, so the blow lands with the sim's hit; 0 for a clip with no known contact.
    /// </summary>
    public static int SwingStartMs(string clip) =>
        ContactMs(clip) is int contact ? Math.Max(0, contact - SwingLeadMs) : 0;

    /// <summary>
    /// The hit flash strength in permille: 1000 on the frame a health drop lands, fading to 0 over
    /// <see cref="FlashTicks"/> ticks. <paramref name="ticksSinceHurt"/> is the current sim tick minus the hurt tick and
    /// <paramref name="alphaPermille"/> the render interpolation into the current tick (0 to 999).
    /// </summary>
    public static int FlashPermille(int ticksSinceHurt, int alphaPermille)
    {
        if (ticksSinceHurt < 0 || ticksSinceHurt >= FlashTicks)
        {
            return 0; // also keeps a far-past (or never) hurt tick from overflowing the permille sum below
        }
        var elapsed = (ticksSinceHurt * 1000) + Math.Clamp(alphaPermille, 0, 999);
        var span = FlashTicks * 1000;
        return elapsed >= span ? 0 : 1000 - (elapsed * 1000 / span);
    }

    /// <summary>
    /// True when <paramref name="events"/> (one sim step's events as type and <c>targetId</c>) hold an
    /// <see cref="InterruptEvent"/> on <paramref name="actorId"/>: its cast was kicked this step. The kicked caster plays
    /// the hit one-shot (a flinch) instead of dropping its cast loop straight to idle.
    /// </summary>
    public static bool Kicked(int actorId, IEnumerable<(string Type, int? TargetId)> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var (type, id) in events)
        {
            if (id == actorId && string.Equals(type, InterruptEvent, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The clip decision for one frame.</summary>
    public static AnimDecision Decide(AnimFacts f)
    {
        if (!f.Alive)
        {
            return new AnimDecision(AnimClip.Death, true, false);
        }
        if (f.Casting)
        {
            return new AnimDecision(AnimClip.Cast, false, false);
        }
        if (f.NewTick)
        {
            if (f.Acted)
            {
                return new AnimDecision(AnimClip.Act, true, true);
            }
            if (f.Staggered || (f.Hurt && f.Playing == AnimClip.None))
            {
                return new AnimDecision(AnimClip.Hit, true, true);
            }
        }
        if (f.Playing != AnimClip.None)
        {
            return new AnimDecision(f.Playing, true, false);
        }
        return new AnimDecision(f.Moving ? AnimClip.Move : AnimClip.Idle, false, false);
    }

    /// <summary>
    /// True when <paramref name="events"/> (one sim step's events as type and <c>actorId</c>) hold a
    /// <see cref="ResolveEvent"/> for <paramref name="actorId"/>: the actor's ability resolved this step. Enemy melee
    /// (gcdTicks 0, so the GCD never restarts) is only visible through this.
    /// </summary>
    public static bool Resolved(int actorId, IEnumerable<(string Type, int? ActorId)> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var (type, id) in events)
        {
            if (id == actorId && string.Equals(type, ResolveEvent, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
