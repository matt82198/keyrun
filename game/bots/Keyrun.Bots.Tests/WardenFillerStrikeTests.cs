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
/// least once in every <see cref="MaxGapTicks"/> ticks of the trash filler window.
/// <para>
/// The filler window: a tick on which the Warden is alive with an engaged enemy inside Strike's 3000 mm reach, outside
/// the boss pull, outside the GCD of a better button that actually fired (Reckoning, Vigil, Bulwark, or a Guard Break
/// whose Payoff closed a Window and fired a Combo). Off-GCD presses (Shield Bash, Taunt, Brace) take no global and do not
/// pause it.
/// </para>
/// <para>
/// The numbers (content/data/abilities/warden.json): Strike has cooldownTicks 0, costs nothing and has gcdTicks 30, so in
/// the filler window it is ready on every global and the next Strike is due at most 30 ticks after the last one. N = 60
/// ticks (two globals) leaves one global of slack for the approach/facing tick and the step on which the window opens.
/// A rotation that presses the cooldown-free, cost-free Guard Break with no Window to close (main at 1f8fed7f: Strike
/// at ticks 106 and 647 only in the KF1 capture) fails it by hundreds of ticks.
/// </para>
/// <para>
/// R-WARDEN-FILLER-3 (supersedes the withdrawn R-WARDEN-FILLER-2): the filler gate is a trash-pull rule. In the boss pull
/// the Warden keeps main's priority, Guard Break whenever ready: Strike-as-filler there opened a Sundered Window every
/// global and the Consecrate heals masked a careless seat (BossTeachesTests' careless run kept the +3 tier).
/// </para>
/// </summary>
public sealed class WardenFillerStrikeTests
{
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";
    private const string Strike = "warden.strike";

    /// <summary>The run must cover at least this many ticks (150 s at 20 Hz): the filler is proven over a long run.</summary>
    private const int MinTicks = 3000;

    /// <summary>
    /// Non-vacuity floor for trash filler-window ticks, now 800 (R-FACING-2 (c): measured 1011 without bot facing, 885
    /// with it). R-K4-13 (b) PR #665 (AoE kit): the kit's faster hollow-vault run measured 992 filler-window ticks in 7000
    /// (was 1000+ on main). A third of the run is still not allowed to be skipped; the cadence rule is measured, never skipped.
    /// </summary>
    // R-FACING-2 (c): filler ticks measure 1011 without bot facing (HEAD) and 885 with it, so the floor is 800 for both.
    private const int MinFillerTicks = 800;

    /// <summary>The run stops here at the latest (past the boss pull of the default run, which clears near tick 5800).</summary>
    private const int MaxTicks = 7000;

    /// <summary>N: the longest filler-window span (ticks) allowed with no Strike: two 30-tick globals (see the summary).</summary>
    private const int MaxGapTicks = 60;

    /// <summary>Guard Break: a better button only on a global where its Payoff closed a Window (a "combo" event).</summary>
    private const string PayoffBuilder = "warden.guardBreak";

    /// <summary>The cooldown or resource buttons that outrank the filler whenever they fire.</summary>
    private static readonly string[] BetterButtons = { "warden.reckoning", "warden.vigil", "warden.bulwark" };

    /// <summary>Strike's reach (content/data/abilities/warden.json range 3000 mm): melee means an engaged enemy this close.</summary>
    private const long MeleeMm = 3000;

    /// <summary>The boss half needs a real boss fight: at least this many boss-pull Guard Breaks.</summary>
    private const int MinBossGuardBreaks = 10;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    private static readonly Lazy<Drive> DefaultDrive = new(DriveDefaultRun);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the cast counts and gaps are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public WardenFillerStrikeTests(ITestOutputHelper output) => _output = output;

