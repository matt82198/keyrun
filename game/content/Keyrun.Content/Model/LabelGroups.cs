using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>One actor as the overhead label rule reads it (pure; no Godot types).</summary>
/// <param name="Id">The actor id.</param>
/// <param name="Name">The display name ("Paired Warrior").</param>
/// <param name="Alive">Whether the actor is alive in the sim state this frame.</param>
/// <param name="Enemy">True for a hostile actor (plates of an enemy cull by distance).</param>
/// <param name="Human">True for the played character, whose name lives on the ability bar.</param>
/// <param name="DistanceMm">The actor's distance from the viewing player, mm.</param>
public readonly record struct LabelActor(int Id, string Name, bool Alive, bool Enemy, bool Human, long DistanceMm);

/// <summary>One overhead label group: the living members that share a display name, in roster order.</summary>
/// <param name="Name">The shared display name.</param>
/// <param name="ActorIds">The labelled members only (a dead member is never in a group).</param>
public sealed record LabelGroup(string Name, IReadOnlyList<int> ActorIds)
{
    /// <summary>How many labelled members the group holds.</summary>
    public int Count => ActorIds.Count;

    /// <summary>The label text: the name, and " xN" when N labelled members share it.</summary>
    public string Text => Count > 1 ? Name + " x" + Count : Name;
}

/// <summary>
/// The overhead label rule (LABEL-CULL, Matt 2026-10-21 demo frame 1:58): a label exists only for a living actor the
/// plate rule draws (<see cref="PlateRule.DrawsOverhead(int, bool, bool, int?, bool, long)"/>), so a dead enemy's label
/// is gone on the tick its death applies, and an "xN" count never includes a dead member.
/// </summary>
public static class LabelGroups
{
    /// <summary>True when <paramref name="a"/> draws an overhead label this frame (the one rule every label node reads).</summary>
    /// <param name="a">The actor.</param>
    /// <param name="bossFrameActorId">The enemy the boss unit frame names, or null.</param>
    public static bool Labelled(LabelActor a, int? bossFrameActorId) =>
        PlateRule.DrawsOverhead(a.Id, a.Alive, a.Human, bossFrameActorId, a.Enemy, a.DistanceMm);

    /// <summary>
    /// The labelled groups of one roster: every actor the plate rule draws (alive, not the played character, not the
    /// boss-frame enemy, an enemy within plate range), grouped by display name (ordinal), groups in order of first
    /// appearance. A dead actor is in no group, so it is never counted in an "xN".
    /// </summary>
    /// <param name="actors">The roster, every actor of the sim state.</param>
    /// <param name="bossFrameActorId">The enemy the boss unit frame names (no overhead label), or null.</param>
    public static IReadOnlyList<LabelGroup> Build(IEnumerable<LabelActor> actors, int? bossFrameActorId)
    {
        var order = new List<string>();
        var members = new Dictionary<string, List<int>>(System.StringComparer.Ordinal);
        foreach (var a in actors)
        {
            if (!Labelled(a, bossFrameActorId))
            {
                continue;
            }
            if (!members.TryGetValue(a.Name, out var list))
            {
                list = new List<int>();
                members[a.Name] = list;
                order.Add(a.Name);
            }
            list.Add(a.Id);
        }
        var groups = new List<LabelGroup>();
        foreach (var name in order)
        {
            groups.Add(new LabelGroup(name, members[name]));
        }
        return groups;
    }
}
