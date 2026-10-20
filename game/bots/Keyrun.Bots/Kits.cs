using System;
using System.Collections.Generic;
using Keyrun.Content;

namespace Keyrun.Bots;

/// <summary>
/// bots/src/kits.ts ClassKit: the bot-facing, decision-ready view of one class, derived entirely from content
/// (ClassDef slots, ability tags, costs, resource ops and shapes). Kits never invent ability data.
/// </summary>
public sealed record ClassKit
{
    /// <summary>Class id.</summary>
    public required string ClassId { get; init; }

    /// <summary>Resource loop kind.</summary>
    public required LoopKind LoopKind { get; init; }

    /// <summary>Loop resource name.</summary>
    public required string Resource { get; init; }

    /// <summary>Loop resource cap.</summary>
    public required long ResourceMax { get; init; }

    /// <summary>The five rotational abilities in base priority order (<see cref="KitPriority.BuildRotationPriority"/>).</summary>
    public required IReadOnlyList<string> Rotation { get; init; }

    /// <summary>Rotational abilities tagged "setup:&lt;windowId&gt;", slot order.</summary>
    public required IReadOnlyList<string> SetupAbilityIds { get; init; }

    /// <summary>Rotational abilities tagged "payoff", slot order.</summary>
    public required IReadOnlyList<string> PayoffAbilityIds { get; init; }

    /// <summary>Setup ability id to the windowId it opens.</summary>
    public required IReadOnlyDictionary<string, string> WindowIdBySetupAbility { get; init; }

    /// <summary>Rotational abilities with a positive resource delta (builders).</summary>
    public required IReadOnlyList<string> ResourceBuilderAbilityIds { get; init; }

    /// <summary>Rotational Payoffs with a negative resource delta (controlled dumps, e.g. Cold Snap).</summary>
    public required IReadOnlyList<string> ResourceDumpAbilityIds { get; init; }

    /// <summary>Rotational ability id to its cost in the loop resource (0 if free).</summary>
    public required IReadOnlyDictionary<string, long> CostByAbility { get; init; }

    /// <summary>Rotational ability id to its net explicit resource delta.</summary>
    public required IReadOnlyDictionary<string, long> ResourceGainByAbility { get; init; }

    /// <summary>Interrupt slot.</summary>
    public required string InterruptAbilityId { get; init; }

    /// <summary>Defensive slot.</summary>
    public required string DefensiveAbilityId { get; init; }

    /// <summary>Utility slot.</summary>
    public required string UtilityAbilityId { get; init; }

    /// <summary>The utility ability when it is tagged "dispel", else null.</summary>
    public required string? DispelAbilityId { get; init; }

    /// <summary>Movement slot.</summary>
    public required string MovementAbilityId { get; init; }

    /// <summary>First rotational ability whose effects (or any variant's) heal; null for non-healers.</summary>
    public required string? HealAbilityId { get; init; }

    /// <summary>First neutral, self-shaped, resource-spending rotational ability (Warden's Vigil), or null.</summary>
    public required string? SelfSustainAbilityId { get; init; }

    /// <summary>Stance loops only: the first neutral rotational ability with a positive delta (Refrain), or null.</summary>
    public required string? StanceSwapAbilityId { get; init; }

    /// <summary>Shape-aware heals.</summary>
    public required HealKit Heals { get; init; }

    /// <summary>The threat-op utility (Warden's Taunt), or null.</summary>
    public required string? TauntAbilityId { get; init; }

    /// <summary>
    /// Every slotted ability (five rotational, then interrupt, defensive, utility, movement) whose own gcdTicks is 0:
    /// off the GCD, so readiness never waits for it (finding f-80b5cf16, the sim's Cooldowns.IsAbilityReady).
    /// </summary>
    public IReadOnlyList<string> OffGcdAbilityIds { get; init; } = Array.Empty<string>();
}

/// <summary>Port of bots/src/kits.ts buildClassKit and its constants.</summary>
public static class Kits
{
    /// <summary>kits.ts PRIORITY_INTERRUPT_TAG: an enemy ability tagged "healer" must be kicked first (the Choir).</summary>
    public const string PriorityInterruptTag = "healer";

    private const string DispelTag = "dispel";

