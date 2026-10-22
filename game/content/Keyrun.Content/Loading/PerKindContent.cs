using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// Assembles the C#-only per-kind content directories into the same in-memory records the single files load into
/// (ruling R-LOADER-1), so content grows by adding a file:
/// <list type="bullet">
/// <item><c>layout/&lt;dungeon&gt;/dungeon.json</c> (dungeon, start, route, formation), <c>rooms/*.json</c> (one
/// <see cref="LayoutBox"/> each) and <c>packs/*.json</c> (one <see cref="LayoutPack"/> each) become one
/// <see cref="LayoutDef"/>; rooms and packs keep ordinal path order (name rooms <c>NNN-&lt;name&gt;.json</c>).</item>
/// <item><c>forces/&lt;dungeon&gt;.json</c> (required, route) and <c>forces/values/*.json</c> (one
/// <see cref="ForcesValue"/> each) become one <see cref="ForcesTable"/>; a duplicate enemy id across value files fails.</item>
/// </list>
/// Every file is parsed with the content loader's strict rules (<see cref="ContentLoader.ReadOne{T}"/>).
/// </summary>
public static class PerKindContent
{
    /// <summary>The per-kind layout directory root.</summary>
    public const string LayoutRoot = "layout/";

    /// <summary>The per-kind forces directory root.</summary>
    public const string ForcesRoot = "forces/";

    /// <summary>The forces values directory: one <see cref="ForcesValue"/> per file.</summary>
    public const string ForcesValuesDir = ForcesRoot + "values/";

    /// <summary>The C#-only directories whose files need no list entry (<see cref="ContentFileSet"/>).</summary>
    public static IReadOnlyList<string> KnownDirectories { get; } = new[] { LayoutRoot, ForcesRoot };

    /// <summary>Assembles <c>layout/&lt;dungeonId&gt;/</c> into a <see cref="LayoutDef"/>.</summary>
    /// <param name="source">The content source.</param>
    /// <param name="dungeonId">The dungeon directory name, e.g. <c>hollow-vault</c>.</param>
    /// <returns>The layout, equal to the one the equivalent single layout.json loads into.</returns>
    /// <exception cref="ContentLoadException">A file is missing or fails to parse.</exception>
    public static LayoutDef AssembleLayout(IContentSource source, string dungeonId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(dungeonId);
        string dir = LayoutRoot + dungeonId + "/";
        LayoutDungeonFile head = ContentLoader.ReadOne<LayoutDungeonFile>(source, dir + "dungeon.json")[0];
        LayoutBox[] rooms = ContentDirectory.Under(source, dir + "rooms/").Select(p => ContentLoader.ReadOne<LayoutBox>(source, p)[0]).ToArray();
        LayoutPack[] packs = ContentDirectory.Under(source, dir + "packs/").Select(p => ContentLoader.ReadOne<LayoutPack>(source, p)[0]).ToArray();
        return new LayoutDef
        {
            Dungeon = head.Dungeon,
            Start = head.Start,
            Boxes = new ValueList<LayoutBox>(rooms),
            Route = head.Route,
            Packs = new ValueList<LayoutPack>(packs),
            Formation = head.Formation,
        };
    }

    /// <summary>Assembles <c>forces/&lt;dungeonId&gt;.json</c> and <c>forces/values/*.json</c> into a <see cref="ForcesTable"/>.</summary>
    /// <param name="source">The content source.</param>
    /// <param name="dungeonId">The dungeon, e.g. <c>hollow-vault</c>.</param>
    /// <returns>The forces table, values in ordinal file order.</returns>
    /// <exception cref="ContentLoadException">A file is missing or fails to parse, or two files value the same enemy.</exception>
    public static ForcesTable AssembleForces(IContentSource source, string dungeonId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(dungeonId);
        ForcesRouteFile head = ContentLoader.ReadOne<ForcesRouteFile>(source, ForcesRoot + dungeonId + ".json")[0];
        IEnumerable<ForcesValue> values = ContentDirectory.Under(source, ForcesValuesDir).Select(p => ContentLoader.ReadOne<ForcesValue>(source, p)[0]);
        return new ForcesTable
        {
            Required = head.Required,
            Values = new ValueList<ForcesValue>(UniqueById("forces value", values, v => v.EnemyId)),
            Route = head.Route,
        };
    }

