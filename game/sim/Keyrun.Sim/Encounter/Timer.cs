using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>How a run ended (sim/encounter/timer.ts:58 RunResult).</summary>
public enum RunResult
{
    /// <summary>"timed": completed no later than par plus the Slack left.</summary>
    Timed,

    /// <summary>"depleted": completed, but the Slack bank ran out.</summary>
    Depleted,

    /// <summary>"abandoned": not completed.</summary>
    Abandoned,
}

/// <summary>
/// The run timer against par and the Slack bank (sim/encounter/timer.ts). Slack is a soft time bank in ticks that
/// starts full per key level and is spent on player deaths and full-party wipes instead of a hard fail (BIBLE s8).
/// Spends clamp at 0 and emit <c>depleted</c> exactly once, the tick the bank first reaches 0. A depleted bank still
/// allows completion; it only downgrades the result from timed to depleted.
/// </summary>
public static class Timer
{
    /// <summary>30 s base bank at key 1 (timer.ts:16).</summary>
    public const int SlackBaseTicks = 600;

    /// <summary>+1 s per key level above 1 (timer.ts:17).</summary>
    public const int SlackPerKeyTicks = 20;

    /// <summary>A death costs 15 s (timer.ts:19).</summary>
    public const int DeathSlackCostTicks = 300;

    /// <summary>A full wipe costs 25 s (timer.ts:20).</summary>
    public const int WipeSlackCostTicks = 500;

    /// <summary>The TS string of a result: "timed", "depleted" or "abandoned".</summary>
    public static string ToJs(this RunResult r) => r switch
    {
        RunResult.Timed => "timed",
        RunResult.Depleted => "depleted",
        RunResult.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    /// <summary>timer.ts:22 initialSlackTicks: 600 + 20 per key level above 1.</summary>
    public static int InitialSlackTicks(int keyLevel) => SlackBaseTicks + (SlackPerKeyTicks * Math.Max(0, keyLevel - 1));

    // timer.ts:26 spendSlack.
    private static EncounterState SpendSlack(EncounterState encounter, int costTicks, int tick, Action<SimEvent> emit)
    {
        var next = Math.Max(0, encounter.SlackTicks - costTicks);
        if (next == 0 && !encounter.Depleted)
        {
            emit(new SimEvent(tick, "depleted"));
        }
        return encounter with { SlackTicks = next, Depleted = encounter.Depleted || next == 0 };
    }

    /// <summary>timer.ts:38 spendSlackOnDeath: spends <see cref="DeathSlackCostTicks"/>.</summary>
    public static EncounterState SpendSlackOnDeath(EncounterState encounter, int tick, Action<SimEvent> emit) =>
        SpendSlack(encounter, DeathSlackCostTicks, tick, emit);

    /// <summary>timer.ts:46 spendSlackOnWipe: spends <see cref="WipeSlackCostTicks"/>.</summary>
    public static EncounterState SpendSlackOnWipe(EncounterState encounter, int tick, Action<SimEvent> emit) =>
        SpendSlack(encounter, WipeSlackCostTicks, tick, emit);

    /// <summary>timer.ts:54 isOverPar: strictly past par.</summary>
    public static bool IsOverPar(int runTimerTicks, EncounterState encounter) => runTimerTicks > encounter.ParTicks;

    /// <summary>A timed run finishing within this share of par upgrades the key by +2 (BIBLE s8: "within 80% of par").</summary>
    public const int UpgradeTwoParBp = 8000;

    /// <summary>A timed run finishing within this share of par upgrades the key by +3 (BIBLE s8: "within 60% of par").</summary>
    public const int UpgradeThreeParBp = 6000;

    private const int ParBpDenom = 10000;

    /// <summary>
    /// The last tick a completed run can finish on and still be timed (BIBLE s8, M1 TIMER-TIERS): par plus the Slack
    /// still left. Slack is grace on top of par, so every death or wipe it pays for pulls the deadline in.
    /// </summary>
    /// <param name="encounter">The encounter, for its par and its Slack left.</param>
    /// <returns>The deadline tick (the run timer counts from tick 0).</returns>
    public static long DeadlineTicks(EncounterState encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return (long)encounter.ParTicks + Math.Max(0, encounter.SlackTicks);
    }

    /// <summary>
    /// True when a completed run finishing on <paramref name="finishTick"/> is timed: the bank never ran dry (a wipe
    /// past its grace forces the depleted flag) and the finish is no later than <see cref="DeadlineTicks"/>.
    /// </summary>
    /// <param name="encounter">The encounter at the finish.</param>
    /// <param name="finishTick">The finish tick, which is the run timer (both start at 0 and step together).</param>
    /// <returns>True when timed.</returns>
    public static bool IsTimed(EncounterState encounter, int finishTick)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return !encounter.Depleted && finishTick <= DeadlineTicks(encounter);
    }

