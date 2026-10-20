using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// Key level to Tempo (sim/encounter/tempo.ts), plus the per-step scaling the TS integration layer applies to
/// content scripts and spawns (client/src/sim-host/content-dungeon.ts:48-63 scaleStep / scaleScript / scaleSpawns).
/// The key scales the pace (BIBLE s8): casts come more often (<c>every</c> shrinks), telegraph lead shrinks toward a
/// floor, and health grows gently. All basis-point integer math, 10000 = 100%; each scale is one truncating
/// division (tempo.ts Math.trunc, <see cref="IntMath.TruncDiv"/>) clamped to its floor.
/// </summary>
public static class Tempo
{
    /// <summary>10000 basis points = 100% (tempo.ts:12).</summary>
    public const int BpDenom = 10000;

    /// <summary>Cast frequency +2% per key level above 1 (tempo.ts:15).</summary>
    public const int TempoBpPerKey = 200;

    /// <summary>Telegraph lead shrinks 1.5% per key level above 1, capped at 100% (tempo.ts:17).</summary>
    public const int LeadShrinkBpPerKey = 150;

    /// <summary>A telegraph never leads by fewer than 10 ticks, 0.5 s (tempo.ts:19).</summary>
    public const int LeadFloorTicks = 10;

    /// <summary>An enemy never casts more than once every 20 ticks, 1 s (tempo.ts:21).</summary>
    public const int EveryFloorTicks = 20;

    /// <summary>Health +3% per key level above 1 (tempo.ts:23).</summary>
    public const int HealthBpPerKey = 300;

    // tempo.ts:25 MECHANIC_UNLOCK_KEYS (one extra mechanic at keys 5, 10 and 15) is superseded by Fortified and
    // Tyrannical (BIBLE s8, Matt 2026-10-20; plan M5): the key's extra difficulty is Affixes.AffixesFor.

    private static long KeysAbove1(int keyLevel) => Math.Max(0, keyLevel - 1);

    /// <summary>tempo.ts:27 tempoMultiplierBp: 10000 + 200 per key above 1.</summary>
    public static int TempoMultiplierBp(int keyLevel) => checked((int)(BpDenom + (TempoBpPerKey * KeysAbove1(keyLevel))));

    /// <summary>tempo.ts:31 scaleCastEvery: <c>max(20, trunc(every * 10000 / mult))</c>.</summary>
    public static int ScaleCastEvery(int baseEveryTicks, int keyLevel)
    {
        long mult = TempoMultiplierBp(keyLevel);
        var scaled = IntMath.TruncDiv((long)baseEveryTicks * BpDenom, mult);
        return checked((int)Math.Max(EveryFloorTicks, scaled));
    }

    /// <summary>tempo.ts:37 scaleLeadTicks: <c>max(10, trunc(lead * (10000 - min(10000, 150 * keys)) / 10000))</c>.</summary>
    public static int ScaleLeadTicks(int baseLeadTicks, int keyLevel)
    {
        var shrinkBp = Math.Min(BpDenom, LeadShrinkBpPerKey * KeysAbove1(keyLevel));
        var scaled = IntMath.TruncDiv((long)baseLeadTicks * (BpDenom - shrinkBp), BpDenom);
        return checked((int)Math.Max(LeadFloorTicks, scaled));
    }

    /// <summary>tempo.ts:43 scaleHealth: <c>trunc(health * (10000 + 300 * keys) / 10000)</c> (no floor).</summary>
    public static long ScaleHealth(long baseHealth, int keyLevel)
    {
        var bp = BpDenom + (HealthBpPerKey * KeysAbove1(keyLevel));
        return IntMath.TruncDiv(baseHealth * bp, BpDenom);
    }

