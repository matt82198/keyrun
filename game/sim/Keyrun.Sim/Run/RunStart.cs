using System;
using System.Collections.Generic;
using Keyrun.Sim.Combat;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The options the host creates a run with (finding f-f9b80112, BIBLE s7): the queued request's Clause and talent
/// loadouts, plus the human's equipped gear from the screen's <see cref="Progression"/> (every slot, keystone included).
/// game/src/Host/SimDriver.cs StartRun passes <see cref="Options"/> to <see cref="RunRoot.CreateRun"/>; this is the pure
/// half of that call so the composition is tested without the engine.
/// </summary>
/// <param name="Options">The run options, or null for a run with no request and no gear (created exactly as before).</param>
/// <param name="Gear">The human's gear loadout, or null when no progression was supplied (no screen yet).</param>
public sealed record RunStart(RunOptions? Options, GearLoadout? Gear)
{
    /// <summary>
    /// Composes the run options. The wearer's kit is <paramref name="baseAbilities"/> overlaid by the request's talent
    /// loadout for <paramref name="humanActorId"/>, so a Facet stacks on the talented ability rather than replacing it;
    /// <see cref="GearLoadout.ToRunOptions"/> then merges the patched kit into the request's options (Clause and every
    /// other seat kept).
    /// </summary>
    /// <param name="request">The queued request, or null (CLI, --autorun, --solo start).</param>
    /// <param name="progression">The screen's progression, or null when there is no screen.</param>
    /// <param name="humanActorId">The human's actor id (the gear wearer).</param>
    /// <param name="baseAbilities">The run content's base ability map.</param>
    /// <param name="patchesByFacetId">The wearer class's Facet patches by Facet id.</param>
    /// <returns>The options and the gear they carry.</returns>
    public static RunStart Of(
        QueueRequest? request,
        Progression? progression,
        int humanActorId,
        IReadOnlyDictionary<string, AbilityDef> baseAbilities,
        IReadOnlyDictionary<string, GearPatch> patchesByFacetId)
    {
        ArgumentNullException.ThrowIfNull(baseAbilities);
        ArgumentNullException.ThrowIfNull(patchesByFacetId);
        var options = request?.ToRunOptions();
        if (progression is null)
        {
            return new RunStart(options, null);
        }
        var kit = baseAbilities;
        if (options?.Loadouts is { } loadouts && loadouts.TryGetValue(humanActorId, out var talented) && talented.Count > 0)
        {
            var merged = new Dictionary<string, AbilityDef>(baseAbilities, StringComparer.Ordinal);
            foreach (var kv in talented)
            {
                merged[kv.Key] = kv.Value;
            }
            kit = merged;
        }
        var gear = GearLoadout.Of(progression, humanActorId, kit, patchesByFacetId);
        return new RunStart(gear.ToRunOptions(options), gear);
    }
}