    /// <summary>
    /// The key upgrade a finished run earns (BIBLE s8, Mythic+ upgrade tiers measured against par, never against par
    /// plus Slack): 0 for a depleted or abandoned run (Matt 2026-10-20: an untimed key stays at its level), else +3
    /// within <see cref="UpgradeThreeParBp"/> of par, +2 within <see cref="UpgradeTwoParBp"/>, and +1 otherwise.
    /// </summary>
    /// <param name="result">The run result.</param>
    /// <param name="finishTick">The finish tick (the run timer).</param>
    /// <param name="parTicks">Par.</param>
    /// <returns>0, 1, 2 or 3.</returns>
    public static int UpgradeTier(RunResult result, int finishTick, int parTicks)
    {
        if (result != RunResult.Timed)
        {
            return 0;
        }
        var scaled = (long)finishTick * ParBpDenom;
        if (scaled <= (long)parTicks * UpgradeThreeParBp)
        {
            return 3;
        }
        return scaled <= (long)parTicks * UpgradeTwoParBp ? 2 : 1;
    }

    /// <summary>The key level after a run: <paramref name="keyLevel"/> raised by <see cref="UpgradeTier"/>, never lowered.</summary>
    /// <param name="keyLevel">The key the run was played at.</param>
    /// <param name="tier">The run's upgrade tier, 0-3.</param>
    /// <returns>The next key level (the caller clamps to its band).</returns>
    public static int NextKeyLevel(int keyLevel, int tier) => keyLevel + Math.Clamp(tier, 0, 3);

    /// <summary>
    /// The key level the end-of-run screen reopens at (BIBLE s8, finding f-7c7abdf4): a timed run raises the key by its
    /// <see cref="UpgradeTier"/> (+1, +2 within 80% of par, +3 within 60%); a depleted or abandoned run keeps the key at
    /// the level it was played at, never lower (Matt 2026-10-20: "an untimed key stays at the same level, with no drop").
    /// </summary>
    /// <param name="keyLevel">The key the run was played at.</param>
    /// <param name="result">The run result.</param>
    /// <param name="finishTick">The finish tick (the run timer).</param>
    /// <param name="parTicks">Par.</param>
    /// <returns>The next key level (the caller clamps to its band).</returns>
    public static int KeyAfterRun(int keyLevel, RunResult result, int finishTick, int parTicks) =>
        result == RunResult.Timed ? NextKeyLevel(keyLevel, UpgradeTier(result, finishTick, parTicks)) : keyLevel - 1;

    /// <summary>
    /// timer.ts:61 finishRun: always emits <c>{tick, type:"finish", result}</c>; abandoned when not completed, else
    /// timed when <see cref="IsTimed"/> (par plus the Slack left, M1 TIMER-TIERS) and depleted otherwise.
    /// </summary>
    public static RunResult FinishRun(EncounterState encounter, int tick, bool completed, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        var result = !completed ? RunResult.Abandoned : IsTimed(encounter, tick) ? RunResult.Timed : RunResult.Depleted;
        emit(new SimEvent(tick, "finish", ("result", JsValue.Str(result.ToJs()))));
        return result;
    }
}
