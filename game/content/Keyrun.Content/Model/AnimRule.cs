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
/// (<see cref="Kicked"/>). KF1-READABLE (f-fa28e36e): a resolve on the step the next cast starts plays the release over
/// that cast's loop until <see cref="ReleaseEndsMs"/> (a chained caster used to loop its cast pose forever).
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

    /// <summary>
    /// KF1-READABLE: where the release window of <paramref name="clip"/> ends, in ms into the clip: its contact frame
    /// plus <see cref="ReleaseTailMs"/> (a clip with no known contact counts contact as 0). Over a chained cast the
    /// release plays from <see cref="SwingStartMs"/> to here, then the next cast's pose resumes.
    /// </summary>
    public static int ReleaseEndsMs(string clip) => (ContactMs(clip) ?? 0) + ReleaseTailMs;

    /// <summary>True while <paramref name="playedMs"/> (ms into <paramref name="clip"/>) is before <see cref="ReleaseEndsMs"/>.</summary>
    public static bool InRelease(string clip, int playedMs) => playedMs < ReleaseEndsMs(clip);

    /// <summary>The engine proof line for a combat clip starting on an actor: <c>VIEW anim actor=N clip=X tick=T</c>.</summary>
    public static string ViewLine(int actorId, string clip, int tick)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return FormattableString.Invariant($"VIEW anim actor={actorId} clip={clip} tick={tick}");
    }

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
        // KF1-WINDUP (AbilityAnim.ActClip), measured the same way (hand.r; the shield hand.l for Block_Attack) on the
        // Knight and Skeleton_Rogue tracks at 30 fps, rounded to 100 ms from 200, 733, 467, 333 and 767 ms.
        ["1H_Melee_Attack_Slice_Horizontal"] = 200,
        ["1H_Melee_Attack_Stab"] = 700,
        ["Block_Attack"] = 500,
        ["2H_Melee_Attack_Slice"] = 300,
        ["1H_Melee_Attack_Jump_Chop"] = 800,
    };

    /// <summary>The contact frame of <paramref name="clip"/> in ms, or null for a clip not in the table.</summary>
    public static int? ContactMs(string clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return ContactMsByClip.TryGetValue(clip, out var ms) ? ms : null;
    }

    /// <summary>
    /// R-ANIM-2 (b): where the swing one-shot starts, in ms into <paramref name="clip"/>, on the tick its ability resolves.
    /// Always 0: the clip starts at frame 0 on the resolve tick, as WoW plays an instant. The hit presentation waits for
    /// the contact frame instead (<see cref="HitPresentationDelayMs"/>); the sim damage tick is unchanged.
    /// </summary>
    public static int SwingStartMs(string clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return 0;
    }

    /// <summary>R-ANIM-2 (b): the longest the hit presentation (spark, body flash, floating damage) waits for contact, in ms.</summary>
    public const int HitPresentationCapMs = 300;

    /// <summary>
    /// R-ANIM-2 (b): how long the hit presentation of a resolved attack on <paramref name="clip"/> waits after the resolve
    /// tick, in ms: the clip's contact frame, capped at <see cref="HitPresentationCapMs"/>. Presentation only; the sim
    /// damage tick is not moved. A clip with no known contact waits 0 ms.
    /// </summary>
    public static int HitPresentationDelayMs(string clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return Math.Min(ContactMs(clip) ?? 0, HitPresentationCapMs);
    }

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
    /// ENEMY-BODY-STABLE: the hit flash for one actor's role. An enemy body never flashes (0 at every tick, so its colour
    /// and alpha stay stable and its cast and shape stay readable); a party body (<see cref="FadeRole.Self"/>,
    /// <see cref="FadeRole.Ally"/>) keeps <see cref="FlashPermille"/> unchanged.
    /// </summary>
    /// <param name="role">Who the body is.</param>
    /// <param name="ticksSinceHurt">The current sim tick minus the hurt tick.</param>
    /// <param name="alphaPermille">The render interpolation into the current tick (0 to 999).</param>
    /// <returns>The flash strength in permille, 0 to 1000.</returns>
    public static int FlashPermilleFor(FadeRole role, int ticksSinceHurt, int alphaPermille) =>
        role == FadeRole.Enemy ? 0 : FlashPermille(ticksSinceHurt, alphaPermille);

    /// <summary>
    /// ENEMY-BODY-STABLE receipt line for a hit flash applied on an actor: <c>FLASH actor=N tick=T strength=S</c>, written
    /// through Driver.HostLine so it reaches the run log (GD.Print never does).
    /// </summary>
    /// <param name="actorId">The sim actor id.</param>
    /// <param name="tick">The sim tick the flash is shown on.</param>
    /// <param name="strength">The flash strength in permille.</param>
    public static string FlashLine(int actorId, int tick, int strength) =>
        FormattableString.Invariant($"FLASH actor={actorId} tick={tick} strength={strength}");

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
            // KF1-READABLE: a caster that chains casts starts the next one on the step the last resolves, so the cast
            // loop used to hide every release. The release plays over the new cast for its window, then the pose resumes.
            if (f.NewTick && f.Released)
            {
                return new AnimDecision(AnimClip.Act, true, true);
            }
            if (f.Playing == AnimClip.Act && f.InRelease)
            {
                return new AnimDecision(AnimClip.Act, true, false);
            }
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
