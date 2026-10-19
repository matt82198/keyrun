using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// Test-only probe for lane SEED-BONUS-REWARDS, run by RunEndBonusSceneTest.ps1 (run P) as its own scene
/// (res://src/Hud/RunEndRewardsProbe.tscn): the run-end panel's bonus line must be the award the sim carries on
/// <see cref="Run.Rewards"/> (#197 SEED-LOOT-AWARD), never a second fold of the step events. The probe builds a real
/// content run (hollow-vault, key 2, the warden seat), ends it, and feeds the panel's <see cref="SeedBonusLog"/> the
/// way <see cref="RunEndPanel"/> does on every step, in three cases:
///   agree   - the run carries the Seed award and the panel saw the events that earned it;
///   carried - the run carries the Seed award but the panel saw no Seed event;
///   none    - the run carries no award but the panel saw events that would fold to one.
/// In each case the "HUD runend bonus ..." line must be the one <see cref="Run.Rewards"/> gives. Prints
/// "RUNEND-REWARDS PASS cases=3" or one "RUNEND-REWARDS FAIL ..." line per failed case, then quits 0 or 1.
/// </summary>
public partial class RunEndRewardsProbe : Node
{
    /// <inheritdoc />
    public override void _Ready()
    {
        List<string> failures;
        try
        {
            failures = Check();
        }
        catch (Exception ex)
        {
            failures = new List<string> { "probe threw " + ex.GetType().Name + ": " + ex.Message };
        }
        foreach (var f in failures)
        {
            GD.Print("RUNEND-REWARDS FAIL " + f);
        }
        if (failures.Count == 0)
        {
            GD.Print("RUNEND-REWARDS PASS cases=3");
        }
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }

    /// <summary>Runs the three cases.</summary>
    /// <returns>One message per failed case; empty when all pass.</returns>
    public static List<string> Check()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, SimDriver.DungeonId, ArenaOptions.DefaultKeyLevel, SimDriver.HumanClassId, withBots: false);
        var created = RunRoot.CreateRun(1, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content);
        var rule = created.SeedRule ?? throw new InvalidOperationException("the content run has no Seed rule (no onDeath Window)");
        var ended = created with { RunEnd = new RunEndState(RunResult.Timed, 4000, null) };
        var kit = new HashSet<string>(setup.Slots.Select(s => s.AbilityId), StringComparer.Ordinal);
        var comboId = registry.Combos.Keys.OrderBy(k => k, StringComparer.Ordinal).First();

        // The Seed's Window opens at 3000 (ends 3100) and a Combo lands in it at 3050: SeedLoot.Observe awards one entry.
        var seedEvents = new List<SimEvent>
        {
            new(3000, "window_opened", ("endsTick", JsValue.Int(3100)), ("setupActorId", JsValue.Int(rule.SeedActorId)), ("windowId", JsValue.Str(rule.WindowId))),
            new(3050, "combo", ("comboId", JsValue.Str(comboId)), ("payoffActorId", JsValue.Int(setup.HumanActorId)), ("setupActorId", JsValue.Int(rule.SeedActorId)), ("windowId", JsValue.Str(rule.WindowId))),
        };
        var award = SeedLoot.Observe(RunRewards.Empty, rule, seedEvents);
        if (award.BonusLoot.Count != 1)
        {
            return new List<string> { "fixture: the Seed events do not fold to one bonus entry" };
        }

        var failures = new List<string>();
        void Case(string name, RunRewards carried, IReadOnlyList<SimEvent> seen)
        {
            var run = ended with { Rewards = carried };
            var bonus = new SeedBonusLog();
            Feed(bonus, run, seen);
            var view = RunEndView.Of(run, setup.HumanActorId, new PrivateRunLog(), registry, kit, bonus)
                ?? throw new InvalidOperationException("RunEndView.Of returned null for an ended run");
            var got = view.LogLines().Last();
            var want = BonusLineOf(run.Rewards, registry);
            if (got != want)
            {
                failures.Add(FormattableString.Invariant($"case={name}: want '{want}' (Run.Rewards), got '{got}'"));
            }
        }

        Case("agree", award, seedEvents);
        Case("carried", award, Array.Empty<SimEvent>());
        Case("none", RunRewards.Empty, seedEvents);
        return failures;
    }

    // Feeds the panel's log exactly as RunEndPanel.OnStepped does after a step.
    private static void Feed(SeedBonusLog bonus, Run run, IReadOnlyList<SimEvent> events) => bonus.Observe(events, run.Content);

    private static string BonusLineOf(RunRewards rewards, ContentRegistry registry)
    {
        var ends = rewards.SeedWindowEndsTick is { } e ? e.ToString(CultureInfo.InvariantCulture) : "none";
        var label = RunEndView.BonusLabelOf(rewards.BonusLoot, registry) is { } l ? $" label=\"{l.Replace('"', '\'')}\"" : string.Empty;
        return FormattableString.Invariant($"HUD runend bonus count={rewards.BonusLoot.Count} seedWindowEnds={ends}{label}");
    }
}
