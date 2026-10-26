using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The owned tree node ids the run skipped because the sim cannot run their delta (R-TREES-10b), in node order. Empty
    /// when every owned node was applied.
    /// </summary>
    public IReadOnlyList<string> SkippedTreeNodes { get; init; } = Array.Empty<string>();

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
    /// <param name="nodeEdits">
    /// The tree node edits by node id (ABILITY-TREES s5.2, R-TREES-8), or null or empty for no tree edits. Each unlocked
    /// node of the seat's <see cref="Progression.Trees"/> is composed into its ability through <see cref="TreeComposer.ToGearPatch"/>
    /// after the talent loadout and before the Facet patches.
    /// </param>
    /// <returns>The options and the gear they carry.</returns>
    public static RunStart Of(
        QueueRequest? request,
        Progression? progression,
        int humanActorId,
        IReadOnlyDictionary<string, AbilityDef> baseAbilities,
        IReadOnlyDictionary<string, GearPatch> patchesByFacetId,
        IReadOnlyDictionary<string, TreeNodeEdit>? nodeEdits = null)
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
        // ABILITY-TREES s5.2: the unlocked tree nodes patch the kit first; a Facet then stacks on the tree-patched ability.
        var skipped = new List<string>();
        var treePatched = TreePatches(progression, kit, nodeEdits, skipped);
        if (treePatched.Count > 0)
        {
            var merged = new Dictionary<string, AbilityDef>(kit, StringComparer.Ordinal);
            foreach (var kv in treePatched)
            {
                merged[kv.Key] = kv.Value;
            }
            kit = merged;
        }
        var facetGear = GearLoadout.Of(progression, humanActorId, kit, patchesByFacetId);
        // The facet loadout's abilities were patched from the tree-patched kit, so they supersede the tree-only entries.
        var abilities = new Dictionary<string, AbilityDef>(treePatched, StringComparer.Ordinal);
        foreach (var kv in facetGear.Abilities)
        {
            abilities[kv.Key] = kv.Value;
        }
        var gear = new GearLoadout(humanActorId, facetGear.Gear, abilities, facetGear.UnappliedFacetIds);
        return new RunStart(gear.ToRunOptions(options), gear) { SkippedTreeNodes = skipped };
    }

    // The abilities of the kit that an unlocked tree node changed, by kit key, each composed from the seat's nodes. The
    // seat's nodes are the union of every class's tree state: a node id is prefixed by its ability id, so only the ability
    // it names is changed. An ability no owned node edits is left out, so a seat with no unlocks yields an empty map.
    // R-TREES-10b: an owned node the sim cannot run (TreeComposer refuses it) is skipped, not thrown, and its id is added to
    // skipped so the run start reports it (RunStart.SkippedTreeNodes). TreeComposer itself still throws for direct callers.
    private static Dictionary<string, AbilityDef> TreePatches(
        Progression progression,
        IReadOnlyDictionary<string, AbilityDef> kit,
        IReadOnlyDictionary<string, TreeNodeEdit>? nodeEdits,
        List<string> skipped)
    {
        var changed = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        if (nodeEdits is null || nodeEdits.Count == 0 || progression.Trees.Count == 0)
        {
            return changed;
        }
        var nodes = progression.Trees.Values
            .SelectMany(t => t.Nodes)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (nodes.Count == 0)
        {
            return changed;
        }
        var refused = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in kit.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var ability = kit[key];
            var runnable = new List<string>();
            foreach (var node in nodes)
            {
                try
                {
                    TreeComposer.ToGearPatch(new TreeState(0, 0, new[] { node }), ability, nodeEdits);
                    runnable.Add(node);
                }
                catch (TreeCompositionException)
                {
                    if (refused.Add(node))
                    {
                        skipped.Add(node);
                    }
                }
            }
            var patch = TreeComposer.ToGearPatch(new TreeState(0, 0, runnable), ability, nodeEdits);
            if (patch.Effects is null && patch.EffectsAppend is null)
            {
                continue;
            }
            changed[key] = patch.ApplyTo(ability);
        }
        return changed;
    }
}
