using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// One player ability's themed text (game/content/data/ability-text.json; Matt 2026-10-22, "cooler names for the
/// players"): the display name the HUD shows and a one-line flavor. The ability's id and its content <c>name</c> stay as
/// they are (bots, tests, fixtures and the run log key on them); only the HUD reads this row.
/// </summary>
public sealed record AbilityTextRow
{
    /// <summary>The player ability id (a class kit slot).</summary>
    public required string Id { get; init; }

    /// <summary>The themed display name, one to <see cref="AbilityTextTable.MaxWords"/> words.</summary>
    public required string DisplayName { get; init; }

    /// <summary>One flavor line.</summary>
    public required string Flavor { get; init; }
}

/// <summary>The ability-text.json document: one row per player kit ability.</summary>
public sealed record AbilityTextDef
{
    /// <summary>The rows, one per player kit ability.</summary>
    public required ValueList<AbilityTextRow> Abilities { get; init; }
}

/// <summary>
/// The loaded ability text by id (<see cref="AbilityTextLoader"/>). Render data only: the sim never reads it.
/// </summary>
public sealed class AbilityTextTable
{
    /// <summary>The most words a display name may have (two to three words, Matt 2026-10-22).</summary>
    public const int MaxWords = 3;

    private readonly Dictionary<string, AbilityTextRow> _rows;

    /// <summary>Builds the table from its rows (ids must be unique; the loader checks it first).</summary>
    /// <param name="rows">The rows.</param>
    public AbilityTextTable(IEnumerable<AbilityTextRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
    }

    /// <summary>Every row by ability id.</summary>
    public IReadOnlyDictionary<string, AbilityTextRow> Rows => _rows;

    /// <summary>The row for an ability, or null.</summary>
    /// <param name="abilityId">The ability id.</param>
    /// <returns>The row, or null when the ability has none (an enemy cast).</returns>
    public AbilityTextRow? For(string abilityId) => _rows.TryGetValue(abilityId, out var row) ? row : null;

    /// <summary>The display name for an ability, else <paramref name="fallback"/>.</summary>
    /// <param name="abilityId">The ability id.</param>
    /// <param name="fallback">The name to use when the ability has no row.</param>
    /// <returns>The name.</returns>
    public string NameOr(string abilityId, string fallback) => For(abilityId)?.DisplayName ?? fallback;

    /// <summary>The initials of a display name ("Sundering Verdict" is "SV"), the short tag a narrow slot draws.</summary>
    /// <param name="displayName">A display name.</param>
    /// <returns>Upper-case initials, one per word.</returns>
    public static string Initials(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        return string.Concat(Words(displayName).Select(w => char.ToUpperInvariant(w[0])));
    }

    /// <summary>A display name's words (split on spaces).</summary>
    /// <param name="displayName">A display name.</param>
    /// <returns>The words.</returns>
    public static string[] Words(string displayName) =>
        displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
