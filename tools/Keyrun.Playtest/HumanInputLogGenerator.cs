using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Host;
using Keyrun.Sim.Core;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>
/// R-GCD-1: generates a <c>keyrun.input-log/1</c> for one seat's first minute from intent, not from hand-placed ticks.
/// The screen keys pick the class and Enter queues it; for the chanter the generator then presses Q (interrupt) inside
/// each Cantor <c>choir.healCast</c> window, found by replaying the log so far (<see cref="HumanReplay"/>), so the kick
/// lands on the cast whatever the GCD is.
/// </summary>
public static class HumanInputLogGenerator
{
    /// <summary>The key level's run length the first-minute proof plays (run ticks).</summary>
    private const int FirstMinuteTicks = 1200;

    /// <summary>The class whose seat presses Q against the Cantor's heal casts.</summary>
    private const string Chanter = "chanter";

    /// <summary>The Cantor's class id in the Choir pull.</summary>
    private const string CantorClass = "choir.lieutenant";

    /// <summary>The Cantor's heal cast id.</summary>
    private const string HealCast = "choir.healCast";

    /// <summary>Most Q presses one generated log holds (the Cantor casts a heal per window; this bounds the log).</summary>
    private const int MaxKicks = 6;

    /// <summary>Most replays the generator runs while it places kicks and target cycles (bounds the search).</summary>
    private const int MaxRounds = 40;

    /// <summary>The run tick a Q press lands on, relative to the window's start (inside the window, after the cast begins).</summary>
    private const int KickOffset = 1;

    /// <summary>The first screen tick (the first Down).</summary>
    private const int FirstScreenTick = 4;

    /// <summary>Generates the input log for <paramref name="classId"/> at key <paramref name="key"/> and seed <paramref name="seed"/>.</summary>
    /// <param name="classId">The class the human seat plays.</param>
    /// <param name="key">The launch key level.</param>
    /// <param name="seed">The run seed.</param>
    /// <returns>A keyrun.input-log/1 document (no camera headings).</returns>
    /// <exception cref="ArgumentException">The class is not in the roster, or the key is not a valid level.</exception>
    public static InputLogDocument Generate(string classId, int key, long seed)
    {
        ArgumentException.ThrowIfNullOrEmpty(classId);
        var reg = C.ContentLoader.Load();
        var ids = Lobby.ClassIds(reg);
        var target = ids.ToList().IndexOf(classId);
        if (target < 0)
        {
            throw new ArgumentException($"human log generator: class \"{classId}\" is not on the roster");
        }
        var start = Math.Max(0, ids.ToList().IndexOf(Lobby.DefaultClassId));
        var downs = (((target - start) % ids.Count) + ids.Count) % ids.Count;

        var screen = new List<InputLogRawEvent>();
        var tick = FirstScreenTick;
        for (var i = 0; i < downs; i++)
        {
            screen.Add(new InputLogRawEvent(tick, "Down", true));
            tick += 2;
        }
        screen.Add(new InputLogRawEvent(tick + 2, "Enter", true));
        var enterTick = tick + 2;

        // The human walks forward from the queue (W held) so the Cantor's choir engages.
        screen.Add(new InputLogRawEvent(enterTick + 2, "W", true));

        var kicks = new List<InputLogRawEvent>();
        if (classId == Chanter)
        {
            kicks = FindKicks(screen, key, seed, enterTick);
        }

        var events = screen.Concat(kicks).OrderBy(e => e.Tick).ToList();
        return Document(events, key, seed);
    }

    // Replays the log built so far and presses Q at the start of each Cantor heal window the replay reaches, one at a
    // time: the prefix before a press is unchanged by it, so the next window found is the next one after the last kick.
    private static List<InputLogRawEvent> FindKicks(List<InputLogRawEvent> screen, int key, long seed, int enterTick)
    {
        var kicks = new List<InputLogRawEvent>();
        var afterTick = -1;
        var kicked = 0;
        for (var round = 0; round < MaxRounds && kicked < MaxKicks; round++)
        {
            var events = screen.Concat(kicks).OrderBy(e => e.Tick).ToList();
            var log = Document(events, key, seed);
            var windows = new List<(int Actor, int Start, int End)>();
            var targets = new Dictionary<int, int?>();
            var result = HumanReplay.Play(log, FirstMinuteTicks, null, run =>
            {
                if (run.State.Actors.TryGetValue(HostRunSetup.HumanId, out var me))
                {
                    targets[run.State.Tick] = me.TargetId;
                }
                foreach (var a in run.State.Actors.Values)
                {
                    if (a.CastId == HealCast && a.CastStartTick is { } s && a.CastEndsTick is { } e)
                    {
                        windows.Add((a.Id, (int)s, (int)e));
                    }
                }
            });
            var cantors = result.Setup.Dungeon.Pulls[0].Spawns.Where(s => s.ClassId == CantorClass).Select(s => s.ActorId).ToHashSet();
            var next = windows
                .Where(w => cantors.Contains(w.Actor) && w.Start > afterTick && w.Start + KickOffset <= w.End)
                .OrderBy(w => w.Start)
                .Select(w => (int?)w.Start)
                .FirstOrDefault();
            if (next is not { } start)
            {
                break;
            }
            // Q is the interrupt on the human's target: when the target at the kick is not a Cantor, one Tab cycles it
            // (at the tick before the kick, so the next replay sees it) and the same window is tried again.
            var kickTick = start + KickOffset;
            var targetAtKick = targets.TryGetValue(kickTick, out var t) ? t : null;
            if (targetAtKick is not { } id || !cantors.Contains(id))
            {
                kicks.Add(new InputLogRawEvent(enterTick + start - 1, "Tab", true));
                continue;
            }
            kicks.Add(new InputLogRawEvent(enterTick + kickTick, "Q", true));
            afterTick = kickTick;
            kicked++;
        }
        return kicks;
    }

    private static InputLogDocument Document(IReadOnlyList<InputLogRawEvent> events, int key, long seed) =>
        new(InputLogDocument.SchemaV1, seed, key, 0, null, false, events, Array.Empty<InputLogCamera>());
}
