using System;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>The objective line for the current pull (Godot-free).</summary>
/// <param name="Number">1-based position of the current pull in the dungeon (boss last).</param>
/// <param name="Total">Pulls in the dungeon, boss included.</param>
/// <param name="PullId">The sim pull id ("choir", ..., "boss").</param>
/// <param name="Name">The pull's content display name (the boss's enemy name for the boss pull).</param>
/// <param name="Remaining">Enemies of this pull still alive; once complete, enemies still alive anywhere in the run.</param>
/// <param name="Count">Enemies the pull spawned; once complete, enemies the run spawned.</param>
/// <param name="Complete">True once the pull index is past the last pull (the run is cleared).</param>
public sealed record ObjectiveLine(int Number, int Total, string PullId, string Name, int Remaining, int Count, bool Complete)
{
    /// <summary>
    /// The line a player reads: "Pull 1/4  The Choir" (<see cref="PullHeader.Title"/>; the content <c>teaches</c> note
    /// is designer intent and is never shown).
    /// </summary>
    public string Title => Complete
        ? FormattableString.Invariant($"{Name} cleared")
        : new PullHeader(Number, Total, Name).Title;

    /// <summary>The task under it: "Defeat the pull: 3 of 3 remain".</summary>
    public string Task => Complete
        ? Remaining switch
        {
            0 => "No enemies remain",
            1 => "1 enemy left standing",
            _ => FormattableString.Invariant($"{Remaining} enemies left standing"),
        }
        : FormattableString.Invariant($"Defeat the pull: {Remaining} of {Count} remain");

    /// <summary>"HUD objective pull=1/4 id=choir name="The Choir" remaining=3/3".</summary>
    public string LogLine() => FormattableString.Invariant(
        $"HUD objective pull={Number}/{Total} id={PullId} name=\"{Name}\" remaining={Remaining}/{Count}{(Complete ? " complete" : string.Empty)}");
}

/// <summary>
/// Builds the objective line from the run (Godot-free; moved out of Objective.cs so host tests drive the header's own
/// rule, HUD-SCOREBOARD-LIVE).
/// </summary>
public static class ObjectiveModel
{
    /// <summary>
    /// The current pull: the sim's <c>Encounter.PullIndex</c> into the run's dungeon, named and numbered from the
    /// content dungeon by pull id (a host that starts at a later pull still numbers it in the full dungeon).
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="registry">Content, or null (ids only, numbered within the run's dungeon).</param>
    /// <returns>The line.</returns>
    public static ObjectiveLine Build(Run run, ContentRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(run);
        var pulls = run.Dungeon.Pulls;
        var content = registry is not null && registry.Dungeons.TryGetValue(run.Dungeon.Id, out var d) ? d : null;
        var contentIds = content is null ? pulls.Select(p => p.Id).ToList() : content.Pulls.Select(p => p.Id).Append("boss").ToList();
        var total = contentIds.Count;
        var index = run.Encounter.PullIndex;
        if (index >= pulls.Count)
        {
            // Cleared: the boss pull spawns only the boss, so a Sundered Twin can outlive it and stays alive (frozen
            // by RUN-END-FREEZE, Run.StepRun). Count the enemies the sim still holds alive, never claim zero.
            var standing = run.State.Actors.Values.Count(a => a.Alive && a.Kind == ActorKind.Enemy);
            var enemies = run.State.Actors.Values.Count(a => a.Kind == ActorKind.Enemy);
            return new ObjectiveLine(total, total, "done", content?.Name ?? run.Dungeon.Id, standing, enemies, true);
        }
        var pull = pulls[index];
        var number = contentIds.IndexOf(pull.Id) + 1;
        if (number <= 0)
        {
            number = index + 1;
        }
        var name = content is not null && PullHeader.Of(registry!, content, pull.Id) is { } header ? header.Name : pull.Id;
        var ids = pull.Spawns.Select(s => s.ActorId).ToHashSet();
        var remaining = run.State.Actors.Values.Count(a => ids.Contains(a.Id) && a.Alive && a.Kind == ActorKind.Enemy);
        // An actor not spawned yet is still owed: count the spawns the state does not hold yet.
        remaining += ids.Count(id => !run.State.Actors.ContainsKey(id));
        return new ObjectiveLine(number, total, pull.Id, name, remaining, ids.Count, false);
    }
}
