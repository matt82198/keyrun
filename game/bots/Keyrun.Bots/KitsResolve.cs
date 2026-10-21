using System;
using System.Collections.Generic;
using Keyrun.Content;

namespace Keyrun.Bots;

/// <summary>
/// bots/src/kit-resolve.ts EffectiveKit: the superset of ClassKit fields the roles read, every field safe for a
/// scripted view whose classId content does not define. Built by <see cref="KitRegistry.ResolveKit"/>.
/// </summary>
public sealed record EffectiveKit
{
    private static readonly IReadOnlyList<string> NoIds = Array.Empty<string>();
    private static readonly IReadOnlyDictionary<string, string> NoWindows = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, long> NoAmounts = new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>Rotation in priority order.</summary>
    public required IReadOnlyList<string> Rotation { get; init; }

    /// <summary>Interrupt, or null.</summary>
    public required string? InterruptAbilityId { get; init; }

    /// <summary>Defensive, or null.</summary>
    public required string? DefensiveAbilityId { get; init; }

    /// <summary>Heal-triage ability, or null.</summary>
    public required string? HealAbilityId { get; init; }

    /// <summary>Self-sustain ability, or null.</summary>
    public required string? SelfSustainAbilityId { get; init; }

    /// <summary>Stance swap, or null.</summary>
    public required string? StanceSwapAbilityId { get; init; }

    /// <summary>Dispel, or null.</summary>
    public required string? DispelAbilityId { get; init; }

    /// <summary>Setup abilities.</summary>
    public required IReadOnlyList<string> SetupAbilityIds { get; init; }

    /// <summary>Payoff abilities.</summary>
    public required IReadOnlyList<string> PayoffAbilityIds { get; init; }

    /// <summary>Setup ability to windowId.</summary>
    public required IReadOnlyDictionary<string, string> WindowIdBySetupAbility { get; init; }

    /// <summary>Ability to cost.</summary>
    public required IReadOnlyDictionary<string, long> CostByAbility { get; init; }

    /// <summary>Ability to net resource delta.</summary>
    public required IReadOnlyDictionary<string, long> ResourceGainByAbility { get; init; }

    /// <summary>Resource dumps.</summary>
    public required IReadOnlyList<string> ResourceDumpAbilityIds { get; init; }

    /// <summary>Resource builders.</summary>
    public required IReadOnlyList<string> ResourceBuilderAbilityIds { get; init; }

    /// <summary>Shape-aware heals.</summary>
    public required HealKit Heals { get; init; }

    /// <summary>Taunt, or null.</summary>
    public required string? TauntAbilityId { get; init; }

    /// <summary>Loop kind, or null for a fallback kit.</summary>
    public required LoopKind? LoopKind { get; init; }

    /// <summary>Loop resource, or null for a fallback kit.</summary>
    public required string? Resource { get; init; }

    /// <summary>Loop cap, or null for a fallback kit.</summary>
    public required long? ResourceMax { get; init; }

    /// <summary>Slotted abilities whose own gcdTicks is 0 (off the GCD); empty for a fallback kit.</summary>
    public IReadOnlyList<string> OffGcdAbilityIds { get; init; } = NoIds;

    /// <summary>
    /// Slotted abilities with more than one charge, to their charges and cooldown (BANKED-CHARGE, R-PILE-7c); empty for a
    /// fallback kit.
    /// </summary>
    public IReadOnlyDictionary<string, AbilityCharges> ChargedAbilities { get; init; } = Kits.NoCharges;

    /// <summary>
    /// ALLY-VIGIL (ruling R-FORKPARITY-UNPLAYED): the self-sustain spender when the run's own def of it is
    /// target-shaped and tagged <see cref="KitRegistry.AllyShieldTag"/> (Warden's Vigil with the "ally" talent), else
    /// null. When set, <see cref="SelfSustainAbilityId"/> is null: the sim refuses a target-shaped cast with no target,
    /// so the spender is cast only on a pressured ally (<see cref="Roles.TankPressure.AllyShieldCommand"/>).
    /// </summary>
    public string? AllyShieldAbilityId { get; init; }

    /// <summary>The content-backed branch of kit-resolve.ts resolveKit.</summary>
    /// <param name="kit">Content kit.</param>
    /// <returns>The effective kit.</returns>
    public static EffectiveKit FromClassKit(ClassKit kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        return new EffectiveKit
        {
            Rotation = kit.Rotation,
            InterruptAbilityId = kit.InterruptAbilityId,
            DefensiveAbilityId = kit.DefensiveAbilityId,
            HealAbilityId = kit.HealAbilityId,
            SelfSustainAbilityId = kit.SelfSustainAbilityId,
            StanceSwapAbilityId = kit.StanceSwapAbilityId,
            DispelAbilityId = kit.DispelAbilityId,
            SetupAbilityIds = kit.SetupAbilityIds,
            PayoffAbilityIds = kit.PayoffAbilityIds,
            WindowIdBySetupAbility = kit.WindowIdBySetupAbility,
            CostByAbility = kit.CostByAbility,
            ResourceGainByAbility = kit.ResourceGainByAbility,
            ResourceDumpAbilityIds = kit.ResourceDumpAbilityIds,
            ResourceBuilderAbilityIds = kit.ResourceBuilderAbilityIds,
            LoopKind = kit.LoopKind,
            Resource = kit.Resource,
            ResourceMax = kit.ResourceMax,
            Heals = kit.Heals,
            TauntAbilityId = kit.TauntAbilityId,
            OffGcdAbilityIds = kit.OffGcdAbilityIds,
            ChargedAbilities = kit.ChargedAbilities,
        };
    }

    /// <summary>The scripted-view branch of kit-resolve.ts resolveKit: the view's fields, everything else empty.</summary>
    /// <param name="fallback">The view's classKit.</param>
    /// <returns>The effective kit.</returns>
    public static EffectiveKit FromFallback(ClassKitView fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return new EffectiveKit
        {
            Rotation = fallback.Rotation,
            InterruptAbilityId = fallback.InterruptAbilityId,
            DefensiveAbilityId = fallback.DefensiveAbilityId,
            HealAbilityId = fallback.HealAbilityId,
            SelfSustainAbilityId = null,
            StanceSwapAbilityId = null,
            DispelAbilityId = null,
            SetupAbilityIds = NoIds,
            PayoffAbilityIds = NoIds,
            WindowIdBySetupAbility = NoWindows,
            CostByAbility = NoAmounts,
            ResourceGainByAbility = NoAmounts,
            ResourceDumpAbilityIds = NoIds,
            ResourceBuilderAbilityIds = NoIds,
            LoopKind = null,
            Resource = null,
            ResourceMax = null,
            Heals = HealKit.None with { TargetHealAbilityId = fallback.HealAbilityId },
            TauntAbilityId = null,
        };
    }
}
