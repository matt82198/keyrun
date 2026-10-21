using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>How a run ended (sim/encounter/timer.ts RunResult).</summary>
public enum RunResult
{
    /// <summary>"timed": completed with the run clock (elapsed plus death and wipe time) no later than par.</summary>
    Timed,

    /// <summary>"depleted": completed past par on the run clock, or ended by an unrecovered full wipe.</summary>
    Depleted,

    /// <summary>"abandoned": not completed.</summary>
    Abandoned,
}

/// <summary>
/// The run timer against par (sim/encounter/timer.ts). Mythic+ rules (STATE.md D46, Matt 2026-10-20 "lean into m+
/// design heavily", "almost exact to wow"): a player death adds its death cost to the run clock and a full wipe adds
/// the wipe cost on top. Nothing drains on its own: a key is depleted only when the clock (elapsed plus that added
/// time) finishes past par, or when a full wipe is never recovered (<see cref="EncounterState.Depleted"/>). The Slack
/// bank constants stay as tunables (BIBLE s8) but no longer decide a result.
/// </summary>
public static class Timer
{
    /// <summary>30 s base bank at key 1 (timer.ts SLACK_BASE_TICKS). Tunable; no longer decides a result (D46).</summary>
    public const int SlackBaseTicks = 600;

    /// <summary>+1 s per key level above 1 (timer.ts SLACK_PER_KEY_TICKS). Tunable; no longer decides a result (D46).</summary>
    public const int SlackPerKeyTicks = 20;

    /// <summary>A death adds 15 s to the run clock (timer.ts DEATH_SLACK_COST_TICKS) unless the Clause sets its own.</summary>
    public const int DeathSlackCostTicks = 300;

    /// <summary>A full wipe adds 25 s on top of its deaths (timer.ts WIPE_SLACK_COST_TICKS) unless the Clause sets its own.</summary>
    public const int WipeSlackCostTicks = 500;

    /// <summary>The TS string of a result: "timed", "depleted" or "abandoned".</summary>
    public static string ToJs(this RunResult r) => r switch
    {
        RunResult.Timed => "timed",
        RunResult.Depleted => "depleted",
        RunResult.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    /// <summary>timer.ts initialSlackTicks: 600 + 20 per key level above 1.</summary>
    public static int InitialSlackTicks(int keyLevel) => SlackBaseTicks + (SlackPerKeyTicks * Math.Max(0, keyLevel - 1));

    /// <summary>
    /// The time one death adds: the Clause's <see cref="EncounterRules.DeathSlackCostTicks"/> (Clauses.cs:60) when set
    /// (above 0), else <see cref="DeathSlackCostTicks"/>.
    /// </summary>
    /// <param name="rules">The run's encounter rules.</param>
    /// <returns>Ticks.</returns>
    public static int DeathCostTicks(EncounterRules? rules) =>
        rules is { DeathSlackCostTicks: > 0 } ? rules.DeathSlackCostTicks : DeathSlackCostTicks;

    /// <summary>
    /// The time one full wipe adds: the Clause's <see cref="EncounterRules.WipeSlackCostTicks"/> when set (above 0),
    /// else <see cref="WipeSlackCostTicks"/>.
    /// </summary>
    /// <param name="rules">The run's encounter rules.</param>
    /// <returns>Ticks.</returns>
    public static int WipeCostTicks(EncounterRules? rules) =>
        rules is { WipeSlackCostTicks: > 0 } ? rules.WipeSlackCostTicks : WipeSlackCostTicks;

    /// <summary>timer.ts chargeDeath: one more death counted, its cost added to the run clock.</summary>
    /// <param name="encounter">The encounter.</param>
    /// <returns>The encounter with the death charged.</returns>
    public static EncounterState ChargeDeath(EncounterState encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return encounter with
        {
            Deaths = encounter.Deaths + 1,
            PenaltyTicks = encounter.PenaltyTicks + DeathCostTicks(encounter.Rules),
        };
    }

    /// <summary>timer.ts chargeWipe: the wipe cost added to the run clock, on top of the wipe's own deaths.</summary>
    /// <param name="encounter">The encounter.</param>
    /// <returns>The encounter with the wipe charged.</returns>
    public static EncounterState ChargeWipe(EncounterState encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return encounter with { PenaltyTicks = encounter.PenaltyTicks + WipeCostTicks(encounter.Rules) };
    }

    /// <summary>
    /// The time the run's deaths have cost (MPLUS-DEATH-PENALTY): <see cref="EncounterState.Deaths"/> times
    /// <see cref="DeathCostTicks"/>, the "Deaths: N (-Xs)" figure of a Mythic+ timer. Wipe costs are not in it.
    /// </summary>
    /// <param name="encounter">The encounter.</param>
    /// <returns>Ticks lost to deaths.</returns>
    public static long DeathPenaltyTicks(EncounterState encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return (long)Math.Max(0, encounter.Deaths) * DeathCostTicks(encounter.Rules);
    }

    /// <summary>
    /// The run clock: <paramref name="elapsedTicks"/> plus every death and wipe cost so far
    /// (<see cref="EncounterState.PenaltyTicks"/>). Par and the upgrade tiers are measured on this.
    /// </summary>
    /// <param name="encounter">The encounter.</param>
    /// <param name="elapsedTicks">The elapsed run timer.</param>
    /// <returns>Clock ticks.</returns>
    public static long ClockTicks(EncounterState encounter, int elapsedTicks)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return (long)elapsedTicks + Math.Max(0, encounter.PenaltyTicks);
    }

