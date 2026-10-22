using System;
using System.Linq;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>What the objective's task line counts (f-c5fb7b31).</summary>
public enum ObjectiveFocus
{
    /// <summary>The current pull's own enemies (the default; every pull that is engaged, and every trash pull).</summary>
    Pull,

    /// <summary>An engaged generated trash pack's living enemies, while the next boss pull is not yet engaged.</summary>
    Pack,

    /// <summary>No count: trash is alive but no pack is engaged and the next boss pull is not engaged yet.</summary>
    Hidden,
}

/// <summary>The objective line for the current pull (Godot-free).</summary>
/// <param name="Number">1-based position of the current pull in the dungeon (boss last).</param>
/// <param name="Total">Pulls in the dungeon, boss included.</param>
/// <param name="PullId">The sim pull id ("choir", ..., "boss").</param>
/// <param name="Name">The pull's content display name (the boss's enemy name for the boss pull).</param>
/// <param name="Remaining">Enemies of this pull still alive; once complete, enemies still alive anywhere in the run.</param>
/// <param name="Count">Enemies the pull spawned; once complete, enemies the run spawned.</param>
/// <param name="Complete">True once the pull index is past the last pull (the run is cleared).</param>
/// <param name="Focus">What the task line counts (f-c5fb7b31).</param>
/// <param name="Segment">1-based segment of a multi-segment dungeon's boss pull (R-MULTIBOSS-1), or 0 for every other pull.</param>
/// <param name="Segments">Segments in the dungeon when <paramref name="Segment"/> is set, else 0.</param>
public sealed record ObjectiveLine(int Number, int Total, string PullId, string Name, int Remaining, int Count, bool Complete, ObjectiveFocus Focus = ObjectiveFocus.Pull, int Segment = 0, int Segments = 0)
{
    /// <summary>
    /// FINISH-NEEDS-FORCES (f-8c50a787, WoW Mythic+): Enemy Forces in basis points while the pulls are cleared but the
    /// dungeon's forces goal is not met (the run is still open), else null.
    /// </summary>
    public int? ForcesShortBp { get; init; }

    /// <summary>
    /// The line a player reads: "Pull 1/4  The Choir" (<see cref="PullHeader.Title"/>; the content <c>teaches</c> note
    /// is designer intent and is never shown). On a multi-segment route the segment's boss pull reads "Boss k/n: &lt;boss&gt;"
    /// while the boss is the pull, and "Segment k/n: toward &lt;boss&gt;" while trash of that segment is what is fought
    /// (WoW M+ names the boss as the objective only at the boss; the trash objective is forces).
    /// </summary>
    public string Title => Complete
        ? FormattableString.Invariant($"{Name} cleared")
        : Segment > 0
            ? Focus == ObjectiveFocus.Pull
                ? FormattableString.Invariant($"Boss {Segment}/{Segments}: {Name}")
                : FormattableString.Invariant($"Segment {Segment}/{Segments}: toward {Name}")
            : new PullHeader(Number, Total, Name).Title;

    /// <summary>
    /// The task under it: "Defeat the pull: 3 of 3 remain", or "Defeat the pack" while an engaged trash pack is what
    /// is fought, or empty (hidden) while trash is alive and the next boss pull is not engaged (f-c5fb7b31). Once the
    /// pulls are cleared but the forces goal is short, it says what is owed (FINISH-NEEDS-FORCES).
    /// </summary>
    public string Task => Complete && ForcesShortBp is int shortBp
        ? FormattableString.Invariant($"Enemy Forces {shortBp / 100}% - finish the count")
        : Complete
        ? Remaining switch
        {
            0 => "No enemies remain",
            1 => "1 enemy left standing",
            _ => FormattableString.Invariant($"{Remaining} enemies left standing"),
        }
        : Focus switch
        {
            ObjectiveFocus.Pack => FormattableString.Invariant($"Defeat the pack: {Remaining} of {Count} remain"),
            ObjectiveFocus.Hidden => string.Empty,
            _ => FormattableString.Invariant($"Defeat the pull: {Remaining} of {Count} remain"),
        };