    /// <summary>
    /// content-dungeon.ts:48 scaleStep: the telegraph's lead is scaled; a loop's <c>every</c> is scaled, defaulting
    /// to the 20-tick floor when absent (:51); a numeric <c>at</c> keeps its tick and scales <c>every</c> only when
    /// present (:53); onDeath steps keep their cast unchanged (they take the numeric branch with no every). The
    /// target override is not carried (:51 and :54 rebuild the step from at/every/cast/telegraph only).
    /// </summary>
    public static ScriptStep ScaleStep(ScriptStep step, int keyLevel)
    {
        var telegraph = step.Telegraph is { } tg ? tg with { LeadTicks = ScaleLeadTicks(tg.LeadTicks, keyLevel) } : null;
        if (step.Trigger == StepTrigger.Loop)
        {
            return new ScriptStep(StepTrigger.Loop, step.Cast)
            {
                Every = ScaleCastEvery(step.Every ?? EveryFloorTicks, keyLevel),
                Telegraph = telegraph,
            };
        }
        return new ScriptStep(step.Trigger, step.Cast)
        {
            AtTick = step.AtTick,
            Every = step.Every is { } every ? ScaleCastEvery(every, keyLevel) : null,
            Telegraph = telegraph,
        };
    }

    /// <summary>content-dungeon.ts:57 scaleScript: <see cref="ScaleStep(ScriptStep, int)"/> over every step, in order.</summary>
    public static IReadOnlyList<ScriptStep> ScaleScript(IReadOnlyList<ScriptStep> script, int keyLevel) =>
        script.Select(s => ScaleStep(s, keyLevel)).ToList();

    /// <summary>
    /// TEMPO-CAST-FLOOR (BIBLE s3, "Cast bars are the enemy's language"): the shortest period a script step may repeat
    /// its cast, the larger of the ability's cooldown and its cast time. Faster than the cooldown, the cast is refused
    /// <c>not_ready</c> on the fire tick (a telegraph with no cast, and an aliased cadence); faster than the cast time,
    /// a new cast would start before the last one lands. Negative inputs count as 0.
    /// </summary>
    public static int CastFloorTicks(int cooldownTicks, int castTicks) => Math.Max(0, Math.Max(cooldownTicks, castTicks));

    /// <summary>TEMPO-CAST-FLOOR: <see cref="ScaleCastEvery(int, int)"/>, then never below <paramref name="castFloorTicks"/>.</summary>
    /// <param name="baseEveryTicks">The authored period.</param>
    /// <param name="keyLevel">The key level.</param>
    /// <param name="castFloorTicks">The cast's <see cref="CastFloorTicks"/>.</param>
    /// <returns>The scaled period, at least 20 ticks and at least the cast's floor.</returns>
    public static int ScaleCastEvery(int baseEveryTicks, int keyLevel, int castFloorTicks) =>
        Math.Max(ScaleCastEvery(baseEveryTicks, keyLevel), castFloorTicks);

    /// <summary>
    /// TEMPO-CAST-FLOOR: <see cref="ScaleStep(ScriptStep, int)"/> (the frozen-TS parity rule, which the oracle setups
    /// record), then a present <c>every</c> raised to the cast's floor. Trigger, at, cast and telegraph are kept.
    /// </summary>
    /// <param name="step">The authored step.</param>
    /// <param name="keyLevel">The key level.</param>
    /// <param name="castFloorTicks">Ability id to its <see cref="CastFloorTicks"/>.</param>
    /// <returns>The scaled step.</returns>
    public static ScriptStep ScaleStep(ScriptStep step, int keyLevel, Func<string, int> castFloorTicks)
    {
        ArgumentNullException.ThrowIfNull(castFloorTicks);
        var scaled = ScaleStep(step, keyLevel);
        if (scaled.Every is not { } every)
        {
            return scaled;
        }
        var floor = castFloorTicks(step.Cast);
        return every >= floor ? scaled : scaled with { Every = floor };
    }

    /// <summary>TEMPO-CAST-FLOOR: <see cref="ScaleStep(ScriptStep, int, Func{string, int})"/> over every step, in order.</summary>
    /// <param name="script">The authored script.</param>
    /// <param name="keyLevel">The key level.</param>
    /// <param name="castFloorTicks">Ability id to its <see cref="CastFloorTicks"/>.</param>
    /// <returns>The scaled script.</returns>
    public static IReadOnlyList<ScriptStep> ScaleScript(IReadOnlyList<ScriptStep> script, int keyLevel, Func<string, int> castFloorTicks) =>
        script.Select(s => ScaleStep(s, keyLevel, castFloorTicks)).ToList();

    /// <summary>content-dungeon.ts:61 scaleSpawns (one spawn): health scaled, everything else kept.</summary>
    public static PullSpawn ScaleSpawn(PullSpawn spawn, int keyLevel) => spawn with { Health = ScaleHealth(spawn.Health, keyLevel) };
}
