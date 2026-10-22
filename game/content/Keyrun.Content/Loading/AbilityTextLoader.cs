using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// Loads game/content/data/ability-text.json (logical path <see cref="ContentLoader.AbilityTextFile"/>, listed in
/// <see cref="ContentLoader.Files"/> like theme.json, not parsed into the registry) with the loader's strict JSON rules and
/// checks it fail closed against the loaded classes: every class kit ability (the nine slots of each class) has exactly
/// one row, no row names anything else, every display name is one to <see cref="AbilityTextTable.MaxWords"/> words, every
/// flavor is non-empty, and no two display names in one class collide (case-insensitive). Render data only.
/// </summary>
public static class AbilityTextLoader
{
    /// <summary>Loads and checks the table embedded in Keyrun.Content.dll.</summary>
    /// <returns>The table.</returns>
    public static AbilityTextTable Load() => Load(EmbeddedContentSource.Instance);

    /// <summary>Loads the full content from <paramref name="source"/> and the table checked against it.</summary>
    /// <param name="source">A full content source.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ContentLoadException">Content fails to load, or the table breaks a rule.</exception>
    public static AbilityTextTable Load(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Read(source, ContentLoader.Load(source));
    }

    /// <summary>Reads the table from <paramref name="source"/> and checks it against an already loaded registry.</summary>
    /// <param name="source">The content source the registry was loaded from.</param>
    /// <param name="registry">The loaded content.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ContentLoadException">The file fails to parse, or the table breaks a rule.</exception>
    public static AbilityTextTable Read(IContentSource source, ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registry);
        var def = ContentLoader.ReadOne<AbilityTextDef>(source, ContentLoader.AbilityTextFile)[0];
        var errors = Violations(def.Abilities, registry);
        if (errors.Count > 0)
        {
            throw new ContentLoadException("content: " + ContentLoader.AbilityTextFile + " " + string.Join("; ", errors));
        }
        return new AbilityTextTable(def.Abilities);
    }

    /// <summary>Every rule the rows break against <paramref name="registry"/>, empty when they hold.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="registry">The loaded content.</param>
    /// <returns>One message per violation.</returns>
    public static IReadOnlyList<string> Violations(IReadOnlyList<AbilityTextRow> rows, ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(registry);
        var errors = new List<string>();
        var kitClass = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cls in registry.Classes.Values)
        {
            foreach (var id in KitIds(cls.Abilities))
            {
                kitClass[id] = cls.Id;
            }
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!seen.Add(row.Id))
            {
                errors.Add($"duplicate row \"{row.Id}\"");
                continue;
            }
            if (!kitClass.TryGetValue(row.Id, out var classId))
            {
                errors.Add($"row \"{row.Id}\" is not a player class kit ability");
                continue;
            }
            var words = AbilityTextTable.Words(row.DisplayName);
            if (words.Length == 0 || words.Length > AbilityTextTable.MaxWords)
            {
                errors.Add($"\"{row.Id}\" displayName \"{row.DisplayName}\" has {words.Length} words (want 1 to {AbilityTextTable.MaxWords})");
            }
            if (string.IsNullOrWhiteSpace(row.Flavor))
            {
                errors.Add($"\"{row.Id}\" has an empty flavor");
            }
            var key = classId + "|" + string.Join(' ', words);
            if (names.TryGetValue(key, out var other))
            {
                errors.Add($"\"{row.Id}\" displayName \"{row.DisplayName}\" collides with \"{other}\" in class {classId}");
            }
            else
            {
                names[key] = row.Id;
            }
        }
        foreach (var id in kitClass.Keys.Where(id => !seen.Contains(id)).OrderBy(id => id, StringComparer.Ordinal))
        {
            errors.Add($"kit ability \"{id}\" ({kitClass[id]}) has no row");
        }
        return errors;
    }

    /// <summary>A class's nine kit ability ids in slot order (rotation 1-5, interrupt, defensive, utility, movement).</summary>
    /// <param name="s">The class slots.</param>
    /// <returns>The ids.</returns>
    public static IReadOnlyList<string> KitIds(ClassAbilitySlots s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new[] { s.Rot1, s.Rot2, s.Rot3, s.Rot4, s.Rot5, s.Interrupt, s.Defensive, s.Utility, s.Movement };
    }
}
