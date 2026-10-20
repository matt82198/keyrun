using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>One affix's scaling, basis points on top of 100% (10000 = 100%).</summary>
/// <param name="HealthBp">Extra health, bp (2000 = +20%).</param>
/// <param name="DamageBp">Extra damage dealt, bp (3000 = +30%).</param>
public sealed record AffixScale(int HealthBp, int DamageBp);

/// <summary>
/// The key's affix rules (BIBLE s8, Matt 2026-10-20: "follow current WoW"): which keys turn Fortified or Tyrannical on
/// and what each does. The values are content data (game/content/data/run.json <c>affixes</c>), never constants here:
/// the shipped numbers are a lane's WoW-derived values pending Matt.
/// </summary>
/// <param name="Fortified">Non-boss enemies' scaling.</param>
/// <param name="Tyrannical">The boss's and its adds' scaling.</param>
/// <param name="OneAffixKey">The first key with one affix on (rotating), 5 in the bible.</param>
/// <param name="BothAffixKey">The first key with both on, 10 in the bible.</param>
public sealed record AffixTable(AffixScale Fortified, AffixScale Tyrannical, int OneAffixKey, int BothAffixKey);

/// <summary>Which affixes a key carries.</summary>
/// <param name="Fortified">Fortified is on.</param>
/// <param name="Tyrannical">Tyrannical is on.</param>
public sealed record KeyAffixes(bool Fortified, bool Tyrannical)
{
    /// <summary>Neither affix (keys below <see cref="AffixTable.OneAffixKey"/>).</summary>
    public static readonly KeyAffixes None = new(false, false);

    /// <summary>The on affixes' names in display order ("Fortified", "Tyrannical"), empty for none.</summary>
    public IReadOnlyList<string> Names =>
        new[] { (Fortified, "Fortified"), (Tyrannical, "Tyrannical") }.Where(a => a.Item1).Select(a => a.Item2).ToList();
}

/// <summary>
/// FORCES-ROUTE-PLAN M5 FORTIFIED-TYRANNICAL (BIBLE s8): the key's affixes and the health and damage they add. Keys
/// below <see cref="AffixTable.OneAffixKey"/> get none; up to <see cref="AffixTable.BothAffixKey"/> exactly one, by the
/// parity of the host's rotation index (even Fortified, odd Tyrannical); from there both. Fortified scales non-boss
/// enemies; Tyrannical scales the boss and its adds (the Twins). Integer bp math, one truncating division per scale,
/// applied on top of Tempo (the key's own health curve).
/// </summary>
public static class Affixes
{
    private const int BpDenom = 10000;

    /// <summary>The affixes for <paramref name="keyLevel"/> under <paramref name="rotationIndex"/>.</summary>
    /// <param name="table">The affix rules (content).</param>
    /// <param name="keyLevel">The key level.</param>
    /// <param name="rotationIndex">The host's rotation index (the ISO week); any integer, only its parity is read.</param>
    /// <returns>The key's affixes.</returns>
    public static KeyAffixes AffixesFor(AffixTable table, int keyLevel, int rotationIndex)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (keyLevel >= table.BothAffixKey)
        {
            return new KeyAffixes(true, true);
        }
        if (keyLevel < table.OneAffixKey)
        {
            return KeyAffixes.None;
        }
        var fortified = (rotationIndex & 1) == 0;
        return new KeyAffixes(fortified, !fortified);
    }

    /// <summary>
    /// The rotation index a run seeded with <paramref name="seed"/> carries (finding f-319aced5): the seed's low 31 bits,
    /// so the index is non-negative, a replay with the same seed rebuilds the same affixes and Clause, and consecutive
    /// seeds alternate Fortified and Tyrannical on keys 5-9 the way WoW's weeks do. Pure integer math, no clock.
    /// The game host no longer uses it: it takes the ISO week of the run's start date (Keyrun.Host.AffixWeek, outside
    /// the sim) and records that index, so every run in one week shares its affixes.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <returns>The rotation index for <see cref="AffixesFor"/> and the key-set Clause.</returns>
    public static int RotationIndexFor(long seed) => (int)(seed & int.MaxValue);

    /// <summary>
    /// The health multiplier, bp, for one enemy: 10000 plus Fortified's health on a non-boss enemy or Tyrannical's on
    /// the boss side, when that affix is on.
    /// </summary>
    /// <param name="table">The affix rules.</param>
    /// <param name="affixes">The key's affixes.</param>
    /// <param name="bossSide">True for the boss and its adds.</param>
    /// <returns>The multiplier, bp.</returns>
    public static int HealthBp(AffixTable table, KeyAffixes affixes, bool bossSide) =>
        BpDenom + (Applies(affixes, bossSide) ? Scale(table, bossSide).HealthBp : 0);

    /// <summary>The damage multiplier, bp, for one enemy's hits (as <see cref="HealthBp"/>, from the damage values).</summary>
    /// <param name="table">The affix rules.</param>
    /// <param name="affixes">The key's affixes.</param>
    /// <param name="bossSide">True for the boss and its adds.</param>
    /// <returns>The multiplier, bp.</returns>
    public static int DamageBp(AffixTable table, KeyAffixes affixes, bool bossSide) =>
        BpDenom + (Applies(affixes, bossSide) ? Scale(table, bossSide).DamageBp : 0);

    /// <summary><c>trunc(health * bp / 10000)</c>.</summary>
    /// <param name="health">Health (already Tempo-scaled).</param>
    /// <param name="bp">The multiplier from <see cref="HealthBp"/>.</param>
    /// <returns>The scaled health.</returns>
    public static long ScaleHealth(long health, int bp) => IntMath.TruncDiv(health * bp, BpDenom);

    /// <summary>
    /// <paramref name="ability"/> with every <see cref="DamageOp"/> amount (its ops and its verse variants) scaled by
    /// <paramref name="bp"/>; the same instance when <paramref name="bp"/> is 10000.
    /// </summary>
    /// <param name="ability">An enemy ability.</param>
    /// <param name="bp">The multiplier from <see cref="DamageBp"/>.</param>
    /// <returns>The scaled ability.</returns>
    public static AbilityDef ScaleDamage(AbilityDef ability, int bp)
    {
        ArgumentNullException.ThrowIfNull(ability);
        if (bp == BpDenom)
        {
            return ability;
        }
        return ability with
        {
            Effects = ScaleOps(ability.Effects, bp),
            Variants = ability.Variants?.ToDictionary(kv => kv.Key, kv => ScaleOps(kv.Value, bp), StringComparer.Ordinal),
        };
    }

    private static IReadOnlyList<EffectOp> ScaleOps(IReadOnlyList<EffectOp> ops, int bp) =>
        ops.Select(op => op is DamageOp d ? d with { Amount = IntMath.TruncDiv(d.Amount * bp, BpDenom) } : op).ToList();

    private static bool Applies(KeyAffixes affixes, bool bossSide)
    {
        ArgumentNullException.ThrowIfNull(affixes);
        return bossSide ? affixes.Tyrannical : affixes.Fortified;
    }

    private static AffixScale Scale(AffixTable table, bool bossSide)
    {
        ArgumentNullException.ThrowIfNull(table);
        return bossSide ? table.Tyrannical : table.Fortified;
    }
}
