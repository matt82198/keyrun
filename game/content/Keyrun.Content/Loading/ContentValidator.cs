using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// Cross-reference and shape invariants, ported from content/validate.ts. Runs inside
/// <see cref="ContentLoader.Load(IContentSource)"/>; the first violation throws
/// <see cref="ContentLoadException"/>.
/// <para>
/// Ported: effect refs from ability effects/variants and combo effects and break-bar payloads;
/// class slot and talent-fork ability refs; combo setup/payoff class refs and setup != payoff;
/// enemy ability and script refs; dungeon enemy refs; facet ability refs. The 5-rotational-slot
/// shape and the "patch may not carry id" rule are structural here (typed slots; no <c>id</c>
/// member on <see cref="AbilityPatch"/>/<see cref="FacetPatch"/>, so the load fails first).
/// </para>
/// <para>
/// Added beyond validate.ts: effect refs inside talent and facet patches, <c>breakBarId</c>
/// must name a declared break bar, and a Window op on an ability or talent must carry its
/// <c>setupClass</c> (the sim union requires it; only facet <c>effectsAppend</c> may omit it).
/// </para>
/// <para>
/// Not ported: sim/combat/abilities.ts <c>validateAbilityDef</c> (the sim's own ability checks);
/// it belongs with the sim port (lanes 1C/2A) and should be called from here once it exists.
/// </para>
/// </summary>
public static class ContentValidator
{
    /// <summary>Enemy-side pseudo-class used as a combo setup row (sim/combat/combos.ts).</summary>
    public const string BossPseudoClass = "boss";

    /// <summary>Combo payoffClass meaning "any class other than setupClass".</summary>
    public const string AnyOtherClass = "*";

    /// <summary>Validates <paramref name="reg"/>.</summary>
    /// <param name="reg">A loaded registry.</param>
    /// <exception cref="ContentLoadException">The first violated invariant.</exception>
    public static void Validate(ContentRegistry reg)
    {
        System.ArgumentNullException.ThrowIfNull(reg);
        CheckAbilities(reg);
        CheckClasses(reg);
        CheckCombos(reg);
        CheckEnemies(reg);
        CheckDungeons(reg);
        CheckFacets(reg);
    }

    private static void Fail(string message) => throw new ContentLoadException("content: " + message);

    private static void CheckOps(IEnumerable<EffectOp>? ops, ContentRegistry reg, string where, bool requireSetupClass)
    {
        if (ops is null) return;
        foreach (EffectOp op in ops)
        {
            string? effectId = op switch
            {
                ApplyEffectOp apply => apply.EffectId,
                RemoveEffectOp remove => remove.EffectId,
                _ => null,
            };
            if (effectId is not null && effectId != "*" && !reg.Effects.ContainsKey(effectId))
                Fail(where + " references unknown effect \"" + effectId + "\"");
            if (requireSetupClass && op is WindowOp window && window.SetupClass is null)
                Fail(where + " window \"" + window.WindowId + "\" has no setupClass");
        }
    }

    private static void CheckAbilities(ContentRegistry reg)
    {
        foreach (AbilityDef ability in reg.Abilities.Values)
        {
            string where = "ability \"" + ability.Id + "\"";
            CheckOps(ability.Effects, reg, where, requireSetupClass: true);
            foreach (KeyValuePair<string, ValueList<EffectOp>> variant in ability.Variants ?? new ValueMap<ValueList<EffectOp>>())
                CheckOps(variant.Value, reg, where + " variant \"" + variant.Key + "\"", requireSetupClass: true);
        }
        foreach (EffectDef effect in reg.Effects.Values)
            if (effect.Periodic is not null)
                CheckOps(new[] { effect.Periodic.Op }, reg, "effect \"" + effect.Id + "\" periodic", requireSetupClass: true);
    }