    /// <summary>
    /// kits.ts buildClassKit: the ClassKit of <paramref name="cls"/> from the ability and effect registries.
    /// </summary>
    /// <param name="cls">Class definition.</param>
    /// <param name="abilities">Ability registry.</param>
    /// <param name="effects">Effect registry (periodic heals), or null.</param>
    /// <returns>The kit.</returns>
    /// <exception cref="InvalidOperationException">A rotation slot names an unknown ability (the TS throws).</exception>
    public static ClassKit BuildClassKit(ClassDef cls, IReadOnlyDictionary<string, AbilityDef> abilities, IReadOnlyDictionary<string, EffectDef>? effects = null)
    {
        ArgumentNullException.ThrowIfNull(cls);
        ArgumentNullException.ThrowIfNull(abilities);
        AbilityDef Get(string id) =>
            abilities.TryGetValue(id, out var def) ? def : throw new InvalidOperationException($"kits: class \"{cls.Id}\" references unknown ability \"{id}\"");

        var slots = cls.Abilities;
        var rotationalDefs = new[] { Get(slots.Rot1), Get(slots.Rot2), Get(slots.Rot3), Get(slots.Rot4), Get(slots.Rot5) };
        var resource = cls.Loop.Resource;

        var setupAbilityIds = new List<string>();
        var payoffAbilityIds = new List<string>();
        var windowIdBySetupAbility = new Dictionary<string, string>(StringComparer.Ordinal);
        var resourceBuilderAbilityIds = new List<string>();
        var resourceDumpAbilityIds = new List<string>();
        var costByAbility = new Dictionary<string, long>(StringComparer.Ordinal);
        var resourceGainByAbility = new Dictionary<string, long>(StringComparer.Ordinal);
        string? healAbilityId = null;
        string? selfSustainAbilityId = null;
        string? stanceSwapAbilityId = null;

        foreach (var def in rotationalDefs)
        {
            var isPayoff = KitPriority.HasTag(def, KitPriority.PayoffTag);
            var setupTag = KitPriority.TagWithPrefix(def, KitPriority.SetupTagPrefix);
            if (setupTag is not null)
            {
                setupAbilityIds.Add(def.Id);
                windowIdBySetupAbility[def.Id] = setupTag.Substring(KitPriority.SetupTagPrefix.Length);
            }
            if (isPayoff)
            {
                payoffAbilityIds.Add(def.Id);
            }

            var gain = KitResources.ResourceEffectDelta(def);
            resourceGainByAbility[def.Id] = gain;
            var cost = KitResources.CostAmount(def, resource);
            costByAbility[def.Id] = cost;
            if (gain > 0)
            {
                resourceBuilderAbilityIds.Add(def.Id);
            }
            if (gain < 0 && isPayoff)
            {
                resourceDumpAbilityIds.Add(def.Id);
            }

            if (healAbilityId is null && KitResources.HealsOnCast(def))
            {
                healAbilityId = def.Id;
            }
            if (selfSustainAbilityId is null && setupTag is null && !isPayoff && def.Shape.Kind == ShapeKind.Self && cost > 0)
            {
                selfSustainAbilityId = def.Id;
            }
            if (cls.Loop.Kind == LoopKind.Stance && stanceSwapAbilityId is null && setupTag is null && !isPayoff && gain > 0)
            {
                stanceSwapAbilityId = def.Id;
            }
        }

        abilities.TryGetValue(slots.Utility, out var utilityDef);
        var dispelId = utilityDef is not null && KitPriority.HasTag(utilityDef, DispelTag) ? slots.Utility : null;

        return new ClassKit
        {
            ClassId = cls.Id,
            LoopKind = cls.Loop.Kind,
            Resource = resource,
            ResourceMax = cls.Loop.Max,
            Rotation = KitPriority.BuildRotationPriority(rotationalDefs, resource),
            SetupAbilityIds = setupAbilityIds,
            PayoffAbilityIds = payoffAbilityIds,
            WindowIdBySetupAbility = windowIdBySetupAbility,
            ResourceBuilderAbilityIds = resourceBuilderAbilityIds,
            ResourceDumpAbilityIds = resourceDumpAbilityIds,
            CostByAbility = costByAbility,
            ResourceGainByAbility = resourceGainByAbility,
            InterruptAbilityId = slots.Interrupt,
            DefensiveAbilityId = slots.Defensive,
            UtilityAbilityId = slots.Utility,
            DispelAbilityId = dispelId,
            MovementAbilityId = slots.Movement,
            HealAbilityId = healAbilityId,
            SelfSustainAbilityId = selfSustainAbilityId,
            StanceSwapAbilityId = stanceSwapAbilityId,
            Heals = KitHeals.ClassifyHeals(rotationalDefs, effects),
            TauntAbilityId = KitHeals.TauntAbilityId(utilityDef),
            OffGcdAbilityIds = OffGcdIds(slots, abilities),
        };
    }

    private static List<string> OffGcdIds(ClassAbilitySlots slots, IReadOnlyDictionary<string, AbilityDef> abilities)
    {
        var ids = new List<string>();
        foreach (var id in new[] { slots.Rot1, slots.Rot2, slots.Rot3, slots.Rot4, slots.Rot5, slots.Interrupt, slots.Defensive, slots.Utility, slots.Movement })
        {
            if (abilities.TryGetValue(id, out var def) && def.GcdTicks == 0 && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }
}
