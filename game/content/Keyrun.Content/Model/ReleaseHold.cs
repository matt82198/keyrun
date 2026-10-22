using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// ANIMS-READABLE-2 (Matt 2026-10-21 "theres still no ability animations", f-a5ffb07b; KF1 f-fa28e36e). Pure, no Godot
/// types; src/View/ActorAnimator.cs reads it. Every release (the one-shot an ability plays on its <c>cast_complete</c>)
/// shows for at least <see cref="MinReleaseMs"/> of screen time, six render frames of a 20 Hz step pair, before a chained
/// cast's pose or the next cast's wind-up may take the body back; a clip with less than that left from where the view
/// starts it is slowed (<see cref="SpeedPermille"/>), never cut. When one step resolves two abilities for one actor
/// (a cast and an instant fired on its resolve, Flare then Cold Snap), <see cref="Resolved"/> lists both in event order
/// and the view plays them one after the other (<see cref="NextQueuedNow"/>). The sim is never touched.
/// </summary>
public static class ReleaseHold
{
    /// <summary>The shortest a release shows, ms.</summary>
    public const int MinReleaseMs = 300;

    /// <summary>
    /// Where the release window of <paramref name="clip"/> started at <paramref name="startMs"/> ends, ms into the clip:
    /// <see cref="AnimRule.ReleaseEndsMs"/>, but never sooner than <see cref="MinReleaseMs"/> past the start (a swing a
    /// cast wound up starts at contact, which left it only <see cref="AnimRule.ReleaseTailMs"/>).
    /// </summary>
    public static int EndsMs(string clip, int startMs)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return Math.Max(AnimRule.ReleaseEndsMs(clip), startMs + MinReleaseMs);
    }

    /// <summary>True while <paramref name="playedMs"/> (ms into the clip) is before <see cref="EndsMs"/>.</summary>
    public static bool InRelease(string clip, int startMs, int playedMs) => playedMs < EndsMs(clip, startMs);

    /// <summary>
    /// The playback rate, 1/1000 of the clip's own, that makes a clip of <paramref name="lengthMs"/> started at
    /// <paramref name="startMs"/> last at least <see cref="MinReleaseMs"/>: 1000 when it already does or has nothing left.
    /// </summary>
    public static int SpeedPermille(int startMs, int lengthMs)
    {
        var left = lengthMs - startMs;
        if (left <= 0 || left >= MinReleaseMs)
        {
            return 1000;
        }
        return Math.Max(1, left * 1000 / MinReleaseMs);
    }

    /// <summary>
    /// Every ability <paramref name="actorId"/> resolved in one step's <paramref name="events"/> (type, <c>actorId</c>,
    /// <c>abilityId</c>), in event order: the view plays the first now and queues the rest.
    /// </summary>
    public static IReadOnlyList<string> Resolved(int actorId, IEnumerable<(string Type, int? ActorId, string? AbilityId)> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var list = new List<string>();
        foreach (var (type, id, ability) in events)
        {
            if (id == actorId && ability is not null && string.Equals(type, AnimRule.ResolveEvent, StringComparison.Ordinal))
            {
                list.Add(ability);
            }
        }
        return list;
    }

    /// <summary>
    /// True when a cast's wind-up must wait: the playing one-shot is a release still inside its window. The wind-up is
    /// posed from sim time each frame, so it resumes at the right point once the release has shown.
    /// </summary>
    public static bool HoldsOffWindup(AnimClip playing, bool inRelease) => playing == AnimClip.Act && inRelease;

    /// <summary>True when the next of <paramref name="queued"/> same-step releases may start: no release is in its window.</summary>
    public static bool NextQueuedNow(int queued, AnimClip playing, bool inRelease) =>
        queued > 0 && !HoldsOffWindup(playing, inRelease);
}
