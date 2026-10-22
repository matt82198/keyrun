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

    /// <summary>
    /// The layout seed for kit-only builds (a talent or Facet patch reads the setup's abilities, never its layout), so a
    /// multi-segment dungeon builds without a run seed in hand.
    /// </summary>
    public const long KitSeed = 1;

    /// <summary>
    /// f-61f01ca3: the run setup for <paramref name="dungeonId"/>, the one call SimDriver builds a run through. A
    /// multi-segment dungeon (the Sunken Bellfoundry, R-MULTIBOSS-1) is generated from a seed: <paramref name="dungeonSeed"/>
    /// (<c>--dungeon-seed N</c>) when given, else the run's own <paramref name="runSeed"/> (<c>--seed N</c>), never a
    /// constant; the hand-built Hollow Vault ignores both.
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="dungeonId">The dungeon (<see cref="Resolve"/>d).</param>
    /// <param name="runSeed">The run seed (<c>--seed</c>).</param>
    /// <param name="dungeonSeed">The layout seed override (<c>--dungeon-seed</c>), or null.</param>
    /// <param name="keyLevel">The key level.</param>
    /// <param name="humanClassId">The human's class.</param>
    /// <param name="withBots">False for the human's seat alone.</param>
    /// <param name="rotationIndex">The affix rotation index.</param>
    /// <returns>The setup.</returns>
    public static HostRunSetup BuildSetup(C.ContentRegistry registry, string dungeonId, long runSeed, long? dungeonSeed, int keyLevel, string humanClassId, bool withBots = true, int rotationIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return HostRunSetup.Build(registry, dungeonId, keyLevel, humanClassId, withBots, rotationIndex, dungeonSeed: dungeonSeed ?? runSeed);
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
    /// True when a run can be laid out for <paramref name="id"/>: layout/ is the hand-built map of one dungeon, and a
    /// multi-segment dungeon (the Sunken Bellfoundry) is generated, filled and laid out from its seed
    /// (<see cref="HostRunSetup.Build"/>, R-MULTIBOSS-1; <see cref="BuildSetup"/> supplies the seed).
    /// </summary>
    /// <param name="registry">The content.</param>
    /// <param name="id">The dungeon id.</param>
    /// <returns>Whether a run can be built on it.</returns>
    public static bool HasMap(C.ContentRegistry registry, string id)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return string.Equals(registry.Layout.Dungeon, id, StringComparison.Ordinal)
            || (registry.Dungeons.TryGetValue(id, out var def) && def.Segments.Count > 1);
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