    /// <summary>What one default autorun measured.</summary>
    private sealed record Drive(
        int EndTick,
        string Result,
        int MeleeTicks,
        int FillerTicks,
        int PayoffCombos,
        int TrashStrikes,
        int MaxGap,
        int MaxGapEnd,
        int? BossStart,
        int BossGuardBreaks,
        int BossStrikes,
        int BossStrikesOverReadyGuardBreak,
        int LocksOpened,
        int LocksBroken,
        int LockHoldCasts,
        string Casts,
        string FirstStrikes,
        string WorstGap)
    {
        public override string ToString() =>
            $"WARDENFILLER endTick={EndTick} result={Result} meleeTicks={MeleeTicks} fillerTicks={FillerTicks} guardBreakCombos={PayoffCombos} " +
            $"trashStrikes={TrashStrikes} maxGap={MaxGap} maxGapEnd={MaxGapEnd} bossStart={BossStart?.ToString() ?? "-"} bossGuardBreaks={BossGuardBreaks} " +
            $"bossStrikes={BossStrikes} bossStrikesOverReadyGuardBreak={BossStrikesOverReadyGuardBreak} locksOpened={LocksOpened} locksBroken={LocksBroken} " +
            $"lockHoldCasts={LockHoldCasts}\nWARDENFILLER casts {Casts}\n" +
            $"WARDENFILLER first strikes {FirstStrikes}\nWARDENFILLER worst gap {WorstGap}";
    }

    /// <summary>True while the armed boss is alive and engaged (the boss pull, R-WARDEN-FILLER-3).</summary>
    private static bool InBossPull(Run run) =>
        run.State.Actors.TryGetValue(HostRunSetup.BossActorId, out var b) && b.Alive && !b.Idle;

    /// <summary>
    /// True while the boss casts a Lock, or holds one open that no Setup has tagged inside its Window (BIBLE s9 "The
    /// Lock"): R-WARDEN-FILLER-3's hold, when the Warden keeps his global for the setter's Setup.
    /// </summary>
    private static bool LockHoldTick(Run run) =>
        InBossPull(run) && run.Encounter.Boss is { } boss
        && ((boss.Active is { } open && run.State.Tick < open.EndsTick && open.SetupActorId is null)
            || (boss.Active is null && boss.Casting is not null));

    /// <summary>
    /// True when main's rotation would have pressed Guard Break on this tick: the Warden is off the GCD, Reckoning and
    /// Vigil are not castable, Guard Break is ready and its cast target is in reach (ScriptedWarden's range rule).
    /// </summary>
    private static bool GuardBreakWouldFire(Run run, ActorState me, int tick)
    {
        var abilities = run.Content.Abilities;
        if (!Cooldowns.IsGcdReady(me, tick))
        {
            return false;
        }
        var def = abilities[PayoffBuilder];
        if (!Cooldowns.IsAbilityReady(me, def, tick))
        {
            return false;
        }
        var target = CastTargeting.For(run, me.Id, PayoffBuilder);
        return run.State.Actors.TryGetValue(target, out var t) && t.Id != me.Id && Casting.RangeMm(me, t) <= def.Range;
    }

