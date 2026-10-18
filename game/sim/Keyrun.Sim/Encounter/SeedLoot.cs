using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>Which Window the Seed drops and who drops it (BIBLE s9 item 3).</summary>
/// <param name="SeedActorId">The Seed elite's actor id.</param>
/// <param name="AbilityId">Its onDeath cast (seed.deathBloom in content/data/enemies/seed.json).</param>
/// <param name="WindowId">The Window that cast opens (the ability's "window" op windowId, "seedbloom").</param>
public sealed record SeedLootRule(int SeedActorId, string AbilityId, string WindowId);

/// <summary>A bonus-loot entry granted at run end.</summary>
/// <param name="Source">What granted it ("seed").</param>
/// <param name="WindowId">The Window the Combo landed in.</param>
/// <param name="ComboId">The Combo that landed.</param>
/// <param name="Tick">The Combo's tick.</param>
/// <param name="SetupActorId">The Window's setup actor (the Seed).</param>
/// <param name="PayoffActorId">Who landed the Payoff.</param>
public sealed record BonusLootEntry(string Source, string WindowId, string ComboId, int Tick, int SetupActorId, int PayoffActorId);

/// <summary>The run's reward-side state that accrues during play.</summary>
/// <param name="BonusLoot">Bonus entries, at most one from the Seed.</param>
/// <param name="SeedWindowEndsTick">The Seed Window's end tick once it has opened, else null.</param>
public sealed record RunRewards(IReadOnlyList<BonusLootEntry> BonusLoot, int? SeedWindowEndsTick)
{
    /// <summary>No bonus, no Seed Window seen.</summary>
    public static readonly RunRewards Empty = new(Array.Empty<BonusLootEntry>(), null);
}

/// <summary>
/// Seed Window loot (BIBLE s9 item 3, ENGINE-PLAN s5 lane 2C). NEW behaviour: the frozen TS has no bonus loot. The
/// Seed's onDeath cast opens a floor Window (seed.deathBloom: op "window", windowId "seedbloom", setupClass "boss",
/// content/data/abilities/enemies.json); a Combo landed in it sets one bonus-loot entry in the run's rewards.
///
/// It reads combat's SimEvents rather than combat state, so it needs no type from the combat lanes: the Window
/// opening is the <c>window_opened</c> event (sim/combat/windows.ts) with this windowId and the Seed as
/// <c>setupActorId</c>, and a Combo landed in it is the <c>combo</c> event sim/combat/payoff.ts:118-129 emits for a
/// floor Window (<c>windowId</c>, <c>setupActorId</c> = the Window's setup actor). The run root (lane 2D) calls
/// <see cref="Observe"/> once per tick with that tick's events and hands <see cref="RunRewards.BonusLoot"/> to the
/// run-end panel (lane 3C/3D), which decides what the entry pays out.
/// </summary>
public static class SeedLoot
{
    /// <summary>
    /// The rule for one Seed: its first onDeath step whose ability opens a Window, or null when it has none.
    /// <paramref name="windowIdOfAbility"/> maps an ability id to its "window" op's windowId (null when none).
    /// </summary>
    public static SeedLootRule? RuleFor(int seedActorId, IReadOnlyList<ScriptStep> script, Func<string, string?> windowIdOfAbility)
    {
        foreach (var step in script)
        {
            if (step.Trigger == StepTrigger.OnDeath && windowIdOfAbility(step.Cast) is { } windowId)
            {
                return new SeedLootRule(seedActorId, step.Cast, windowId);
            }
        }
        return null;
    }

    private static int? IntField(SimEvent e, string key) =>
        e.Get(key) is JsInt i && i.Value >= int.MinValue && i.Value <= int.MaxValue ? (int)i.Value : null;

    private static string? StrField(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    /// <summary>
    /// Folds one tick's events in: the Seed's <c>window_opened</c> records the Window's end tick; a <c>combo</c> in
    /// that Window (same windowId and setup actor, at or before its end tick) sets the bonus entry if none is set yet.
    /// A Combo before the Window opened, after it ended, or in any other Window never counts.
    /// </summary>
    public static RunRewards Observe(RunRewards rewards, SeedLootRule rule, IReadOnlyList<SimEvent> events)
    {
        var result = rewards;
        foreach (var e in events)
        {
            if (StrField(e, "windowId") != rule.WindowId || IntField(e, "setupActorId") != rule.SeedActorId)
            {
                continue;
            }
            if (e.Type == "window_opened" && IntField(e, "endsTick") is { } ends)
            {
                result = result with { SeedWindowEndsTick = ends };
            }
            else if (e.Type == "combo" && result.SeedWindowEndsTick is { } endsTick && e.Tick <= endsTick && result.BonusLoot.Count == 0)
            {
                var entry = new BonusLootEntry("seed", rule.WindowId, StrField(e, "comboId") ?? string.Empty, e.Tick, rule.SeedActorId,
                    IntField(e, "payoffActorId") ?? throw new ArgumentException("combo event lacks payoffActorId", nameof(events)));
                result = result with { BonusLoot = result.BonusLoot.Append(entry).ToList() };
            }
        }
        return result;
    }
}