    /// <summary>"HUD objective pull=1/4 id=choir name="The Choir" remaining=3/3".</summary>
    public string LogLine() => FormattableString.Invariant(
        $"HUD objective pull={Number}/{Total} id={PullId} name=\"{Name}\" remaining={Remaining}/{Count}{(ForcesShortBp is int bp ? FormattableString.Invariant($" forces_short={bp}") : string.Empty)}{(Complete ? " complete" : string.Empty)}");
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
        // R-MULTIBOSS-1: the route order includes every segment boss pull, so the header's number and total agree with --pull N.
        // (Same list as main's content.Pulls + LayoutDef.BossPullIds; PullHeader.RouteOrder is that list.)
        var contentIds = content is null ? pulls.Select(p => p.Id).ToList() : PullHeader.RouteOrder(content).ToList();
        var total = contentIds.Count;
        var index = run.Encounter.PullIndex;
        if (index >= pulls.Count)
        {
            // Cleared: the boss pull spawns only the boss, so a Sundered Twin can outlive it and stays alive (frozen
            // by RUN-END-FREEZE, Run.StepRun). Count the enemies the sim still holds alive, never claim zero.
            var standing = run.State.Actors.Values.Count(a => a.Alive && a.Kind == ActorKind.Enemy);
            var enemies = run.State.Actors.Values.Count(a => a.Kind == ActorKind.Enemy);
            // FINISH-NEEDS-FORCES: the boss is dead but the count is short, so the run is still open; say what is owed.
            var shortBp = run.RunEnd.Result is null && run.Dungeon.ForcesGoal is { } goal && !goal.Met(run.State)
                ? goal.Read(run.State).PercentBp
                : (int?)null;
            return new ObjectiveLine(total, total, "done", content?.Name ?? run.Dungeon.Id, standing, enemies, true) { ForcesShortBp = shortBp };
        }
        var pull = pulls[index];
        var number = contentIds.IndexOf(pull.Id) + 1;
        if (number <= 0)
        {
            number = index + 1;
        }
        var name = content is not null && PullHeader.Of(registry!, content, pull.Id) is { } header ? header.Name : pull.Id;
        // R-MULTIBOSS-1: a segment's boss pull is the segment (k of n) whether the party fights its trash or the boss.
        var bossAt = content is not null && content.Segments.Count > 1 ? LayoutDef.BossPullIds(content).ToList().IndexOf(pull.Id) : -1;
        var segment = bossAt + 1;
        var segments = bossAt >= 0 ? content!.Segments.Count : 0;
        var ids = pull.Spawns.Select(s => s.ActorId).ToHashSet();
        var remaining = run.State.Actors.Values.Count(a => ids.Contains(a.Id) && a.Alive && a.Kind == ActorKind.Enemy);
        // An actor not spawned yet is still owed: count the spawns the state does not hold yet.
        remaining += ids.Count(id => !run.State.Actors.ContainsKey(id));
        // f-c5fb7b31 (P0, wrong data): while the next boss pull is not engaged, its header names the boss but its count
        // must not describe the boss. Trash is what the party fights: count the engaged pack's living enemies, or hide
        // the count while trash is alive and no pack is engaged. Never "1 of 1 remain" while trash is alive.
        if (LayoutDef.IsBossPull(pull.Id) && !run.Encounter.Engaged.Contains(pull.Id, StringComparer.Ordinal))
        {
            var trash = Aggro.GroupsOf(run.Dungeon, run.State.Tick, run.Content.Packs, run.Content.Layout, run.Content.Patrols)
                .Where(g => g.PullIndex < 0)
                .ToList();
            var engaged = trash.Where(g => run.Encounter.Engaged.Contains(g.Id, StringComparer.Ordinal)).ToList();
            var engagedSpawned = engaged.Sum(g => g.ActorIds.Count);
            var engagedLiving = engaged.SelectMany(g => g.ActorIds).Count(id => IsLivingEnemy(run, id));
            if (engagedLiving > 0)
            {
                return new ObjectiveLine(number, total, pull.Id, name, engagedLiving, engagedSpawned, false, ObjectiveFocus.Pack, segment, segments);
            }
            if (trash.SelectMany(g => g.ActorIds).Any(id => IsLivingEnemy(run, id)))
            {
                return new ObjectiveLine(number, total, pull.Id, name, remaining, ids.Count, false, ObjectiveFocus.Hidden, segment, segments);
            }
        }
        return new ObjectiveLine(number, total, pull.Id, name, remaining, ids.Count, false, ObjectiveFocus.Pull, segment, segments);
    }

    private static bool IsLivingEnemy(Run run, int actorId) =>
        run.State.Actors.TryGetValue(actorId, out var actor) && actor.Alive && actor.Kind == ActorKind.Enemy;
}