    private static Drive DriveDefaultRun()
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
        int meleeTicks = 0, fillerTicks = 0, payoffCombos = 0, betterUntil = -1, gap = 0, maxGap = 0, maxGapEnd = -1;
        int bossGuardBreaks = 0, bossStrikes = 0, bossStrikesOverGb = 0, locksOpened = 0, locksBroken = 0, lockHoldCasts = 0;
        int? bossStart = null;
        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null)
        {
            var tick = run.State.Tick;
            var alive = run.State.Actors.TryGetValue(HostRunSetup.HumanId, out var me) && me.Alive;
            var inBoss = InBossPull(run);
            bossStart ??= inBoss ? tick : null;
            var inMelee = alive && !inBoss
                && EngagedTargets.Nearest(run.State, me!) is { } foe && Casting.RangeMm(me!, foe) <= MeleeMm;
            var gbWouldFire = alive && inBoss && GuardBreakWouldFire(run, me!, tick);
            var lockHold = alive && LockHoldTick(run);

            var commands = new List<Command>();
            // SimDriver.StepOnce order: the human's commands first, then every bot seat's.
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;

            locksOpened += step.Step.Events.Count(e => e.Type == "lock_open");
            locksBroken += step.Step.Events.Count(e => e.Type == "lock_broken");
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
                    if (lockHold && setup.Content.Abilities[id.Value].GcdTicks > 0)
                    {
                        lockHoldCasts++;
                    }
                    if (inBoss)
                    {
                        bossGuardBreaks += id.Value == PayoffBuilder ? 1 : 0;
                        if (id.Value == Strike)
                        {
                            bossStrikes++;
                            bossStrikesOverGb += gbWouldFire ? 1 : 0;
                        }
                        continue;
                    }
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

        return new Drive(
            run.State.Tick,
            run.RunEnd.Result?.ToString() ?? "none",
            meleeTicks,
            fillerTicks,
            payoffCombos,
            strikeTicks.Count,
            maxGap,
            maxGapEnd,
            bossStart,
            bossGuardBreaks,
            bossStrikes,
            bossStrikesOverGb,
            locksOpened,
            locksBroken,
            lockHoldCasts,
            string.Join(" ", casts.Select(kv => $"{kv.Key}={kv.Value}")),
            string.Join(",", strikeTicks.Take(20)),
            string.Join(" ", log.Where(c => c.Tick > maxGapEnd - maxGap - 30 && c.Tick <= maxGapEnd + 30).Select(c => $"{c.Tick}:{c.Ability}")));
    }

    [Fact(DisplayName = "Warden filler (f-95d23cfc): default three-seat autorun casts warden.strike at least once per 60 trash filler-window ticks over 3000+ ticks")]
    public void Strike_is_the_filler_in_a_default_autorun()
    {
        var d = DefaultDrive.Value;
        _output.WriteLine(d.ToString());

        // Non-vacuity: a long run with real trash melee time, or the gap bound proves nothing.
        Assert.True(d.EndTick >= MinTicks, $"the default autorun ended at tick {d.EndTick}, before {MinTicks}");
        Assert.True(d.FillerTicks >= MinFillerTicks,$"only {d.FillerTicks} trash filler-window ticks in {d.EndTick}: the filler window was never exercised");
        // The Payoff decision survives: Strike never replaces a Guard Break that closes an ally's Window (synergy first).
        Assert.True(d.PayoffCombos >= 1, $"no Guard Break closed a Window in {d.EndTick} ticks: the Payoff press was lost, not prioritised");
        Assert.True(
            d.MaxGap <= MaxGapTicks,
            $"warden.strike went {d.MaxGap} trash filler-window ticks without a cast (ending tick {d.MaxGapEnd}; bound {MaxGapTicks}): {d}");
    }

    [Fact(DisplayName = "R-WARDEN-FILLER-3: in the boss pull the Warden keeps main's priority, Guard Break whenever ready, never Strike over it")]
    public void Boss_pull_keeps_guard_break_priority()
    {
        var d = DefaultDrive.Value;
        _output.WriteLine(d.ToString());

        // Non-vacuity: the boss pull ran and the Warden fought in it.
        Assert.True(d.BossStart is not null, $"the boss pull never started: {d}");
        Assert.True(d.BossGuardBreaks >= MinBossGuardBreaks, $"only {d.BossGuardBreaks} Guard Breaks in the boss pull, need {MinBossGuardBreaks}: {d}");
        // Main's priority: a Strike in the boss pull only on a global where Guard Break could not fire.
        Assert.True(
            d.BossStrikesOverReadyGuardBreak == 0,
            $"the Warden pressed Strike over a ready, in-reach Guard Break {d.BossStrikesOverReadyGuardBreak} times in the boss pull (the trash filler gate leaked into the boss fight): {d}");
    }

    [Fact(DisplayName = "R-WARDEN-FILLER-3: the Warden holds his global while a boss Lock waits for its Setup, and every Lock of the default run is broken")]
    public void Warden_holds_the_global_for_the_lock()
    {
        var d = DefaultDrive.Value;
        _output.WriteLine(d.ToString());

        Assert.True(d.LocksOpened >= 2, $"only {d.LocksOpened} Locks opened in the default run, need 2: {d}");
        Assert.True(d.LockHoldCasts == 0, $"the Warden spent {d.LockHoldCasts} globals while a Lock waited for its Setup: {d}");
        Assert.True(d.LocksBroken == d.LocksOpened, $"{d.LocksBroken} of {d.LocksOpened} Locks broken: {d}");
    }
}
