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
/// must name a declared break bar, a script telegraph with <c>spansCast</c> must cast an ability with castTicks &gt; 0,
/// and a Window op on an ability or talent must carry its
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
        CheckComboRiders(reg);
        CheckEnemies(reg);
        CheckDungeons(reg);
        CheckFacets(reg);
        CheckRun(reg.Run);
        CheckHeatOverheat(reg);
        CheckPairMarks(reg);
        CheckForces(reg);
        LayoutValidator.Check(reg);
    }

    /// <summary>
    /// BIBLE section 9.2 forces + route invariants: a positive bar; every value names a known enemy once, is not
    /// negative, and is earned only by a teaching-pull or pack enemy (nothing farmable); every teaching-pull and pack
    /// enemy has a value; the fork precedes the rejoin; branch and room ids are unique; packs and hazards stand in a
    /// branch room; and every branch's critical path plus all of its packs reaches the bar.
    /// </summary>
    private static void CheckForces(ContentRegistry reg)
    {
        ForcesTable forces = reg.Forces;
        if (forces.Required <= 0) Fail("forces required must be positive, got " + forces.Required);

        var valued = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (ForcesValue value in forces.Values)
        {
            if (!reg.Enemies.ContainsKey(value.EnemyId)) Fail("forces value references unknown enemy \"" + value.EnemyId + "\"");
            if (!valued.Add(value.EnemyId)) Fail("duplicate forces value for enemy \"" + value.EnemyId + "\"");
            if (value.Points < 0) Fail("forces value for enemy \"" + value.EnemyId + "\" is negative, got " + value.Points);
        }

        RouteDef route = forces.Route;
        if (!reg.Dungeons.TryGetValue(route.DungeonId, out DungeonDef? dungeon))
            Fail("route references unknown dungeon \"" + route.DungeonId + "\"");
        var earners = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (PullDef pull in dungeon!.Pulls)
            foreach (PullEnemy group in pull.Enemies)
            {
                if (!valued.Contains(group.EnemyId))
                    Fail("pull \"" + pull.Id + "\" enemy \"" + group.EnemyId + "\" has no forces value");
                earners.Add(group.EnemyId);
            }

        List<string> pullIds = dungeon.Pulls.Select(p => p.Id).ToList();
        int fork = pullIds.IndexOf(route.ForkAfterPull);
        int rejoin = pullIds.IndexOf(route.RejoinPull);
        if (fork < 0) Fail("route forkAfterPull \"" + route.ForkAfterPull + "\" is not a pull of \"" + dungeon.Id + "\"");
        if (rejoin < 0) Fail("route rejoinPull \"" + route.RejoinPull + "\" is not a pull of \"" + dungeon.Id + "\"");
        if (rejoin <= fork)
            Fail("route rejoinPull \"" + route.RejoinPull + "\" must come after forkAfterPull \"" + route.ForkAfterPull + "\"");

        var branchIds = new HashSet<string>(System.StringComparer.Ordinal);
        var rooms = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (BranchDef branch in route.Branches)
        {
            if (!branchIds.Add(branch.Id)) Fail("duplicate branch id \"" + branch.Id + "\"");
            if (branch.Rooms.Count == 0) Fail("branch \"" + branch.Id + "\" has no rooms");
            foreach (string room in branch.Rooms)
                if (!rooms.Add(room)) Fail("room \"" + room + "\" is in more than one branch");
        }

        foreach (PackDef pack in reg.Packs.Values)
        {
            string where = "pack \"" + pack.Id + "\"";
            if (!rooms.Contains(pack.Room)) Fail(where + " is in unknown room \"" + pack.Room + "\"");
            if (pack.RingMm <= 0) Fail(where + " ringMm must be positive, got " + pack.RingMm);
            if (pack.Enemies.Count == 0) Fail(where + " has no enemies");
            foreach (PullEnemy member in pack.Enemies)
            {
                if (!reg.Enemies.ContainsKey(member.EnemyId)) Fail(where + " references unknown enemy \"" + member.EnemyId + "\"");
                if (member.Count <= 0) Fail(where + " enemy \"" + member.EnemyId + "\" count must be positive, got " + member.Count);
                if (!valued.Contains(member.EnemyId)) Fail(where + " enemy \"" + member.EnemyId + "\" has no forces value");
                earners.Add(member.EnemyId);
            }
        }

        foreach (ForcesValue value in forces.Values)
            if (value.Points > 0 && !earners.Contains(value.EnemyId))
                Fail("enemy \"" + value.EnemyId + "\" is worth " + value.Points + " forces but is in no pull or pack");

        foreach (HazardDef hazard in reg.Hazards.Values)
            if (!rooms.Contains(hazard.Room)) Fail("hazard \"" + hazard.Id + "\" is in unknown room \"" + hazard.Room + "\"");

        foreach (BranchDef branch in route.Branches)
        {
            int total = RouteForces.BranchPoints(reg, branch);
            if (total < forces.Required)
                Fail("branch \"" + branch.Id + "\" reaches " + total + " of the " + forces.Required + " forces required (critical path plus every pack)");
        }
    }

    private static void CheckHeatOverheat(ContentRegistry reg)
    {
        HeatOverheatConfig h = reg.Run.HeatOverheat;
        if (!reg.Effects.ContainsKey(h.ChillEffectId))
            Fail("run heatOverheat.chillEffectId \"" + h.ChillEffectId + "\" is not an effect");
        if (h.ChillTicks <= 0) Fail("run heatOverheat.chillTicks must be positive, got " + h.ChillTicks);
    }

    /// <summary>run.json pairMarks (BIBLE section 9.6 item 2): both marks name real effects; every number is positive.</summary>
    private static void CheckPairMarks(ContentRegistry reg)
    {
        PairMarksConfig m = reg.Run.PairMarks;
        if (!reg.Effects.ContainsKey(m.SpreadEffectId))
            Fail("run pairMarks.spreadEffectId \"" + m.SpreadEffectId + "\" is not an effect");
        if (!reg.Effects.ContainsKey(m.StackEffectId))
            Fail("run pairMarks.stackEffectId \"" + m.StackEffectId + "\" is not an effect");
        if (m.SpreadRadiusMm <= 0) Fail("run pairMarks.spreadRadiusMm must be positive, got " + m.SpreadRadiusMm);
        if (m.SpreadDamage <= 0) Fail("run pairMarks.spreadDamage must be positive, got " + m.SpreadDamage);
        if (m.StackRadiusMm <= 0) Fail("run pairMarks.stackRadiusMm must be positive, got " + m.StackRadiusMm);
        if (m.StackDamage <= 0) Fail("run pairMarks.stackDamage must be positive, got " + m.StackDamage);
    }

    private static void CheckRun(RunConfig run)
    {
        if (run.Movement.SpeedMmPerTick <= 0) Fail("run movement.speedMmPerTick must be positive, got " + run.Movement.SpeedMmPerTick);
        if (run.Movement.RadiusMm <= 0) Fail("run movement.radiusMm must be positive, got " + run.Movement.RadiusMm);
        AffixesConfig a = run.Affixes;
        if (a.OneAffixKey <= 0 || a.BothAffixKey <= a.OneAffixKey)
            Fail("run affixes must have 0 < oneAffixKey < bothAffixKey, got " + a.OneAffixKey + " and " + a.BothAffixKey);
        if (a.ClauseStartKey <= 0) Fail("run affixes.clauseStartKey must be positive, got " + a.ClauseStartKey);
        foreach ((string name, AffixScaleConfig s) in new[] { ("fortified", a.Fortified), ("tyrannical", a.Tyrannical) })
        {
            if (s.HealthBp < 0 || s.DamageBp < 0)
                Fail("run affixes." + name + " healthBp and damageBp must be non-negative, got " + s.HealthBp + " and " + s.DamageBp);
        }
        ArenaBounds b = run.ArenaBounds;
        if (b.MinX >= b.MaxX || b.MinZ >= b.MaxZ)
            Fail("run arenaBounds must have minX < maxX and minZ < maxZ, got x " + b.MinX + ".." + b.MaxX + " z " + b.MinZ + ".." + b.MaxZ);
    }

    private static void Fail(string message) => throw new ContentLoadException("content: " + message);

    private static void CheckOps(IEnumerable<EffectOp>? ops, ContentRegistry reg, string where, bool requireSetupClass, bool riderOps = false)
    {
        if (ops is null) return;
        foreach (EffectOp op in ops)
        {
            if (!riderOps && op is PartyEffectOp or CloudOp)
                Fail(where + " uses the C#-only combo op \"" + (op is CloudOp ? "cloud" : "partyEffect") + "\", allowed only in " + ContentLoader.ComboRidersFile);
            if (op is PartyEffectOp party && party.DurationTicks <= 0)
                Fail(where + " partyEffect durationTicks must be positive, got " + party.DurationTicks);
            if (op is CloudOp cloud && cloud.DurationTicks <= 0)
                Fail(where + " cloud durationTicks must be positive, got " + cloud.DurationTicks);
            if (op is CloudOp sized && sized.RadiusMm <= 0)
                Fail(where + " cloud radiusMm must be positive, got " + sized.RadiusMm);
            string? effectId = op switch
            {
                ApplyEffectOp apply => apply.EffectId,
                RemoveEffectOp remove => remove.EffectId,
                PartyEffectOp party2 => party2.EffectId,
                CloudOp cloud2 => cloud2.EffectId,
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

    private static void CheckComboRiders(ContentRegistry reg)
    {
        foreach (ComboRider rider in reg.ComboRiders.Values)
        {
            string where = "combo rider \"" + rider.Id + "\"";
            if (!reg.Combos.ContainsKey(rider.Id))
                Fail(where + " references unknown combo \"" + rider.Id + "\"");
            CheckOps(rider.Effects, reg, where, requireSetupClass: true, riderOps: true);
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
            {
                if (!reg.Abilities.TryGetValue(step.Cast, out AbilityDef? cast)) Fail(where + " script references unknown ability \"" + step.Cast + "\"");
                else if (step.Telegraph?.SpansCast == true && cast.CastTicks <= 0)
                    Fail(where + " script step \"" + step.Cast + "\" telegraph spansCast needs a cast with castTicks > 0 (has " + cast.CastTicks + ")");
            }
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
