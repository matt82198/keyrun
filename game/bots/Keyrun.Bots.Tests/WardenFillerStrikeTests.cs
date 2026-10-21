using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// WARDEN-FILLER (finding f-95d23cfc; BIBLE test 1 "five buttons, real decisions"; the core loop defers to WoW Mythic+,
/// where a tank's filler fires every global nothing better is ready for): in the default three-seat autorun (the run
/// SimDriver.StartRun builds, driven headlessly as <see cref="HostPathThreatTelegraphTests"/> drives it: the autorun
/// human <see cref="ScriptedWarden"/> plus the <see cref="BotCommandSource"/> seats), the Warden casts warden.strike at
/// least once in every <see cref="MaxGapTicks"/> ticks of the filler window.
/// <para>
/// The filler window: a tick on which the Warden is alive with an engaged enemy inside Strike's 3000 mm reach, outside
/// the GCD of a better button that actually fired (Reckoning, Vigil, Bulwark, or a Guard Break whose Payoff closed a
/// Window and fired a Combo). Off-GCD presses (Shield Bash, Taunt, Brace) take no global and do not pause it.
/// </para>
/// <para>
/// The numbers (content/data/abilities/warden.json): Strike has cooldownTicks 0, costs nothing and has gcdTicks 30, so in
/// the filler window it is ready on every global and the next Strike is due at most 30 ticks after the last one. N = 60
/// ticks (two globals) leaves one global of slack for the approach/facing tick and the step on which the window opens.
/// A rotation that presses the cooldown-free, cost-free Guard Break with no Window to close (main at 1f8fed7f: Strike
/// at ticks 106 and 647 only in the KF1 capture) fails it by hundreds of ticks.
/// </para>
/// </summary>
public sealed class WardenFillerStrikeTests
{
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";
    private const string Strike = "warden.strike";

    /// <summary>The run must cover at least this many ticks (150 s at 20 Hz): the filler is proven over a long run.</summary>
    private const int MinTicks = 3000;

    /// <summary>The run stops here at the latest.</summary>
    private const int MaxTicks = 6000;

    /// <summary>N: the longest filler-window span (ticks) allowed with no Strike: two 30-tick globals (see the summary).</summary>
    private const int MaxGapTicks = 60;

    /// <summary>Guard Break: a better button only on a global where its Payoff closed a Window (a "combo" event).</summary>
    private const string PayoffBuilder = "warden.guardBreak";

    /// <summary>The cooldown or resource buttons that outrank the filler whenever they fire.</summary>
    private static readonly string[] BetterButtons = { "warden.reckoning", "warden.vigil", "warden.bulwark" };

    /// <summary>Strike's reach (content/data/abilities/warden.json range 3000 mm): melee means an engaged enemy this close.</summary>
    private const long MeleeMm = 3000;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the cast counts and gaps are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public WardenFillerStrikeTests(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Warden filler (f-95d23cfc): default three-seat autorun casts warden.strike at least once per 60 filler-window ticks over 3000+ ticks")]
    public void Strike_is_the_filler_in_a_default_autorun()
    {
        var registry = Registry.Value;
        var seed = 1L;
        var setup = HostRunSetup.Build(registry, DungeonId, 2, HumanClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        Assert.Equal(3, party.Count);

        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();

        var backfill = BackfillState.Empty;
        var casts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var strikeTicks = new List<int>();
        var log = new List<(int Tick, string Ability)>();
        var meleeTicks = 0;
        var fillerTicks = 0;
        var payoffCombos = 0;
        var betterUntil = -1;
        var gap = 0;
        var maxGap = 0;
        var maxGapEnd = -1;
        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null)
        {
            var tick = run.State.Tick;
            var inMelee = run.State.Actors.TryGetValue(HostRunSetup.HumanId, out var me) && me.Alive
                && EngagedTargets.Nearest(run.State, me) is { } foe && Casting.RangeMm(me, foe) <= MeleeMm;

            var commands = new List<Command>();
            // SimDriver.StepOnce order: the human's commands first, then every bot seat's.
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;

            var combo = false;
            foreach (var e in step.Step.Events.Where(e => e.Type is "combo" or "combo_denied"))
            {
                if (e.Get("payoffActorId") is JsInt p && p.Value == HostRunSetup.HumanId)
                {
                    combo |= e.Type == "combo";
                    log.Add((tick, e.Type));
                    casts[e.Type] = casts.TryGetValue(e.Type, out var n) ? n + 1 : 1;
                }
            }

            var struck = false;
            foreach (var e in step.Step.Events.Where(e => e.Type == "cast_complete"))
            {
                if (e.Get("actorId") is JsInt a && a.Value == HostRunSetup.HumanId && e.Get("abilityId") is JsString id)
                {
                    casts[id.Value] = casts.TryGetValue(id.Value, out var n) ? n + 1 : 1;
                    log.Add((tick, id.Value));
                    if (id.Value == Strike)
                    {
                        struck = true;
                        strikeTicks.Add(tick);
                    }
                    else if (BetterButtons.Contains(id.Value) || (id.Value == PayoffBuilder && combo))
                    {
                        payoffCombos += id.Value == PayoffBuilder ? 1 : 0;
                        // A better button spent this global: its GCD is not filler time.
                        betterUntil = tick + setup.Content.Abilities[id.Value].GcdTicks;
                    }
                }
            }

            if (!inMelee || struck)
            {
                gap = 0;
                continue;
            }
            meleeTicks++;
            if (tick < betterUntil)
            {
                continue;
            }
            fillerTicks++;
            gap++;
            if (gap > maxGap)
            {
                maxGap = gap;
                maxGapEnd = tick;
            }
        }

        var endTick = run.State.Tick;
        _output.WriteLine($"WARDENFILLER endTick={endTick} result={run.RunEnd.Result?.ToString() ?? "none"} meleeTicks={meleeTicks} fillerTicks={fillerTicks} guardBreakCombos={payoffCombos} strikes={strikeTicks.Count} maxGap={maxGap} maxGapEnd={maxGapEnd}");
        _output.WriteLine("WARDENFILLER casts " + string.Join(" ", casts.Select(kv => $"{kv.Key}={kv.Value}")));
        _output.WriteLine("WARDENFILLER first strikes " + string.Join(",", strikeTicks.Take(20)));
        _output.WriteLine("WARDENFILLER worst gap " + string.Join(" ", log.Where(c => c.Tick > maxGapEnd - maxGap - 30 && c.Tick <= maxGapEnd + 30).Select(c => $"{c.Tick}:{c.Ability}")));

        // Non-vacuity: a long run with real melee time, or the gap bound proves nothing.
        Assert.True(endTick >= MinTicks, $"the default autorun ended at tick {endTick}, before {MinTicks}");
        Assert.True(fillerTicks >= MinTicks / 3, $"only {fillerTicks} filler-window ticks in {endTick}: the filler window was never exercised");
        // The Payoff decision survives: Strike never replaces a Guard Break that closes an ally's Window (synergy first).
        Assert.True(payoffCombos >= 1, $"no Guard Break closed a Window in {endTick} ticks: the Payoff press was lost, not prioritised");
        Assert.True(
            maxGap <= MaxGapTicks,
            $"warden.strike went {maxGap} filler-window ticks without a cast (ending tick {maxGapEnd}; bound {MaxGapTicks}); strikes={strikeTicks.Count} casts: "
            + string.Join(" ", casts.Select(kv => $"{kv.Key}={kv.Value}")));
    }
}
