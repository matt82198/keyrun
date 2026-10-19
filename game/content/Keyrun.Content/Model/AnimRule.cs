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
public readonly record struct AnimFacts(
    bool Alive, bool NewTick, bool Moving, bool Casting, bool Acted, bool Hurt, bool Staggered, AnimClip Playing);

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
/// clip's own length and returns.
/// </summary>
public static class AnimRule
{
    /// <summary>The sim event that marks an ability resolving (instants included): <c>actorId</c> is the caster.</summary>
    public const string ResolveEvent = "cast_complete";

    /// <summary>The clip decision for one frame.</summary>
    public static AnimDecision Decide(AnimFacts f) => default;

    /// <summary>
    /// True when <paramref name="events"/> (one sim step's events as type and <c>actorId</c>) hold a
    /// <see cref="ResolveEvent"/> for <paramref name="actorId"/>: the actor's ability resolved this step. Enemy melee
    /// (gcdTicks 0, so the GCD never restarts) is only visible through this.
    /// </summary>
    public static bool Resolved(int actorId, IEnumerable<(string Type, int? ActorId)> events) => false;
}