    /// <summary>timer.ts isOverPar: strictly past par.</summary>
    public static bool IsOverPar(int runTimerTicks, EncounterState encounter) => runTimerTicks > encounter.ParTicks;

    /// <summary>A timed run finishing within this share of par upgrades the key by +2 (BIBLE s8: "within 80% of par").</summary>
    public const int UpgradeTwoParBp = 8000;

    /// <summary>A timed run finishing within this share of par upgrades the key by +3 (BIBLE s8: "within 60% of par").</summary>
    public const int UpgradeThreeParBp = 6000;

    private const int ParBpDenom = 10000;

    /// <summary>
    /// The last elapsed tick a completed run can finish on and still be timed (D46): par less the time deaths and wipes
    /// have added to the clock. Every death pulls the deadline in by its cost; nothing else does.
    /// </summary>
    /// <param name="encounter">The encounter, for its par and its added time.</param>
    /// <returns>The deadline tick (the run timer counts from tick 0); below 0 once the added time exceeds par.</returns>
    public static long DeadlineTicks(EncounterState encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return (long)encounter.ParTicks - Math.Max(0, encounter.PenaltyTicks);
    }

    /// <summary>
    /// True when a completed run finishing on <paramref name="finishTick"/> is timed: no unrecovered full wipe forced
    /// the depleted flag and the run clock (<see cref="ClockTicks"/>) is no later than par.
    /// </summary>
    /// <param name="encounter">The encounter at the finish.</param>
    /// <param name="finishTick">The finish tick, which is the run timer (both start at 0 and step together).</param>
    /// <returns>True when timed.</returns>
    public static bool IsTimed(EncounterState encounter, int finishTick)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return !encounter.Depleted && ClockTicks(encounter, finishTick) <= encounter.ParTicks;
    }

    /// <summary>
    /// The key upgrade a finished run earns (BIBLE s8, Mythic+ upgrade tiers measured on the run clock against par):
    /// 0 for a depleted or abandoned run (Matt 2026-10-20: an untimed key stays at its level), else +3 within
    /// <see cref="UpgradeThreeParBp"/> of par, +2 within <see cref="UpgradeTwoParBp"/>, and +1 otherwise.
    /// </summary>
    /// <param name="result">The run result.</param>
    /// <param name="clockTicks">The run clock at the finish (<see cref="ClockTicks"/>: elapsed plus death and wipe time).</param>
    /// <param name="parTicks">Par.</param>
    /// <returns>0, 1, 2 or 3.</returns>
    public static int UpgradeTier(RunResult result, long clockTicks, int parTicks)
    {
        if (result != RunResult.Timed)
        {
            return 0;
        }
        var scaled = clockTicks * ParBpDenom;
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
    /// <see cref="UpgradeTier"/> (+1, +2 within 80% of par, +3 within 60%, on the run clock, D46); a depleted or
    /// abandoned run keeps the key at the level it was played at, never lower (Matt 2026-10-20: "an untimed key stays
    /// at the same level, with no drop").
    /// </summary>
    /// <param name="keyLevel">The key the run was played at.</param>
    /// <param name="result">The run result.</param>
    /// <param name="clockTicks">The run clock at the finish (<see cref="ClockTicks"/>: elapsed plus death and wipe time, D46).</param>
    /// <param name="parTicks">Par.</param>
    /// <returns>The next key level (the caller clamps to its band).</returns>
    public static int KeyAfterRun(int keyLevel, RunResult result, long clockTicks, int parTicks) =>
        result == RunResult.Timed ? NextKeyLevel(keyLevel, UpgradeTier(result, clockTicks, parTicks)) : keyLevel;

    /// <summary>
    /// timer.ts finishRun: always emits <c>{tick, type:"finish", result}</c>; abandoned when not completed, else
    /// timed when <see cref="IsTimed"/> (the run clock within par) and depleted otherwise.
    /// </summary>
    public static RunResult FinishRun(EncounterState encounter, int tick, bool completed, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        var result = !completed ? RunResult.Abandoned : IsTimed(encounter, tick) ? RunResult.Timed : RunResult.Depleted;
        emit(new SimEvent(tick, "finish", ("result", JsValue.Str(result.ToJs()))));
        return result;
    }
}
