using System.Collections.Generic;
using System.Linq;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>Content rule moved unchanged from ContentValidator; registered in <see cref="ContentRules"/>.</summary>
internal sealed class RunConfigRule : IContentRule
{
    public void Check(ContentRegistry reg)
    {
        RunConfig run = reg.Run;
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
        if (run.EnemyAggro is { } aggro)
        {
            CheckEnemyAggro(reg, aggro);
        }
        if (run.Cleaved is { } cleaved)
        {
            // R-BOSS-4: the marking ability and the stacking effect exist, and a mark lasts a positive time.
            if (!reg.Abilities.ContainsKey(cleaved.AbilityId)) Fail("run cleaved.abilityId \"" + cleaved.AbilityId + "\" is not an ability");
            if (!reg.Effects.ContainsKey(cleaved.EffectId)) Fail("run cleaved.effectId \"" + cleaved.EffectId + "\" is not an effect");
            if (cleaved.DurationTicks <= 0) Fail("run cleaved.durationTicks must be positive, got " + cleaved.DurationTicks);
        }
        if (run.EnemyScriptAt is { } scriptAt)
        {
            // R-PAIR-1: each entry names a known enemy, a cast its script makes, and a non-negative first tick.
            foreach (string enemyId in scriptAt.Keys)
            {
                if (!reg.Enemies.TryGetValue(enemyId, out var enemy)) Fail("run enemyScriptAt \"" + enemyId + "\" is not an enemy");
                foreach (string cast in scriptAt[enemyId].Keys)
                {
                    if (!enemy!.Script.Any(s => s.Cast == cast)) Fail("run enemyScriptAt " + enemyId + " \"" + cast + "\" is not a cast of its script");
                    if (scriptAt[enemyId][cast] < 0) Fail("run enemyScriptAt " + enemyId + " " + cast + " must be non-negative, got " + scriptAt[enemyId][cast]);
                }
            }
        }
    }

    // AGGRO-CHASE (run.json enemyAggro): non-negative values, known enemy keys, and a leash that is on is longer than the
    // same enemy's aggro radius, so a pack never resets inside the reach that pulls it again.
    private static void CheckEnemyAggro(ContentRegistry reg, EnemyAggroConfig aggro)
    {
        foreach (string id in aggro.Kinds.Keys)
        {
            if (!reg.Enemies.ContainsKey(id))
                Fail("run enemyAggro.kinds \"" + id + "\" is not an enemy");
        }
        foreach (string id in aggro.Kinds.Keys.Prepend("(default)"))
        {
            var (aggroMm, assistMm, leashMm) = aggro.For(id);
            if (aggroMm < 0 || assistMm < 0 || leashMm < 0)
                Fail("run enemyAggro " + id + " aggroMm, assistMm and leashMm must be non-negative, got " + aggroMm + ", " + assistMm + " and " + leashMm);
            if (leashMm > 0 && leashMm <= aggroMm)
                Fail("run enemyAggro " + id + " leashMm " + leashMm + " must be longer than its aggroMm " + aggroMm);
        }
    }
}