    /// <summary>Reads a single-file layout (today's layout.json shape) with the strict loader rules.</summary>
    /// <param name="source">The content source.</param>
    /// <param name="path">The file, e.g. <c>layout.json</c>.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ContentLoadException">The file is missing or fails to parse.</exception>
    public static LayoutDef ReadLayoutFile(IContentSource source, string path) => ContentLoader.ReadOne<LayoutDef>(source, path)[0];

    /// <summary>Reads a single-file forces table (today's forces.json shape) with the strict loader rules.</summary>
    /// <param name="source">The content source.</param>
    /// <param name="path">The file, e.g. <c>forces.json</c>.</param>
    /// <returns>The forces table.</returns>
    /// <exception cref="ContentLoadException">The file is missing or fails to parse.</exception>
    public static ForcesTable ReadForcesFile(IContentSource source, string path) => ContentLoader.ReadOne<ForcesTable>(source, path)[0];

    /// <summary>
    /// The duplicate-id rule <see cref="ContentLoader"/> applies to every kind: items in order, a repeated id fails
    /// with <c>content: duplicate &lt;kind&gt; id "&lt;id&gt;"</c>.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="kind">The kind named in the error.</param>
    /// <param name="items">The items, in load order.</param>
    /// <param name="idOf">The item's id.</param>
    /// <returns>The items, unchanged order.</returns>
    /// <exception cref="ContentLoadException">Two items share an id.</exception>
    public static IReadOnlyList<T> UniqueById<T>(string kind, IEnumerable<T> items, Func<T, string> idOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(idOf);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<T>();
        foreach (T item in items)
        {
            if (!seen.Add(idOf(item))) throw new ContentLoadException("content: duplicate " + kind + " id \"" + idOf(item) + "\"");
            kept.Add(item);
        }
        return kept;
    }
}

/// <summary>
/// The file-set rule for a content root that mixes the frozen oracle list with per-kind directories: every listed file
/// must be present, and every other JSON file must sit under a known directory. The frozen 19 (content/data/**) and the
/// single C#-only files stay an explicit list; a file under layout/ or forces/ needs no list edit.
/// </summary>
public static class ContentFileSet
{
    /// <summary>Checks <paramref name="source"/> against <paramref name="listed"/> and <paramref name="knownDirectories"/>.</summary>
    /// <param name="source">The content source.</param>
    /// <param name="listed">Files that must each be present.</param>
    /// <param name="knownDirectories">'/'-terminated prefixes whose files need no list entry.</param>
    /// <exception cref="ContentLoadException">A listed file is missing, or a file is neither listed nor under a known directory.</exception>
    public static void Check(IContentSource source, IReadOnlyList<string> listed, IReadOnlyList<string> knownDirectories)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(knownDirectories);
        var present = new HashSet<string>(source.Paths.Where(p => p.EndsWith(".json", StringComparison.Ordinal)), StringComparer.Ordinal);
        foreach (string file in listed)
            if (!present.Contains(file)) throw new ContentLoadException("content: missing file \"" + file + "\"");
        foreach (string file in present.OrderBy(p => p, StringComparer.Ordinal))
        {
            if (listed.Contains(file, StringComparer.Ordinal)) continue;
            if (knownDirectories.Any(d => d.EndsWith('/') && file.StartsWith(d, StringComparison.Ordinal))) continue;
            throw new ContentLoadException(
                "content: unlisted file \"" + file + "\" (add it to ContentLoader.Files, or put it under a known directory: "
                + string.Join(", ", knownDirectories) + ")");
        }
    }
}

/// <summary>layout/&lt;dungeon&gt;/dungeon.json: the layout fields that are not rooms or packs.</summary>
internal sealed record LayoutDungeonFile
{
    /// <summary>Dungeon id.</summary>
    public required string Dungeon { get; init; }

    /// <summary>Party start.</summary>
    public required LayoutPoint Start { get; init; }

    /// <summary>The walk route.</summary>
    public required ValueList<LayoutPoint> Route { get; init; }

    /// <summary>Follower formation.</summary>
    public required ValueList<FormationSlot> Formation { get; init; }
}

/// <summary>forces/&lt;dungeon&gt;.json: the bar's size and the route; the values live one per file.</summary>
internal sealed record ForcesRouteFile
{
    /// <summary>Points that fill the bar.</summary>
    public required int Required { get; init; }

    /// <summary>Fork and rejoin pulls.</summary>
    public required RouteDef Route { get; init; }
}