    private static void CheckClasses(ContentRegistry reg)
    {
        foreach (ClassDef cls in reg.Classes.Values)
        {
            ClassAbilitySlots s = cls.Abilities;
            var slots = new (string Slot, string Id)[]
            {
                ("rot1", s.Rot1), ("rot2", s.Rot2), ("rot3", s.Rot3), ("rot4", s.Rot4), ("rot5", s.Rot5),
                ("interrupt", s.Interrupt), ("defensive", s.Defensive), ("utility", s.Utility), ("movement", s.Movement),
            };
            foreach ((string slot, string id) in slots)
                if (!reg.Abilities.ContainsKey(id))
                    Fail("class \"" + cls.Id + "\" slot \"" + slot + "\" references unknown ability \"" + id + "\"");
            foreach (TalentFork fork in cls.Talents)
            {
                if (!reg.Abilities.ContainsKey(fork.AbilityId))
                    Fail("class \"" + cls.Id + "\" talent fork references unknown ability \"" + fork.AbilityId + "\"");
                foreach (TalentOption option in fork.Options)
                {
                    string where = "class \"" + cls.Id + "\" talent \"" + option.Id + "\"";
                    CheckOps(option.Patch.Effects, reg, where, requireSetupClass: true);
                    foreach (KeyValuePair<string, ValueList<EffectOp>> variant in option.Patch.Variants ?? new ValueMap<ValueList<EffectOp>>())
                        CheckOps(variant.Value, reg, where + " variant \"" + variant.Key + "\"", requireSetupClass: true);
                }
            }
        }
    }

    private static void CheckCombos(ContentRegistry reg)
    {
        foreach (ComboEntry combo in reg.Combos.Values)
        {
            string where = "combo \"" + combo.Id + "\"";
            if (combo.SetupClass != BossPseudoClass && !reg.Classes.ContainsKey(combo.SetupClass))
                Fail(where + " references unknown setupClass \"" + combo.SetupClass + "\"");
            if (combo.PayoffClass != AnyOtherClass && !reg.Classes.ContainsKey(combo.PayoffClass))
                Fail(where + " references unknown payoffClass \"" + combo.PayoffClass + "\"");
            if (combo.PayoffClass == combo.SetupClass)
                Fail(where + " has matching setupClass/payoffClass \"" + combo.SetupClass + "\"");
            CheckOps(combo.Effects, reg, where, requireSetupClass: true);
        }
    }

    private static void CheckEnemies(ContentRegistry reg)
    {
        var bars = new HashSet<string>(reg.Enemies.Values.SelectMany(e => e.BreakBars ?? new ValueList<BreakBarDef>()).Select(b => b.Id), System.StringComparer.Ordinal);
        foreach (EnemyDef enemy in reg.Enemies.Values)
        {
            string where = "enemy \"" + enemy.Id + "\"";
            foreach (string id in enemy.Abilities)
                if (!reg.Abilities.ContainsKey(id)) Fail(where + " references unknown ability \"" + id + "\"");
            foreach (ScriptStep step in enemy.Script)
                if (!reg.Abilities.ContainsKey(step.Cast)) Fail(where + " script references unknown ability \"" + step.Cast + "\"");
            foreach (BreakBarDef bar in enemy.BreakBars ?? new ValueList<BreakBarDef>())
                CheckOps(bar.OnEmpty, reg, where + " breakBar \"" + bar.Id + "\"", requireSetupClass: true);
            if (enemy.BreakBarId is not null && !bars.Contains(enemy.BreakBarId))
                Fail(where + " references unknown breakBar \"" + enemy.BreakBarId + "\"");
        }
    }

    private static void CheckDungeons(ContentRegistry reg)
    {
        foreach (DungeonDef dungeon in reg.Dungeons.Values)
        {
            foreach (PullDef pull in dungeon.Pulls)
                foreach (PullEnemy enemy in pull.Enemies)
                    if (!reg.Enemies.ContainsKey(enemy.EnemyId))
                        Fail("dungeon \"" + dungeon.Id + "\" pull \"" + pull.Id + "\" references unknown enemy \"" + enemy.EnemyId + "\"");
            if (!reg.Enemies.ContainsKey(dungeon.Boss.EnemyId))
                Fail("dungeon \"" + dungeon.Id + "\" boss references unknown enemy \"" + dungeon.Boss.EnemyId + "\"");
        }
    }

    private static void CheckFacets(ContentRegistry reg)
    {
        foreach (FacetDef facet in reg.Facets.Values)
        {
            string where = "facet \"" + facet.Id + "\"";
            if (!reg.Abilities.ContainsKey(facet.AbilityId))
                Fail(where + " references unknown ability \"" + facet.AbilityId + "\"");
            CheckOps(facet.Patch.Effects, reg, where, requireSetupClass: true);
            CheckOps(facet.Patch.EffectsAppend, reg, where + " effectsAppend", requireSetupClass: false);
        }
    }
}
