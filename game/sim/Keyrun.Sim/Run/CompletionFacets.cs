using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The class facet pool a completion roll draws over (R-ROLL-1 (b)(iii); design/PROGRESSION-PIVOT.md s2-s3): per class, the
/// class's own facets per gear slot, ordered by id. A host with content loaded fills <see cref="CompletionRollTable.ClassFacets"/>
/// from the registry; a run with no content (the fixture runs) draws over <see cref="Fallback"/>, the Warden's shipped table
/// (content/data/facets/warden.json, embedded from the same file, so the two cannot drift).
/// </summary>
public static class CompletionFacets
{
    private const string FallbackResource = "Keyrun.Sim.DefaultFacets.warden.json";

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<Facet>>> FallbackPool = new(LoadFallback);

    /// <summary>The Warden's facets per gear slot, ordered by id: the pool for a class with no entry in the table.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Facet>> Fallback => FallbackPool.Value;

    /// <summary>The pool for <paramref name="classId"/>: its table entry, else <see cref="Fallback"/>.</summary>
    /// <param name="table">The completion table, or null for <see cref="CompletionRollTable.Default"/>.</param>
    /// <param name="classId">The seat's class id.</param>
    /// <returns>Slot to facets.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<Facet>> For(CompletionRollTable? table, string classId) =>
        table?.ClassFacets is { } byClass && byClass.TryGetValue(classId, out var pool) ? pool : Fallback;

    private static IReadOnlyDictionary<string, IReadOnlyList<Facet>> LoadFallback()
    {
        using var stream = typeof(CompletionFacets).Assembly.GetManifestResourceStream(FallbackResource)
            ?? throw new InvalidOperationException("embedded resource missing: " + FallbackResource);
        using var doc = JsonDocument.Parse(stream);
        var facets = doc.RootElement.EnumerateArray()
            .Select(e => (Slot: e.GetProperty("slot").GetString()!, Facet: new Facet(e.GetProperty("id").GetString()!, e.GetProperty("text").GetString()!, e.GetProperty("abilityId").GetString()!)))
            .ToList();
        return Rewards.GearSlots.ToDictionary(
            s => s,
            s => (IReadOnlyList<Facet>)facets.Where(f => f.Slot == s).Select(f => f.Facet).OrderBy(f => f.Id, StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal);
    }
}
