using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Host;

public sealed partial record HostRunSetup
{
    /// <summary>The sim's Pair mark rules from content run.json <c>pairMarks</c> (BIBLE section 9.6 item 2).</summary>
    /// <param name="m">The content block (validated at load).</param>
    /// <returns>The rules.</returns>
    public static MarkRules MarkRulesOf(C.PairMarksConfig m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return new MarkRules(m.SpreadEffectId, m.SpreadRadiusMm, m.SpreadDamage, m.StackEffectId, m.StackRadiusMm, m.StackDamage);
    }

    /// <summary>The sim affix table from content run.json <c>affixes</c> (the values are data, pending Matt).</summary>
    /// <param name="a">The content block.</param>
    /// <returns>The table.</returns>
    public static AffixTable AffixTableOf(C.AffixesConfig a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new AffixTable(
            new AffixScale(a.Fortified.HealthBp, a.Fortified.DamageBp),
            new AffixScale(a.Tyrannical.HealthBp, a.Tyrannical.DamageBp),
            a.OneAffixKey,
            a.BothAffixKey);
    }

    /// <summary>
    /// The sim's Heat/Overheat rules (BIBLE section 5 Cinder): one <see cref="OverheatRule"/> per class whose loop is
    /// heat-overheat, its threshold that loop's max, its Chill the content run.json <c>heatOverheat</c> block. A class
    /// with a rule opens the run at 0 Heat and its cast at the threshold is the free Overheat (Combat/Casting.cs).
    /// </summary>
    /// <param name="registry">The loaded content (validated at load).</param>
    /// <returns>Class id to its rule.</returns>
    public static IReadOnlyDictionary<string, OverheatRule> OverheatRulesOf(C.ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var chill = registry.Run.HeatOverheat;
        return registry.Classes.Values
            .Where(c => c.Loop.Kind == C.LoopKind.HeatOverheat)
            .ToDictionary(c => c.Id, c => new OverheatRule(c.Loop.Max, chill.ChillEffectId, chill.ChillTicks), StringComparer.Ordinal);
    }

    /// <summary>
    /// Plan M5: every enemy ability's damage scaled by its side's affix. The trash side is every ability a pull enemy
    /// lists or casts; the boss side is the boss's and the Twins' (Tyrannical: "the boss and its adds"). An ability on
    /// both sides fails closed, since one shared map cannot hold two scales for it.
    /// </summary>
    private static Dictionary<string, AbilityDef> ScaleEnemyDamage(
        C.ContentRegistry registry, C.DungeonDef dungeonDef, IReadOnlyList<C.EnemyDef> bosses, Dictionary<string, AbilityDef> abilities, int trashBp, int bossBp)
    {
        static IEnumerable<string> Casts(C.EnemyDef e) => e.Abilities.Concat(e.Script.Select(s => s.Cast));
        var trash = dungeonDef.Pulls
            .SelectMany(p => p.Enemies)
            .Select(g => Enemy(registry, g.EnemyId, $"pull enemy \"{g.EnemyId}\""))
            .SelectMany(Casts)
            .ToHashSet(StringComparer.Ordinal);
        var bossSide = registry.Enemies.Values
            .Where(e => bosses.Any(boss => e.Id == boss.Id || (e.BreakBarId is { } bar && boss.BreakBars?.Any(b => b.Id == bar) == true)))
            .SelectMany(Casts)
            .ToHashSet(StringComparer.Ordinal);
        if (trash.Intersect(bossSide, StringComparer.Ordinal).FirstOrDefault() is { } shared)
        {
            throw new InvalidOperationException($"run setup: ability \"{shared}\" is cast by trash and by the boss side; affix damage needs one side");
        }
        foreach (var (ids, bp) in new[] { (trash, trashBp), (bossSide, bossBp) })
        {
            foreach (var id in ids.Where(abilities.ContainsKey))
            {
                abilities[id] = Affixes.ScaleDamage(abilities[id], bp);
            }
        }
        return abilities;
    }
}
