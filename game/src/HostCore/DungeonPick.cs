using System;
using System.Collections.Generic;
using System.Linq;
using C = Keyrun.Content;

namespace Keyrun.Host;

/// <summary>
/// D65 / f-61f01ca3: which dungeon a run plays. The arena's <c>--dungeon ID</c> flag, the play script's <c>-Dungeon</c>
/// switch and the pre-run screen's pick all go through this one seam. The default is the Hollow Vault; an id the
/// registry does not hold is an error that names the id and lists the registry's, never a silent fall back. Godot-free.
/// </summary>
public static class DungeonPick
{
    /// <summary>The arena flag.</summary>
    public const string Flag = "--dungeon";

    /// <summary>The dungeon a run plays when none is selected: the tutorial.</summary>
    public const string DefaultId = "hollow-vault";

    /// <summary>The registry's dungeon ids in pick order: the default first, the rest ordinal.</summary>
    /// <param name="registry">The content.</param>
    /// <returns>The ids.</returns>
    public static IReadOnlyList<string> Ids(C.ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.Dungeons.Keys
            .OrderBy(id => string.Equals(id, DefaultId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The dungeon to play: <paramref name="requested"/>, or the default when null.</summary>
    /// <param name="registry">The content.</param>
    /// <param name="requested">The <c>--dungeon</c> value, or null.</param>
    /// <returns>The id.</returns>
    /// <exception cref="ArgumentException">The id is not a dungeon of the registry (the message names it and lists the known ids).</exception>
    public static string Resolve(C.ContentRegistry registry, string? requested)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var id = requested ?? DefaultId;
        if (!registry.Dungeons.ContainsKey(id))
        {
            throw new ArgumentException($"{Flag}: unknown dungeon '{id}'; known dungeons: {string.Join(", ", Ids(registry))}");
        }
        return id;
    }

    /// <summary>The id <paramref name="delta"/> places after <paramref name="current"/> in pick order, wrapping.</summary>
    /// <param name="registry">The content.</param>
    /// <param name="current">The id now picked.</param>
    /// <param name="delta">Steps, negative for back.</param>
    /// <returns>The id.</returns>
    public static string Cycle(C.ContentRegistry registry, string current, int delta)
    {
        var ids = Ids(registry);
        var at = ids.ToList().IndexOf(current);
        var next = ((Math.Max(at, 0) + delta) % ids.Count + ids.Count) % ids.Count;
        return ids[next];
    }

    /// <summary>
    /// True when the content lays out a map for <paramref name="id"/> (layout/ is for one dungeon; a seeded dungeon's rooms
    /// come from the generator that has not landed, so <see cref="HostRunSetup.Build"/> refuses it). The pick still lists it.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="id">The dungeon id.</param>
    /// <returns>Whether a run can be built on it.</returns>
    public static bool HasMap(C.ContentRegistry registry, string id)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return string.Equals(registry.Layout.Dungeon, id, StringComparison.Ordinal);
    }

    /// <summary>What the pick shows: the dungeon's display name and its boss count (one per segment, the tutorial's one).</summary>
    /// <param name="registry">The content.</param>
    /// <param name="id">The dungeon id.</param>
    /// <returns>The name and boss count.</returns>
    public static (string Name, int Bosses) Describe(C.ContentRegistry registry, string id)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var def = registry.Dungeons[Resolve(registry, id)];
        return (def.Name, Math.Max(1, def.Segments.Count));
    }
}
