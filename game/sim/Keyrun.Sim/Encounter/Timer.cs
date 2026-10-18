using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>How a run ended (sim/encounter/timer.ts:58 RunResult).</summary>
public enum RunResult
{
    /// <summary>"timed": completed with Slack left.</summary>
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

    /// <summary>
    /// timer.ts:61 finishRun: always emits <c>{tick, type:"finish", result}</c>; abandoned when not completed, else
    /// depleted or timed by the bank.
    /// </summary>
    public static RunResult FinishRun(EncounterState encounter, int tick, bool completed, Action<SimEvent> emit)
    {
        var result = !completed ? RunResult.Abandoned : encounter.Depleted ? RunResult.Depleted : RunResult.Timed;
        emit(new SimEvent(tick, "finish", ("result", JsValue.Str(result.ToJs()))));
        return result;
    }
}
