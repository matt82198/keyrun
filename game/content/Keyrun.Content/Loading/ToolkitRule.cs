using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>One toolkit gap: a class button or an encounter threat the contract (design/TOOLKIT-CONTRACT.md) wants and content lacks.</summary>
/// <param name="Subject">"class" or "encounter".</param>
/// <param name="Id">The class id, or the encounter id (a pull id or a boss enemy id).</param>
/// <param name="Code">The missing toolkit part.</param>
/// <param name="Detail">One line saying what content has instead.</param>
public sealed record ToolkitGap(string Subject, string Id, string Code, string Detail);

/// <summary>
/// STUB (red step): the toolkit rule of design/TOOLKIT-CONTRACT.md s2 and s3, section 5 lane L1. Reports gaps; never
/// throws and is not in <see cref="ContentRules.All"/> (a report, not a gate, until the gaps are closed).
/// </summary>
public static class ToolkitRule
{
    /// <summary>Class gaps over <paramref name="reg"/>.</summary>
    public static IReadOnlyList<ToolkitGap> ClassGaps(ContentRegistry reg) => new List<ToolkitGap>();

    /// <summary>Encounter ids.</summary>
    public static IReadOnlyList<string> EncounterIds(ContentRegistry reg) => new List<string>();

    /// <summary>Enemy ids owning an encounter's script.</summary>
    public static IReadOnlyList<string> ScriptOwners(ContentRegistry reg, string id) => new List<string>();

    /// <summary>Renders a report.</summary>
    public static string Render(IReadOnlyList<ToolkitGap> gaps, System.Func<string, string> fileOf) => "";

    /// <summary>Encounter gaps over <paramref name="reg"/>.</summary>
    public static IReadOnlyList<ToolkitGap> EncounterGaps(ContentRegistry reg) => new List<ToolkitGap>();
}
