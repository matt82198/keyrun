using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// The ordered content rule list <see cref="ContentValidator.Validate"/> runs, one line per rule, no reflection. The
/// order is the old fail-fast call order, so the first violation (and its message) is unchanged.
/// </summary>
public static class ContentRules
{
    /// <summary>Every content rule, in validation order.</summary>
    public static IReadOnlyList<IContentRule> All { get; } = System.Array.AsReadOnly(new IContentRule[]
    {
        new AbilitiesRule(),
        new ClassesRule(),
        new CombosRule(),
        new ComboRidersRule(),
        new EnemiesRule(),
        new DungeonsRule(),
        new FacetsRule(),
        new RunConfigRule(),
        new HeatOverheatRule(),
        new ClassArmorRule(),
        new PairMarksRule(),
        new StunImmuneRule(),
        new MeleeAbilitiesRule(),
        new ForcesRule(),
        new LayoutRule(),
        new BranchTotalsRule(),
        new SkipBarRule(),
        new GearRule(),
        new TreesRule(),
    });
}
